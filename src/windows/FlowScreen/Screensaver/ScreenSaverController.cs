using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using FlowScreen.Hosting;
using FlowScreen.Input;
using FlowScreen.Interop;
using FlowScreen.Logging;
using FlowScreen.Settings;
using FlowScreen.Views;

namespace FlowScreen.Screensaver;

/// <summary>
/// Runs the full-screen screensaver session: one window per display, one shared
/// WebView2 environment, one input watcher, and a single well-defined way out.
/// </summary>
public sealed class ScreenSaverController : IAsyncDisposable
{
    private readonly IAppLogger logger;
    private readonly FlowScreenSettings settings;
    private readonly RendererEnvironment environment;
    private readonly EmbeddedRendererAssets assets;
    private readonly InputWatcher input;

    private readonly List<SaverWindow> windows = [];
    private readonly List<RendererHost> hosts = [];

    private readonly DispatcherTimer watchdog;

    private int hiddenCursorCount;
    private int readyCount;
    private bool exiting;

    public ScreenSaverController(
        IAppLogger logger,
        FlowScreenSettings settings,
        RendererEnvironment environment,
        EmbeddedRendererAssets assets)
    {
        this.logger = logger;
        this.settings = settings;
        this.environment = environment;
        this.assets = assets;

        input = new InputWatcher(logger);
        input.Activity += (_, activity) => RequestExit($"{activity.Source}/{activity.Kind}");

        // If nothing has rendered by the time this fires, something is wrong with
        // the GPU stack or the WebView. Exiting is the right answer: a
        // permanently black screen with no way out is indistinguishable from a
        // frozen machine.
        watchdog = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(25),
        };
        watchdog.Tick += (_, _) =>
        {
            watchdog.Stop();
            if (readyCount == 0) RequestExit("watchdog/no-first-frame");
        };
    }

    public event EventHandler<string>? ExitRequested;

    public async Task StartAsync()
    {
        var monitors = MonitorEnumerator.Enumerate();
        var desktop = MonitorEnumerator.GetVirtualDesktop();
        var synchronized = settings.MultiMonitorMode == MultiMonitorMode.Synchronized;

        // One epoch for the whole session. In synchronized mode this is what
        // makes independent browser processes agree on t=0 without any IPC.
        var epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        logger.Info(
            $"starting on {monitors.Count} monitor(s), virtual desktop "
            + $"{desktop.Width}x{desktop.Height} at ({desktop.Left},{desktop.Top}), "
            + $"mode={settings.MultiMonitorMode}");

        HideCursor();

        var env = await environment.GetOrCreateAsync(settings).ConfigureAwait(true);

        foreach (var monitor in monitors)
        {
            logger.Info($"monitor {monitor}");

            var window = new SaverWindow(monitor, monitor.IsPrimary);
            window.KeyDown += OnWindowKeyDown;
            window.MouseDown += (_, _) => RequestExit("window/mouse-button");
            windows.Add(window);

            var host = new RendererHost(logger, assets, allowDevTools: false);
            host.RendererReady += (_, _) => readyCount++;
            host.InputObserved += (_, e) => input.ReportRendererInput(e.Kind, e.Distance);
            host.RendererFailed += (_, message) => OnRendererFailed(message);
            hosts.Add(host);

            window.AttachRenderer(host);
            window.Show();

            var perMonitor = BuildSettingsFor(monitor, synchronized);
            var view = ViewContextDto.For(monitor, monitors.Count, desktop, synchronized, epoch);

            await host.InitializeAsync(env, perMonitor, view).ConfigureAwait(true);
        }

        input.Start();
        watchdog.Start();
    }

    /// <summary>
    /// In synchronized mode every display shares the seed, so together with the
    /// camera view offset they render one continuous scene across the bezels. In
    /// independent mode each display gets its own world.
    /// </summary>
    private FlowScreenSettings BuildSettingsFor(MonitorDescription monitor, bool synchronized)
    {
        var copy = settings.Clone();
        copy.RandomSeedOnLaunch = false;
        if (!synchronized)
        {
            copy.Seed = unchecked(settings.Seed + (monitor.Index * 7919)) & 0x7FFFFFFF;
        }

        return copy.Normalize();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // The low-level hook normally sees this first; this path covers the
        // window between Show() and the hook being armed, and the case where the
        // OS refused to install hooks at all.
        RequestExit(e.Key == Key.Escape ? "window/escape" : "window/key");
    }

    private void OnRendererFailed(string message)
    {
        logger.Error($"renderer failure, ending the session: {message}");
        // Leaving a black screen up after the renderer dies would look exactly
        // like a hung machine, which is worse than simply exiting.
        RequestExit("renderer/failure");
    }

    private void RequestExit(string reason)
    {
        if (exiting) return;
        exiting = true;
        logger.Info($"exiting: {reason}");
        ExitRequested?.Invoke(this, reason);
    }

    private void HideCursor()
    {
        // ShowCursor keeps a counter, not a flag: it has to go negative before
        // the cursor actually disappears.
        for (var i = 0; i < 8 && NativeMethods.ShowCursor(false) >= 0; i++)
        {
            hiddenCursorCount++;
        }
    }

    private void RestoreCursor()
    {
        while (hiddenCursorCount > 0)
        {
            NativeMethods.ShowCursor(true);
            hiddenCursorCount--;
        }
    }

    public async ValueTask DisposeAsync()
    {
        watchdog.Stop();
        input.Dispose();
        RestoreCursor();

        foreach (var host in hosts)
        {
            await host.DisposeAsync().ConfigureAwait(true);
        }

        hosts.Clear();

        foreach (var window in windows)
        {
            try
            {
                window.KeyDown -= OnWindowKeyDown;
                window.Close();
            }
            catch (Exception ex)
            {
                logger.Warn("failed to close a saver window", ex);
            }
        }

        windows.Clear();
    }
}
