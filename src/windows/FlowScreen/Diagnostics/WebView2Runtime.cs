using Microsoft.Web.WebView2.Core;

namespace FlowScreen.Diagnostics;

public sealed record WebView2Status(bool IsInstalled, string? Version, string? Error)
{
    public const string DownloadUrl = "https://developer.microsoft.com/microsoft-edge/webview2/";

    public string UserMessage => IsInstalled
        ? $"WebView2 Runtime {Version}"
        : "The Microsoft Edge WebView2 Runtime is not installed. FlowScreen renders through it, "
          + $"so the screensaver cannot start without it.\n\nInstall it from:\n{DownloadUrl}";
}

/// <summary>
/// Detects the WebView2 Runtime before any window is created.
///
/// This matters because the failure mode without it is silent: WebView2 simply
/// never initialises, and a screensaver would sit on a black screen forever
/// while the user assumes their machine has hung.
/// </summary>
public static class WebView2Runtime
{
    public static WebView2Status Detect()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(version)
                ? new WebView2Status(false, null, "No WebView2 Runtime version was reported.")
                : new WebView2Status(true, version, null);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            return new WebView2Status(false, null, ex.Message);
        }
        catch (Exception ex)
        {
            return new WebView2Status(false, null, ex.Message);
        }
    }
}
