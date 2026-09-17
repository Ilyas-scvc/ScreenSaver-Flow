namespace FlowScreen.CommandLine;

public enum StartupMode
{
    /// <summary>Settings dialog. Also the mode used when launched with no arguments.</summary>
    Configure,

    /// <summary>Full screen on every display. <c>/s</c></summary>
    ScreenSaver,

    /// <summary>Miniature render parented into the Windows preview window. <c>/p HWND</c></summary>
    Preview,

    /// <summary>Windowed development mode with the statistics overlay. <c>/debug</c></summary>
    Debug,
}

/// <param name="Mode">What the process should do.</param>
/// <param name="TargetWindow">
/// The HWND supplied with <c>/p</c> (the preview surface) or with <c>/c</c> (the owner of the
/// settings dialog). <see cref="IntPtr.Zero"/> when none was given.
/// </param>
public sealed record StartupOptions(StartupMode Mode, IntPtr TargetWindow)
{
    public static StartupOptions Default { get; } = new(StartupMode.Configure, IntPtr.Zero);
}
