namespace FlowScreen.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public interface IAppLogger
{
    void Log(LogLevel level, string message, Exception? exception = null);
}

public static class AppLoggerExtensions
{
    public static void Debug(this IAppLogger logger, string message) => logger.Log(LogLevel.Debug, message);

    public static void Info(this IAppLogger logger, string message) => logger.Log(LogLevel.Info, message);

    public static void Warn(this IAppLogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Warning, message, exception);

    public static void Error(this IAppLogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Error, message, exception);
}

/// <summary>Discards everything. Used when the log directory cannot be created.</summary>
public sealed class NullLogger : IAppLogger
{
    public static NullLogger Instance { get; } = new();

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
        // Intentionally empty.
    }
}
