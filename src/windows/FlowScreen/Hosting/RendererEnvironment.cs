using System.IO;
using FlowScreen.Logging;
using FlowScreen.Settings;
using Microsoft.Web.WebView2.Core;

namespace FlowScreen.Hosting;

/// <summary>
/// Owns the single <see cref="CoreWebView2Environment"/> shared by every window.
///
/// One environment means one browser process for the whole screensaver: four
/// displays cost four renderer processes, not four complete browsers.
/// </summary>
public sealed class RendererEnvironment
{
    private readonly IAppLogger logger;
    private CoreWebView2Environment? environment;

    public RendererEnvironment(IAppLogger logger) => this.logger = logger;

    public CoreWebView2Environment? Current => environment;

    public async Task<CoreWebView2Environment> GetOrCreateAsync(FlowScreenSettings settings)
    {
        if (environment is not null) return environment;

        var userData = AppPaths.WebViewUserData;
        Directory.CreateDirectory(userData);

        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = BuildBrowserArguments(settings),
            AreBrowserExtensionsEnabled = false,
        };

        environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userData,
            options: options).ConfigureAwait(true);

        logger.Info($"WebView2 runtime {environment.BrowserVersionString}");
        return environment;
    }

    /// <summary>
    /// Chromium switches that matter for a screensaver.
    ///
    /// The throttling switches are the important ones: Chromium aggressively
    /// slows timers and rAF in windows it believes are backgrounded or occluded,
    /// and a full-screen borderless window over the taskbar trips that heuristic
    /// often enough to stall the animation.
    /// </summary>
    private static string BuildBrowserArguments(FlowScreenSettings settings)
    {
        var args = new List<string>
        {
            "--disable-background-timer-throttling",
            "--disable-renderer-backgrounding",
            "--disable-backgrounding-occluded-windows",
            "--disable-features=CalculateNativeWinOcclusion",
            "--disable-pinch",
            "--enable-gpu-rasterization",
            "--force_high_performance_gpu",
        };

        // The page cannot switch vsync off from inside requestAnimationFrame;
        // only the compositor can, so the setting has to be applied here.
        if (!settings.Vsync)
        {
            args.Add("--disable-gpu-vsync");
            args.Add("--disable-frame-rate-limit");
        }

        return string.Join(' ', args);
    }
}
