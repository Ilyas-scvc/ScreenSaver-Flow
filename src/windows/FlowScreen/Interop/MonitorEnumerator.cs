namespace FlowScreen.Interop;

/// <summary>One physical display, in physical (not DIP) virtual-desktop pixels.</summary>
public sealed record MonitorDescription(
    int Index,
    string DeviceName,
    int Left,
    int Top,
    int Width,
    int Height,
    uint Dpi,
    bool IsPrimary)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public double Scale => Dpi / 96.0;

    public override string ToString() =>
        $"#{Index} {DeviceName} {Width}x{Height} @ ({Left},{Top}) {Dpi}dpi{(IsPrimary ? " primary" : string.Empty)}";
}

/// <summary>The bounding box of every display, also in physical pixels.</summary>
public sealed record VirtualDesktop(int Left, int Top, int Width, int Height);

/// <summary>
/// Enumerates displays through the Win32 API rather than through WPF's
/// <c>SystemParameters</c>, because those report DIPs relative to the primary
/// monitor's scaling and therefore give wrong answers on mixed-DPI setups -
/// exactly the case a multi-monitor screensaver has to get right.
/// </summary>
public static class MonitorEnumerator
{
    public static IReadOnlyList<MonitorDescription> Enumerate()
    {
        var monitors = new List<MonitorDescription>();
        var index = 0;

        bool Callback(IntPtr handle, IntPtr hdc, ref RECT rect, IntPtr data)
        {
            var info = new MONITORINFOEX
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>(),
                szDevice = string.Empty,
            };

            if (!NativeMethods.GetMonitorInfo(handle, ref info)) return true;

            var dpi = 96u;
            try
            {
                if (NativeMethods.GetDpiForMonitor(handle, NativeMethods.MDT_EFFECTIVE_DPI, out var x, out _) == 0
                    && x > 0)
                {
                    dpi = x;
                }
            }
            catch (DllNotFoundException)
            {
                // shcore.dll is present on every supported OS; if it somehow is
                // not, 96 dpi is the right assumption.
            }

            monitors.Add(new MonitorDescription(
                index++,
                string.IsNullOrWhiteSpace(info.szDevice) ? $"DISPLAY{index}" : info.szDevice.TrimEnd('\0'),
                info.rcMonitor.Left,
                info.rcMonitor.Top,
                info.rcMonitor.Width,
                info.rcMonitor.Height,
                dpi,
                (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0));

            return true;
        }

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            // Should be unreachable, but a screensaver that shows nothing because
            // enumeration failed is a much worse outcome than one guessed window.
            var desktop = GetVirtualDesktop();
            monitors.Add(new MonitorDescription(
                0, "PRIMARY", desktop.Left, desktop.Top,
                Math.Max(desktop.Width, 640), Math.Max(desktop.Height, 480), 96, true));
        }

        // Primary first, then left-to-right. The primary window is the one that
        // takes keyboard focus, so it must be created first.
        return monitors
            .OrderByDescending(m => m.IsPrimary)
            .ThenBy(m => m.Left)
            .ThenBy(m => m.Top)
            .Select((m, i) => m with { Index = i })
            .ToList();
    }

    public static VirtualDesktop GetVirtualDesktop() => new(
        NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
        Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN)),
        Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN)));
}
