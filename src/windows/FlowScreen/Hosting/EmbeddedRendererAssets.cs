using System.IO;
using System.Reflection;

namespace FlowScreen.Hosting;

/// <summary>
/// Serves the Vite bundle out of the assembly's manifest resources.
///
/// The alternative - <c>SetVirtualHostNameToFolderMapping</c> onto a folder next
/// to the executable - would mean the published screensaver is a directory, and
/// Windows only lists <c>.scr</c> files that sit directly in System32. Embedding
/// keeps the deliverable a single file.
/// </summary>
public sealed class EmbeddedRendererAssets
{
    private const string Prefix = "renderer/";

    private readonly Assembly assembly;
    private readonly Dictionary<string, string> map;

    public EmbeddedRendererAssets(Assembly? assembly = null)
    {
        this.assembly = assembly ?? Assembly.GetExecutingAssembly();
        map = this.assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                n => Normalize(n[Prefix.Length..]),
                n => n,
                StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAvailable => map.ContainsKey("index.html");

    public int Count => map.Count;

    public IEnumerable<string> Paths => map.Keys;

    /// <summary>Returns null when the path is not part of the bundle.</summary>
    public Stream? Open(string relativePath)
    {
        var key = Normalize(relativePath);
        if (key.Length == 0) key = "index.html";
        return map.TryGetValue(key, out var resource) ? assembly.GetManifestResourceStream(resource) : null;
    }

    public static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" or ".htm" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        ".woff2" => "font/woff2",
        ".woff" => "font/woff",
        ".ttf" => "font/ttf",
        ".map" => "application/json; charset=utf-8",
        _ => "application/octet-stream",
    };

    // MSBuild builds logical names from %(RecursiveDir), which uses backslashes.
    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');
}
