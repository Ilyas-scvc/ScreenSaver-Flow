using System.Diagnostics;
using System.Runtime.InteropServices;
using FlowScreen.Interop;
using FlowScreen.Logging;

namespace FlowScreen.Input;

public sealed record ActivityDetected(string Source, string Kind, double Distance);

/// <summary>
/// Decides when the user has actually asked the screensaver to go away.
///
/// Two independent sources feed it:
///
/// 1. Low-level Win32 hooks. These see input even while the WebView owns the
///    focus, and they still work before the first frame has rendered.
/// 2. Messages posted by the renderer page, because WebView2 consumes input
///    inside its own HWND and there is no guarantee a hook can be installed
///    (group policy, security software).
///
/// Both go through the same threshold logic, and the mouse threshold is the
/// point of the class: laser mice, drifting trackballs and a nudged desk all
/// generate WM_MOUSEMOVE with no human involved, and a screensaver that dies on
/// a one-pixel jitter is the single most common complaint about the genre.
/// </summary>
public sealed class InputWatcher : IDisposable
{
    private readonly IAppLogger logger;
    private readonly double moveThreshold;
    private readonly TimeSpan grace;
    private readonly Stopwatch since = Stopwatch.StartNew();

    // The delegates must be rooted for as long as the hooks are installed: if
    // the GC collects them, the next input event calls into freed memory.
    private readonly HookProc keyboardProc;
    private readonly HookProc mouseProc;

    private IntPtr keyboardHook;
    private IntPtr mouseHook;

    private POINT origin;
    private bool hasOrigin;
    private bool triggered;
    private bool disposed;

    public InputWatcher(IAppLogger logger, double moveThresholdPixels = 8.0, TimeSpan? gracePeriod = null)
    {
        this.logger = logger;
        moveThreshold = Math.Max(0, moveThresholdPixels);
        grace = gracePeriod ?? TimeSpan.FromMilliseconds(900);

        keyboardProc = KeyboardHook;
        mouseProc = MouseHook;
    }

    public event EventHandler<ActivityDetected>? Activity;

    /// <summary>True once the hooks are installed, false if the OS refused.</summary>
    public bool HooksInstalled => keyboardHook != IntPtr.Zero || mouseHook != IntPtr.Zero;

    public void Start()
    {
        var module = NativeMethods.GetModuleHandleW(null);

        keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, keyboardProc, module, 0);
        mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, mouseProc, module, 0);

        if (!HooksInstalled)
        {
            logger.Warn("low-level input hooks could not be installed; relying on renderer-reported input only");
        }
        else
        {
            logger.Info($"input watcher armed (threshold {moveThreshold}px, grace {grace.TotalMilliseconds}ms)");
        }
    }

    /// <summary>Input observed by the renderer page and forwarded by the host.</summary>
    public void ReportRendererInput(string kind, double distance)
    {
        if (kind is "mouse-move")
        {
            if (distance >= moveThreshold) Trigger("renderer", kind, distance);
            return;
        }

        Trigger("renderer", kind, distance);
    }

    private void Trigger(string source, string kind, double distance)
    {
        if (triggered || disposed) return;
        if (since.Elapsed < grace) return;

        triggered = true;
        logger.Info($"exit requested by {source}: {kind} (distance {distance:F1}px)");
        Activity?.Invoke(this, new ActivityDetected(source, kind, distance));
    }

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == NativeMethods.HC_ACTION)
        {
            var message = wParam.ToInt32();
            if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                Trigger("hook", "key", 0);
            }
        }

        return NativeMethods.CallNextHookEx(keyboardHook, code, wParam, lParam);
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == NativeMethods.HC_ACTION)
        {
            switch (wParam.ToInt32())
            {
                case NativeMethods.WM_MOUSEMOVE:
                    HandleMove(lParam);
                    break;

                case NativeMethods.WM_LBUTTONDOWN:
                case NativeMethods.WM_RBUTTONDOWN:
                case NativeMethods.WM_MBUTTONDOWN:
                case NativeMethods.WM_XBUTTONDOWN:
                case NativeMethods.WM_MOUSEWHEEL:
                    Trigger("hook", "mouse-button", 0);
                    break;
            }
        }

        return NativeMethods.CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private void HandleMove(IntPtr lParam)
    {
        MSLLHOOKSTRUCT data;
        try
        {
            data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
        }
        catch (Exception)
        {
            return;
        }

        // The first position we see is the reference, not an event. Capturing it
        // lazily rather than at Start() also absorbs the cursor warp that Windows
        // performs when the saver takes over the desktop.
        if (!hasOrigin)
        {
            origin = data.pt;
            hasOrigin = true;
            return;
        }

        var dx = (double)(data.pt.X - origin.X);
        var dy = (double)(data.pt.Y - origin.Y);
        var distance = Math.Sqrt((dx * dx) + (dy * dy));

        if (distance >= moveThreshold) Trigger("hook", "mouse-move", distance);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(keyboardHook);
            keyboardHook = IntPtr.Zero;
        }

        if (mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(mouseHook);
            mouseHook = IntPtr.Zero;
        }
    }
}
