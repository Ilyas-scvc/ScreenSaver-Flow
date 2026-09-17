using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using FlowScreen.Hosting;
using FlowScreen.Interop;

namespace FlowScreen.Views;

/// <summary>
/// One borderless full-screen window covering exactly one physical display.
///
/// The window is deliberately positioned with <c>SetWindowPos</c> in physical
/// pixels rather than through WPF's DIP-based Left/Top/Width/Height: under
/// per-monitor DPI those two coordinate systems disagree, and the mismatch shows
/// up as a window that misses the edge of a scaled secondary display.
/// </summary>
public partial class SaverWindow : Window
{
    private readonly MonitorDescription monitor;
    private readonly bool isPrimary;

    public SaverWindow(MonitorDescription monitor, bool isPrimary)
    {
        this.monitor = monitor;
        this.isPrimary = isPrimary;

        InitializeComponent();

        // Only the focused window needs to be topmost-activated; making every one
        // of them steal activation causes them to fight on multi-monitor setups.
        Topmost = true;
        ShowActivated = isPrimary;
    }

    public MonitorDescription Monitor => monitor;

    /// <summary>Places the renderer surface into the window, above the placeholder.</summary>
    public void AttachRenderer(RendererHost host)
    {
        var view = host.View;
        Grid.SetZIndex(view, 1);
        HostRoot.Children.Add(view);

        host.RendererReady += (_, _) => Dispatcher.Invoke(() => Placeholder.Visibility = Visibility.Collapsed);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        PositionOnMonitor();
    }

    private void PositionOnMonitor()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        // Drop the frame styles before positioning, otherwise Windows reserves
        // room for a border that WindowStyle=None has already hidden and the
        // window ends up a few pixels short of the screen edge.
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_STYLE).ToInt64();
        style &= ~(long)(NativeMethods.WS_CAPTION | NativeMethods.WS_THICKFRAME
                         | NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_MAXIMIZEBOX
                         | NativeMethods.WS_SYSMENU | NativeMethods.WS_BORDER
                         | NativeMethods.WS_DLGFRAME);
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_STYLE, new IntPtr(style));

        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            monitor.Left,
            monitor.Top,
            monitor.Width,
            monitor.Height,
            NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_SHOWWINDOW
            | (isPrimary ? 0 : NativeMethods.SWP_NOACTIVATE));

        if (isPrimary) NativeMethods.SetForegroundWindow(handle);
    }
}
