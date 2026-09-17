using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FlowScreen.Interop;

/// <summary>
/// Switches a window's non-client area to the dark theme.
///
/// WPF has no API for this, so a light title bar is stapled to the top of an
/// otherwise dark dialog unless DWM is told otherwise. Best effort throughout:
/// the attribute id changed during Windows 10's life, and on anything older than
/// build 17763 neither value exists.
/// </summary>
public static partial class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkModeLegacy = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;

            var enabled = 1;
            if (DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref enabled, sizeof(int)) != 0)
            {
                DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkModeLegacy, ref enabled, sizeof(int));
            }
        }
        catch (Exception)
        {
            // Cosmetic only. Never worth failing a window over.
        }
    }
}
