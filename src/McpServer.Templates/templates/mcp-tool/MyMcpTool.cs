// Generated from the mcp-tool template — dotnet-mcp-server plugin scaffold.
//
// Build:   dotnet build -c Release
// Install: copy bin/Release/net8.0/MyMcpTool.dll into the plugins/ folder of dotnet-mcp-server,
//          then restart the server — your tool appears in tools/list automatically.
using McpServer.Plugins;
using McpServer.Progress;
using McpServer.Protocol;
using McpServer.Tools;
using Microsoft.Extensions.Logging;

namespace MyMcpTool;

/// <summary>
/// Drop-in plugin tool for dotnet-mcp-server.
///
/// Checklist:
///   1. Rename <see cref="Name"/> to a unique snake_case identifier (e.g. "weather_tool").
///   2. Update <see cref="Description"/> — one sentence shown to the AI in tools/list.
///   3. Define your parameters in <see cref="InputSchema"/>.
///   4. Implement <see cref="ExecuteAsync"/> — return Ok() on success, Error() on failure.
///
/// Two constructor patterns are supported by the server loader:
///   • Parameterless (this file) — use when you need no config or logging.
///   • PluginContext              — use for config values + ILogger (see commented block below).
/// </summary>
public class MyMcpTool : ITool
{
    // TODO: rename to a unique snake_case identifier — the AI uses this name to invoke your tool.
    public string Name => "my_mcp_tool";

    // TODO: one sentence describing what this tool does for the AI assistant.
    public string Description => "TODO: describe what this tool does.";

    // TODO: declare every input parameter your tool accepts.
    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "example_action" }
            }
        },
        Required = new List<string> { "action" }
    };

    public Task<ToolCallResult> ExecuteAsync(
        Dictionary<string, object>? arguments,
        IProgressReporter? progress = null,
        CancellationToken cancellationToken = default)
    {
        var action = arguments?.TryGetValue("action", out var a) == true ? a?.ToString() : null;

        return action switch
        {
            "example_action" => Task.FromResult(Ok($"Hello from MyMcpTool!")),
            _                => Task.FromResult(Error($"Unknown action '{action}'."))
        };
    }

    // -------------------------------------------------------------------------
    // OPTIONAL — PluginContext constructor (config + logging)
    //
    // Replace the parameterless constructor above with this pattern when your tool
    // needs configuration values or wants to log diagnostics.
    //
    // In appsettings.json add:
    //   { "Plugins": { "Config": { "my_api_key": "your-value" } } }
    //
    // private readonly string _apiKey;
    //
    // public MyMcpTool(PluginContext context)
    // {
    //     _apiKey = context.GetConfig("my_api_key")
    //         ?? throw new InvalidOperationException(
    //             "Plugins:Config:my_api_key is not configured in appsettings.json");
    //     context.Logger.LogInformation("MyMcpTool initialised");
    // }
    // -------------------------------------------------------------------------

    private static ToolCallResult Ok(string text) => new()
    {
        Content = new List<ContentBlock> { new() { Type = "text", Text = text } }
    };

    private static ToolCallResult Error(string message) => new()
    {
        Content = new List<ContentBlock> { new() { Type = "text", Text = message } },
        IsError = true
    };
}
