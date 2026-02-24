using Microsoft.Extensions.Logging;

namespace McpServer.Logging;

/// <summary>
/// ILogger implementation that forwards log entries to McpLogSink,
/// which serialises them as notifications/message and writes them to stdout.
/// </summary>
public class McpLogger : ILogger
{
    private readonly string _categoryName;
    private readonly McpLogSink _sink;

    public McpLogger(string categoryName, McpLogSink sink)
    {
        _categoryName = categoryName;
        _sink = sink;
    }

    public bool IsEnabled(LogLevel logLevel) => _sink.IsEnabled(logLevel);

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);

        // Argha - 2026-02-24 - append exception message when present so the client sees the full picture
        if (exception != null)
            message = $"{message} | Exception: {exception.GetType().Name}: {exception.Message}";

        _sink.WriteLog(logLevel, _categoryName, message);
    }

    // Argha - 2026-02-24 - scopes are not forwarded to MCP; return null scope
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
