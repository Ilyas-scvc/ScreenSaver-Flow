using System.IO;
using System.Text.Json;
using FlowScreen.Logging;
using FlowScreen.Settings;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace FlowScreen.Hosting;

public sealed record RendererInputEvent(string Kind, double Distance);

/// <summary>
/// Wraps a <see cref="WebView2"/> control and everything needed to point it at
/// the FiberFlow renderer: asset serving, hardening, message plumbing.
/// </summary>
public sealed class RendererHost : IAsyncDisposable
{
    /// <summary>
    /// Synthetic origin the embedded bundle is served from. It must be an https
    /// origin (not file://) so ES modules and the WebGL2 context behave the same
    /// as they do under the dev server.
    /// </summary>
    public const string VirtualHost = "flowscreen.assets";

    private const string DevServerUrl = "http://127.0.0.1:5173/";

    private readonly IAppLogger logger;
    private readonly EmbeddedRendererAssets assets;
    private readonly bool allowDevTools;

    private CoreWebView2Environment? environment;
    private bool initialised;

    public RendererHost(IAppLogger logger, EmbeddedRendererAssets assets, bool allowDevTools)
    {
        this.logger = logger;
        this.assets = assets;
        this.allowDevTools = allowDevTools;

        // WebView2 for WPF is an HwndHost, not a Control: DefaultBackgroundColor
        // is the only way to stop the white flash before the first paint.
        View = new WebView2
        {
            DefaultBackgroundColor = System.Drawing.Color.Black,
        };
    }

    public WebView2 View { get; }

    public event EventHandler<RendererInputEvent>? InputObserved;

    public event EventHandler? RendererReady;

    public event EventHandler<string>? RendererFailed;

    /// <summary>
    /// True when the app will load from the Vite dev server instead of the
    /// embedded bundle. Debug builds prefer the dev server so the visual can be
    /// iterated on without rebuilding the host.
    /// </summary>
    public static bool UseDevServer =>
#if DEBUG
        true;
#else
        false;
#endif

    public static string BaseUrl => UseDevServer ? DevServerUrl : $"https://{VirtualHost}/index.html";

    public async Task InitializeAsync(
        CoreWebView2Environment env,
        FlowScreenSettings settings,
        ViewContextDto view)
    {
        if (initialised) return;
        initialised = true;
        environment = env;

        await View.EnsureCoreWebView2Async(env).ConfigureAwait(true);

        var core = View.CoreWebView2;
        Harden(core);

        if (!UseDevServer)
        {
            core.AddWebResourceRequestedFilter(
                $"https://{VirtualHost}/*",
                CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += OnWebResourceRequested;
        }

        core.WebMessageReceived += OnWebMessageReceived;
        core.ProcessFailed += OnProcessFailed;

        var url = $"{BaseUrl}?cfg={RendererPayload.Encode(settings, view)}";
        logger.Info($"navigating monitor {view.MonitorIndex} to {BaseUrl}");
        core.Navigate(url);
    }

    private void Harden(CoreWebView2 core)
    {
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.AreDevToolsEnabled = allowDevTools;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsBuiltInErrorPageEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsWebMessageEnabled = true;
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (environment is null) return;

        try
        {
            var path = new Uri(e.Request.Uri).AbsolutePath;
            var stream = assets.Open(path);

            if (stream is null)
            {
                logger.Warn($"renderer asset not found: {path}");
                e.Response = environment.CreateWebResourceResponse(
                    null, 404, "Not Found", "Content-Type: text/plain");
                return;
            }

            // Copying into memory keeps the stream alive for as long as WebView2
            // needs it and avoids holding a manifest resource handle open.
            var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            stream.Dispose();
            buffer.Position = 0;

            var headers = string.Join(
                "\r\n",
                $"Content-Type: {EmbeddedRendererAssets.ContentTypeFor(path)}",
                "Cache-Control: no-store",
                "Access-Control-Allow-Origin: *");

            e.Response = environment.CreateWebResourceResponse(buffer, 200, "OK", headers);
        }
        catch (Exception ex)
        {
            logger.Error("failed to serve a renderer asset", ex);
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string raw;
        try
        {
            raw = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            raw = e.WebMessageAsJson;
        }

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var typeProperty)) return;

            switch (typeProperty.GetString())
            {
                case "ready":
                    logger.Info("renderer reported its first frame");
                    RendererReady?.Invoke(this, EventArgs.Empty);
                    break;

                case "input":
                {
                    var kind = root.TryGetProperty("kind", out var k) ? k.GetString() ?? "unknown" : "unknown";
                    var distance = root.TryGetProperty("distance", out var d) && d.TryGetDouble(out var value)
                        ? value
                        : 0;
                    InputObserved?.Invoke(this, new RendererInputEvent(kind, distance));
                    break;
                }

                case "error":
                {
                    var message = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                    logger.Error($"renderer reported an error: {message}");
                    RendererFailed?.Invoke(this, message ?? "unknown renderer error");
                    break;
                }

                case "contextLost":
                    logger.Error("renderer lost its WebGL context");
                    RendererFailed?.Invoke(this, "The GPU context was lost.");
                    break;
            }
        }
        catch (JsonException ex)
        {
            logger.Warn($"unparsable renderer message: {raw}", ex);
        }
    }

    private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        logger.Error($"WebView2 process failed: {e.ProcessFailedKind} ({e.Reason})");
        RendererFailed?.Invoke(this, $"The rendering process stopped ({e.ProcessFailedKind}).");
    }

    /// <summary>Pushes changed settings into a running renderer without a reload.</summary>
    public void PostSettings(FlowScreenSettings settings)
    {
        var core = View.CoreWebView2;
        if (core is null) return;

        try
        {
            core.PostWebMessageAsString(RendererPayload.SettingsMessage(settings));
        }
        catch (Exception ex)
        {
            logger.Warn("failed to post settings to the renderer", ex);
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            var core = View.CoreWebView2;
            if (core is not null)
            {
                core.WebMessageReceived -= OnWebMessageReceived;
                core.ProcessFailed -= OnProcessFailed;
                if (!UseDevServer) core.WebResourceRequested -= OnWebResourceRequested;
            }

            View.Dispose();
        }
        catch (Exception ex)
        {
            logger.Warn("error while disposing the renderer host", ex);
        }

        return ValueTask.CompletedTask;
    }
}
