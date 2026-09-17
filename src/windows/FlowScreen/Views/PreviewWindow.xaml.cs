using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using FlowScreen.Hosting;
using FlowScreen.Interop;
using FlowScreen.Logging;
using Timer = System.Threading.Timer;

namespace FlowScreen.Views;

/// <summary>
/// The miniature render inside the Screen Saver settings dialog (<c>/p HWND</c>).
///
/// Windows hands us a HWND that belongs to another process. The window is
/// re-styled as a child and re-parented into it, which is the documented
/// contract for screensaver previews.
///
/// Nothing tells us when the settings dialog closes, so the parent is polled.
/// The poll deliberately runs on a thread-pool timer rather than a
/// <c>DispatcherTimer</c>: destroying the parent destroys our re-parented child
/// with it, and from that moment the WPF dispatcher stops delivering work
/// entirely. A dispatcher timer would simply never fire again, and the process
/// would linger forever holding a WebView2 browser alive - which is exactly what
/// an earlier version of this class did.
/// </summary>
public partial class PreviewWindow : Window
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>How long an orderly shutdown gets before the process is forced out.</summary>
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromMilliseconds(1200);

    private readonly IntPtr parentHandle;
    private readonly IAppLogger logger;
    private readonly Timer parentWatch;

    private IntPtr ownHandle;
    private int stopping;

    public PreviewWindow(IntPtr parentHandle, IAppLogger logger)
    {
        this.parentHandle = parentHandle;
        this.logger = logger;

        InitializeComponent();

        parentWatch = new Timer(_ => PollParent(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void AttachRenderer(RendererHost host)
    {
        var view = host.View;
        Grid.SetZIndex(view, 1);
        HostRoot.Children.Add(view);
        host.RendererReady += (_, _) => Dispatcher.Invoke(() => Placeholder.Visibility = Visibility.Collapsed);
    }

    /// <summary>Client size of the preview surface, in physical pixels.</summary>
    public (int Width, int Height) ParentClientSize =>
        NativeMethods.GetClientRect(parentHandle, out var rect) && rect.Width > 0 && rect.Height > 0
            ? (rect.Width, rect.Height)
            : (152, 112);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var handle = new WindowInteropHelper(this).Handle;
        ownHandle = handle;

        if (handle == IntPtr.Zero || !NativeMethods.IsWindow(parentHandle))
        {
            logger.Warn("preview parent handle is not a window; closing");
            Application.Current?.Shutdown();
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_STYLE).ToInt64();
        style &= ~(long)(NativeMethods.WS_POPUP | NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME
                         | NativeMethods.WS_SYSMENU | NativeMethods.WS_MINIMIZEBOX
                         | NativeMethods.WS_MAXIMIZEBOX | NativeMethods.WS_BORDER
                         | NativeMethods.WS_DLGFRAME);
        style |= NativeMethods.WS_CHILD | NativeMethods.WS_VISIBLE;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_STYLE, new IntPtr(style));

        var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        exStyle &= ~(long)NativeMethods.WS_EX_APPWINDOW;
        exStyle |= NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

        NativeMethods.SetParent(handle, parentHandle);

        var (width, height) = ParentClientSize;
        NativeMethods.SetWindowPos(
            handle, IntPtr.Zero, 0, 0, width, height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE
            | NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_FRAMECHANGED);

        // The reliable signal. Destroying the settings dialog destroys this
        // window too, and WM_DESTROY is the last moment at which the runtime is
        // still healthy enough to act: measurements show that immediately
        // afterwards the process stops executing managed code altogether, so
        // neither a dispatcher timer nor a thread-pool timer ever runs again.
        HwndSource.FromHwnd(handle)?.AddHook(OnWindowMessage);

        logger.Info($"preview parented into 0x{parentHandle.ToInt64():X} at {width}x{height}");

        // Belt and braces for a parent that disappears without destroying us.
        parentWatch.Change(PollInterval, PollInterval);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is NativeMethods.WM_DESTROY or NativeMethods.WM_NCDESTROY
            && Interlocked.Exchange(ref stopping, 1) == 0)
        {
            parentWatch.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            logger.Info("preview window destroyed, shutting down");
            ExitProcess();
        }

        return IntPtr.Zero;
    }

    private void PollParent()
    {
        if (Volatile.Read(ref stopping) != 0) return;

        // Either handle can go first: destroying the settings dialog destroys our
        // child window with it, and depending on ordering one can still test
        // valid at the moment the other has already gone.
        var parentAlive = NativeMethods.IsWindow(parentHandle);
        var selfAlive = ownHandle == IntPtr.Zero || NativeMethods.IsWindow(ownHandle);
        if (parentAlive && selfAlive) return;

        if (Interlocked.Exchange(ref stopping, 1) != 0) return;

        parentWatch.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        logger.Info(parentAlive
            ? "preview window was destroyed with its parent, shutting down"
            : "preview parent window is gone, shutting down");

        ExitProcess();
    }

    /// <summary>
    /// Asks for an orderly WPF shutdown, then guarantees the exit from the thread
    /// pool. The guarantee is the whole point: by the time this runs the
    /// dispatcher is usually already dead and would never process the request.
    /// </summary>
    private void ExitProcess()
    {
        try
        {
            var app = Application.Current;
            app?.Dispatcher.BeginInvoke(new Action(() => app.Shutdown()));
        }
        catch (Exception ex)
        {
            logger.Warn("orderly preview shutdown failed", ex);
        }

        _ = Task.Delay(ShutdownGrace).ContinueWith(
            _ => Environment.Exit(0),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    protected override void OnClosed(EventArgs e)
    {
        Volatile.Write(ref stopping, 1);
        parentWatch.Dispose();
        base.OnClosed(e);
    }
}
