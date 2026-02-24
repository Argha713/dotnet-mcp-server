// Argha - 2026-02-24 - Phase 5.1: example plugin tool demonstrating both constructor patterns.
// This file shows how to build a drop-in plugin for dotnet-mcp-server.
//
// Build and install:
//   dotnet build -o ./output
//   cp ./output/SamplePlugin.dll ~/.config/dotnet-mcp-server/plugins/
//   # Restart dotnet-mcp-server — "greeting" now appears in tools/list
using McpServer.Plugins;
using McpServer.Progress;
using McpServer.Protocol;
using McpServer.Tools;
using Microsoft.Extensions.Logging;

namespace SamplePlugin;

/// <summary>
/// A minimal plugin tool that demonstrates the parameterless constructor pattern.
/// The server will instantiate this automatically when the DLL is in the plugins directory.
/// </summary>
public class GreetingTool : ITool
{
    public string Name => "greeting";

    public string Description =>
        "Returns a personalised greeting. Action: 'greet' (required param: name).";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "greet" }
            },
            ["name"] = new()
            {
                Type = "string",
                Description = "The name to greet"
            }
        },
        Required = new List<string> { "action", "name" }
    };

    public Task<ToolCallResult> ExecuteAsync(
        Dictionary<string, object>? arguments,
        IProgressReporter? progress = null,
        CancellationToken cancellationToken = default)
    {
        var action = arguments?.TryGetValue("action", out var a) == true ? a?.ToString() : null;
        var name   = arguments?.TryGetValue("name",   out var n) == true ? n?.ToString() : null;

        if (action?.ToLower() != "greet")
            return Task.FromResult(Error($"Unknown action '{action}'. Supported: greet"));

        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(Error("'name' parameter is required."));

        return Task.FromResult(Ok($"Hello, {name}! This greeting was delivered by a plugin tool."));
    }

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

/// <summary>
/// A plugin tool that demonstrates the PluginContext constructor pattern for config access.
/// Reads an optional "greeting_prefix" from appsettings.json Plugins:Config section.
/// </summary>
public class ConfigurableGreetingTool : ITool
{
    private readonly string _prefix;

    // Argha - 2026-02-24 - PluginContext is injected by the server when this constructor is found.
    // Add { "Plugins": { "Config": { "greeting_prefix": "Hey" } } } to appsettings.json to customise.
    public ConfigurableGreetingTool(PluginContext context)
    {
        _prefix = context.GetConfig("greeting_prefix") ?? "Hello";
        context.Logger.LogInformation(
            "ConfigurableGreetingTool initialised with prefix '{Prefix}'", _prefix);
    }

    public string Name => "greeting_configurable";

    public string Description =>
        "Returns a configurable greeting. Prefix is read from Plugins:Config:greeting_prefix in appsettings.json.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Enum = new List<string> { "greet" }
            },
            ["name"] = new()
            {
                Type = "string",
                Description = "The name to greet"
            }
        },
        Required = new List<string> { "action", "name" }
    };

    public Task<ToolCallResult> ExecuteAsync(
        Dictionary<string, object>? arguments,
        IProgressReporter? progress = null,
        CancellationToken cancellationToken = default)
    {
        var name = arguments?.TryGetValue("name", out var n) == true ? n?.ToString() : null;

        if (string.IsNullOrWhiteSpace(name))
            return Task.FromResult(new ToolCallResult
            {
                Content = new List<ContentBlock> { new() { Type = "text", Text = "'name' is required." } },
                IsError = true
            });

        return Task.FromResult(new ToolCallResult
        {
            Content = new List<ContentBlock>
            {
                new() { Type = "text", Text = $"{_prefix}, {name}! (configured plugin tool)" }
            }
        });
    }
}
