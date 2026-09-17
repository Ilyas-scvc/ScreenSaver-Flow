using System.Windows;
using System.Windows.Threading;
using FlowScreen.CommandLine;
using FlowScreen.Diagnostics;
using FlowScreen.Hosting;
using FlowScreen.Logging;
using FlowScreen.Screensaver;
using FlowScreen.Settings;
using FlowScreen.Views;

namespace FlowScreen;

public partial class App : Application
{
    private const int ExitOk = 0;
    private const int ExitNoRuntime = 2;
    private const int ExitFailed = 3;

    private IAppLogger logger = NullLogger.Instance;
    private SettingsService settingsService = null!;
    private RendererEnvironment environment = null!;
    private EmbeddedRendererAssets assets = null!;
    private ScreenSaverController? saver;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var options = CommandLineParser.Parse(e.Args);
        logger = FileLogger.Create(options.Mode.ToString().ToLowerInvariant());
        InstallExceptionHandlers();

        logger.Info($"FlowScreen starting: mode={options.Mode}, args=[{string.Join(' ', e.Args)}]");

        settingsService = new SettingsService(logger);
        assets = new EmbeddedRendererAssets();
        environment = new RendererEnvironment(logger);

        logger.Info($"renderer assets embedded: {assets.Count} file(s), dev server: {RendererHost.UseDevServer}");

        var settings = settingsService.Load();

        switch (options.Mode)
        {
            case StartupMode.ScreenSaver:
                _ = RunScreenSaverAsync(settings);
                break;

            case StartupMode.Preview:
                _ = RunPreviewAsync(settings, options.TargetWindow);
                break;

            case StartupMode.Debug:
                settings.ShowDebugOverlay = true;
                RunConfigure(settings);
                break;

            default:
                RunConfigure(settings);
                break;
        }
    }

    // ------------------------------------------------------------- modes ----

    private async Task RunScreenSaverAsync(FlowScreenSettings settings)
    {
        var status = WebView2Runtime.Detect();
        if (!status.IsInstalled)
        {
            // Silently, and immediately. A screensaver has nobody to show a
            // dialog to, and hanging on a black screen looks like a crashed PC.
            logger.Error($"cannot start the screensaver, WebView2 runtime missing: {status.Error}");
            Shutdown(ExitNoRuntime);
            return;
        }

        if (!RendererHost.UseDevServer && !assets.IsAvailable)
        {
            logger.Error("cannot start the screensaver, the renderer bundle is not embedded");
            Shutdown(ExitFailed);
            return;
        }

        if (settings.RandomSeedOnLaunch)
        {
            settings.Seed = Random.Shared.Next(1, int.MaxValue);
            logger.Info($"random seed for this session: {settings.Seed}");
        }

        try
        {
            saver = new ScreenSaverController(logger, settings, environment, assets);
            saver.ExitRequested += async (_, reason) => await EndSessionAsync(reason).ConfigureAwait(true);
            await saver.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.Error("the screensaver session failed to start", ex);
            await EndSessionAsync("startup-failure").ConfigureAwait(true);
            Shutdown(ExitFailed);
        }
    }

    private async Task RunPreviewAsync(FlowScreenSettings settings, IntPtr parent)
    {
        var status = WebView2Runtime.Detect();
        if (!status.IsInstalled)
        {
            logger.Error("preview cannot render, WebView2 runtime missing");
            Shutdown(ExitNoRuntime);
            return;
        }

        try
        {
            var window = new PreviewWindow(parent, logger);
            MainWindow = window;

            // The preview surface is roughly 152x112: full density there would be
            // wasted work, and a second WebView2 competing with the settings
            // dialog for the GPU makes the whole page feel sluggish.
            var preview = settings.Clone();
            preview.Density = DensityLevel.Low;
            preview.RenderScale = 0.75;
            preview.FpsLimit = 30;
            preview.AdaptiveQuality = AdaptiveQualityMode.Performance;
            preview.ShowDebugOverlay = false;
            preview.RandomSeedOnLaunch = false;
            preview.Normalize();

            var host = new RendererHost(logger, assets, allowDevTools: false);
            window.AttachRenderer(host);
            window.Show();

            var (width, height) = window.ParentClientSize;
            var env = await environment.GetOrCreateAsync(preview).ConfigureAwait(true);
            await host.InitializeAsync(
                env,
                preview,
                ViewContextDto.Standalone(width, height, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            logger.Error("preview failed", ex);
            Shutdown(ExitFailed);
        }
    }

    private void RunConfigure(FlowScreenSettings settings)
    {
        var window = new ConfigWindow(logger, settingsService, environment, assets, settings);
        MainWindow = window;
        window.Closed += (_, _) => Shutdown(ExitOk);
        window.Show();
    }

    // ------------------------------------------------------------ teardown --

    private async Task EndSessionAsync(string reason)
    {
        logger.Info($"ending session: {reason}");

        if (saver is not null)
        {
            var controller = saver;
            saver = null;
            await controller.DisposeAsync().ConfigureAwait(true);
        }

        Shutdown(ExitOk);
    }

    // ---------------------------------------------------------- exceptions --

    private void InstallExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            logger.Error("unhandled dispatcher exception", args.Exception);
            // Keeping the process alive after a UI-thread fault would leave a
            // black full-screen window with no way out, so let it terminate.
            args.Handled = false;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            logger.Error("unhandled domain exception", args.ExceptionObject as Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.Error("unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        logger.Info($"FlowScreen exiting with code {e.ApplicationExitCode}");
        (logger as IDisposable)?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Convenience for code that needs to force a shutdown from a non-UI thread.</summary>
    public static void RequestShutdown(int exitCode = ExitOk) =>
        Current?.Dispatcher.BeginInvoke(DispatcherPriority.Normal, () => Current.Shutdown(exitCode));
}
