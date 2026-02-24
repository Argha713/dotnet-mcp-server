// Argha - 2026-02-24 - Phase 5.1: PluginContext gives plugin tools access to configuration
// and logging without depending on the host's internal DI container.
using Microsoft.Extensions.Logging;

namespace McpServer.Plugins;

/// <summary>
/// Provides plugin tools with configuration values and a scoped logger.
/// Plugins that need config or logging should declare a constructor accepting PluginContext:
/// <code>public MyTool(PluginContext context) { ... }</code>
/// Plugins that need neither may use a parameterless constructor instead.
/// </summary>
public sealed class PluginContext
{
    private readonly Func<string, string?> _configLookup;

    /// <summary>
    /// Logger scoped to this plugin. Use it for diagnostic output during tool execution.
    /// Log output is forwarded to the MCP client via notifications/message.
    /// </summary>
    public ILogger Logger { get; }

    // Argha - 2026-02-24 - public so plugin authors can construct a PluginContext in their own tests
    public PluginContext(Func<string, string?> configLookup, ILogger logger)
    {
        _configLookup = configLookup;
        Logger = logger;
    }

    /// <summary>
    /// Reads a value from the <c>Plugins:Config</c> section of appsettings.json.
    /// Returns null when the key is not configured.
    /// </summary>
    /// <example>
    /// In appsettings.json:
    /// <code>{ "Plugins": { "Config": { "MyApiKey": "abc123" } } }</code>
    /// Usage: <c>context.GetConfig("MyApiKey")</c> → <c>"abc123"</c>
    /// </example>
    public string? GetConfig(string key) => _configLookup(key);
}
