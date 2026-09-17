using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace FlowScreen.Logging;

/// <summary>
/// Appends to <c>%LOCALAPPDATA%\FlowScreen\logs\flowscreen-yyyyMMdd.log</c>.
///
/// A screensaver has no console and no user watching, so logging exists purely
/// so that a bug report can be diagnosed afterwards. Every operation is
/// best-effort: a logger that throws while the saver is starting would be worse
/// than no logging at all.
/// </summary>
public sealed class FileLogger : IAppLogger, IDisposable
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int RetentionDays = 7;

    private readonly object gate = new();
    private readonly string filePath;
    private readonly string tag;
    private readonly LogLevel minimum;
    private bool disposed;

    private FileLogger(string filePath, string tag, LogLevel minimum)
    {
        this.filePath = filePath;
        this.tag = tag;
        this.minimum = minimum;
    }

    public static string LogDirectory =>
        Path.Combine(AppPaths.Root, "logs");

    /// <summary>Never throws. Returns a <see cref="NullLogger"/> if the log file cannot be opened.</summary>
    public static IAppLogger Create(string tag, LogLevel minimum = LogLevel.Info)
    {
        try
        {
            var directory = LogDirectory;
            Directory.CreateDirectory(directory);

            var path = Path.Combine(
                directory,
                $"flowscreen-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

            var logger = new FileLogger(path, tag, minimum);
            logger.Prune(directory);
            logger.RollIfTooLarge();
            return logger;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FlowScreen: file logging unavailable: {ex.Message}");
            return NullLogger.Instance;
        }
    }

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (level < minimum || disposed) return;

        var builder = new StringBuilder(160);
        builder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ")
            .Append('[').Append(tag).Append("] ")
            .Append(message);

        if (exception is not null)
        {
            builder.AppendLine().Append("    ").Append(exception.GetType().FullName)
                .Append(": ").Append(exception.Message);
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                builder.AppendLine().Append(exception.StackTrace);
            }
        }

        var line = builder.ToString();
        Debug.WriteLine(line);

        try
        {
            lock (gate)
            {
                File.AppendAllText(filePath, line + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FlowScreen: failed to write log: {ex.Message}");
        }
    }

    private void RollIfTooLarge()
    {
        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists || info.Length < MaxFileBytes) return;

            var rolled = Path.ChangeExtension(filePath, $".{DateTime.Now:HHmmss}.log");
            File.Move(filePath, rolled, overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FlowScreen: failed to roll log: {ex.Message}");
        }
    }

    private void Prune(string directory)
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(directory, "flowscreen-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FlowScreen: failed to prune logs: {ex.Message}");
        }
    }

    public void Dispose() => disposed = true;
}

/// <summary>Single source of truth for where FlowScreen keeps per-user state.</summary>
public static class AppPaths
{
    public static string Root =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FlowScreen");

    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>
    /// WebView2 insists on a writable user-data folder. It must not default to
    /// the folder next to the executable, because an installed screensaver can
    /// live in System32 where the user has no write access.
    /// </summary>
    public static string WebViewUserData => Path.Combine(Root, "WebView2");
}
