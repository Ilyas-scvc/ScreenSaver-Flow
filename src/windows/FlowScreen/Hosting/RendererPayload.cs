using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using FlowScreen.Interop;
using FlowScreen.Settings;

namespace FlowScreen.Hosting;

public sealed record ViewRect(int X, int Y, int W, int H);

/// <summary>
/// Per-window context handed to the renderer. Mirrors <c>ViewContext</c> in
/// <c>src/renderer/src/config/types.ts</c>.
/// </summary>
public sealed class ViewContextDto
{
    public int MonitorIndex { get; init; }

    public int MonitorCount { get; init; } = 1;

    public bool Synchronized { get; init; }

    /// <summary>
    /// Shared wall-clock origin in Unix milliseconds. Every window in a
    /// synchronized session gets the same value, which is what makes their
    /// simulations agree without any cross-process messaging.
    /// </summary>
    public long EpochMs { get; init; }

    public ViewRect Bounds { get; init; } = new(0, 0, 1920, 1080);

    public ViewRect Virtual { get; init; } = new(0, 0, 1920, 1080);

    public static ViewContextDto For(
        MonitorDescription monitor,
        int monitorCount,
        VirtualDesktop desktop,
        bool synchronized,
        long epochMs) => new()
        {
            MonitorIndex = monitor.Index,
            MonitorCount = monitorCount,
            Synchronized = synchronized,
            EpochMs = epochMs,
            Bounds = new ViewRect(monitor.Left, monitor.Top, monitor.Width, monitor.Height),
            Virtual = new ViewRect(desktop.Left, desktop.Top, desktop.Width, desktop.Height),
        };

    public static ViewContextDto Standalone(int width, int height, long epochMs) => new()
    {
        MonitorIndex = 0,
        MonitorCount = 1,
        Synchronized = false,
        EpochMs = epochMs,
        Bounds = new ViewRect(0, 0, width, height),
        Virtual = new ViewRect(0, 0, width, height),
    };
}

internal sealed class RendererPayloadDto
{
    public required FlowScreenSettings Settings { get; init; }

    public required ViewContextDto View { get; init; }
}

/// <summary>
/// Encodes the startup payload into the navigation URL.
///
/// A query parameter is used rather than a post-load message so the renderer has
/// its settings before the first frame - otherwise the first second of the
/// screensaver would be rendered with defaults and then visibly snap.
/// </summary>
public static class RendererPayload
{
    private static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    public static string Serialize(FlowScreenSettings settings, ViewContextDto view) =>
        JsonSerializer.Serialize(new RendererPayloadDto { Settings = settings, View = view }, Options);

    /// <summary>Base64url, so the value is safe in a query string with no escaping.</summary>
    public static string Encode(FlowScreenSettings settings, ViewContextDto view)
    {
        var json = Serialize(settings, view);
        var bytes = Encoding.UTF8.GetBytes(json);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>Payload for a live settings update, sent over the WebView message channel.</summary>
    public static string SettingsMessage(FlowScreenSettings settings) =>
        JsonSerializer.Serialize(new { type = "settings", settings }, Options);
}
