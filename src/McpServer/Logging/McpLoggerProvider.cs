using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace McpServer.Logging;

/// <summary>
/// ILoggerProvider that creates McpLogger instances backed by a shared McpLogSink.
/// Registered in DI alongside the console provider so every ILogger in the process
/// also forwards to the MCP client once the sink is initialised.
/// </summary>
[ProviderAlias("Mcp")]
public class McpLoggerProvider : ILoggerProvider
{
    private readonly McpLogSink _sink;
    private readonly ConcurrentDictionary<string, McpLogger> _loggers = new();

    public McpLoggerProvider(McpLogSink sink)
    {
        _sink = sink;
    }

    public ILogger CreateLogger(string categoryName)
        => _loggers.GetOrAdd(categoryName, name => new McpLogger(name, _sink));

    public void Dispose() => _loggers.Clear();
}
