using McpServer.Protocol;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace McpServer.Logging;

/// <summary>
/// Central sink that forwards .NET ILogger calls to the MCP client as notifications/message.
/// The StreamWriter is wired in at runtime by McpServerHandler.RunAsync() so that DI can
/// construct the sink before the stdio streams are open.
/// </summary>
public class McpLogSink
{
    // Argha - 2026-02-24 - writer is null until RunAsync calls Initialize; logs before that are dropped silently
    private StreamWriter? _writer;

    // Argha - 2026-02-24 - default to Warning so clients aren't flooded with Info/Debug noise on first connect
    private McpLogLevel _minimumLevel = McpLogLevel.Warning;

    private readonly object _lock = new();

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    /// <summary>
    /// Called by McpServerHandler.RunAsync once the stdout StreamWriter is ready.
    /// </summary>
    public void Initialize(StreamWriter writer)
    {
        lock (_lock)
        {
            _writer = writer;
        }
    }

    /// <summary>
    /// Called by the logging/setLevel handler to change the forwarding threshold.
    /// </summary>
    public void SetLevel(McpLogLevel level)
    {
        lock (_lock)
        {
            _minimumLevel = level;
        }
    }

    /// <summary>
    /// Returns whether a given .NET log level meets the current MCP threshold.
    /// </summary>
    public bool IsEnabled(LogLevel logLevel)
    {
        var mcpLevel = MapToMcpLevel(logLevel);
        lock (_lock)
        {
            return mcpLevel >= _minimumLevel;
        }
    }

    /// <summary>
    /// Formats and writes a notifications/message JSON line to stdout.
    /// Silently drops the message if the writer is not yet initialized or the level is below threshold.
    /// </summary>
    public void WriteLog(LogLevel logLevel, string categoryName, string message)
    {
        var mcpLevel = MapToMcpLevel(logLevel);

        lock (_lock)
        {
            if (_writer == null || mcpLevel < _minimumLevel)
                return;

            var notification = new LogMessageNotification
            {
                Params = new LogMessageParams
                {
                    Level = mcpLevel.ToString().ToLowerInvariant(),
                    Logger = categoryName,
                    Data = message
                }
            };

            // Argha - 2026-02-24 - write inside the lock so notification lines are never interleaved
            // with JSON-RPC response lines written by the handler on the same StreamWriter
            var json = JsonSerializer.Serialize(notification, _jsonOptions);
            _writer.WriteLine(json);
        }
    }

    // Argha - 2026-02-24 - maps .NET LogLevel to MCP syslog-style level
    // Trace and Debug both map to MCP "debug" (no finer grain in MCP spec)
    private static McpLogLevel MapToMcpLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace       => McpLogLevel.Debug,
        LogLevel.Debug       => McpLogLevel.Debug,
        LogLevel.Information => McpLogLevel.Info,
        LogLevel.Warning     => McpLogLevel.Warning,
        LogLevel.Error       => McpLogLevel.Error,
        LogLevel.Critical    => McpLogLevel.Critical,
        _                    => McpLogLevel.Emergency
    };
}
