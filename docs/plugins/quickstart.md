# Plugin Quickstart

This guide walks you through creating, building, and installing a custom plugin tool from scratch using the `dotnet new mcp-tool` template.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- dotnet-mcp-server installed as a global tool, or cloned locally

---

## Step 1 — Install the Template

```bash
dotnet new install DotnetMcpServer.Templates
```

Verify it installed:

```bash
dotnet new list mcp-tool
```

You should see `mcp-tool` in the output.

---

## Step 2 — Scaffold Your Plugin

```bash
dotnet new mcp-tool -n WeatherTool
cd WeatherTool
```

This creates:

```
WeatherTool/
├── WeatherTool.csproj    # References McpServer.Plugin.Abstractions NuGet package
├── WeatherTool.cs        # ITool implementation with TODO markers
└── README.md             # Installation and verification instructions
```

---

## Step 3 — Implement Your Tool

Open `WeatherTool.cs`. The scaffolded file includes a complete `ITool` implementation with TODO markers where you add your logic:

```csharp
public class WeatherTool : ITool
{
    public string Name => "weather";
    public string Description => "TODO: describe what your tool does";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new() { Type = "string", Enum = new List<string> { "TODO" } },
            // TODO: add more parameters
        },
        Required = new List<string> { "action" }
    };

    public Task<ToolCallResult> ExecuteAsync(
        Dictionary<string, object>? arguments,
        IProgressReporter? progress = null,
        CancellationToken cancellationToken = default)
    {
        var action = arguments?.TryGetValue("action", out var a) == true
            ? a?.ToString() : null;

        // TODO: implement your actions
        return Task.FromResult(Ok("Hello from WeatherTool!"));
    }

    private static ToolCallResult Ok(string text) => new()
    {
        Content = new List<ContentBlock> { new() { Type = "text", Text = text } }
    };
}
```

Replace the TODO markers with your implementation.

---

## Step 4 — Build

```bash
dotnet build -c Release
```

---

## Step 5 — Install the Plugin

Copy the compiled DLL to the plugins directory:

=== "Windows (Global Tool)"
    ```powershell
    copy bin\Release\net8.0\WeatherTool.dll "$env:APPDATA\dotnet-mcp-server\plugins\"
    ```

=== "Linux / macOS (Global Tool)"
    ```bash
    cp bin/Release/net8.0/WeatherTool.dll ~/.config/dotnet-mcp-server/plugins/
    ```

=== "Clone & Build"
    ```bash
    cp bin/Release/net8.0/WeatherTool.dll /path/to/dotnet-mcp-server/plugins/
    ```

---

## Step 6 — Verify

Restart the server. Your tool will appear in `tools/list`:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"test","version":"1.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
```

Look for `"name": "weather"` in the response.

---

## Adding Plugin Configuration

If your tool needs configuration values (API keys, base URLs, etc.), use the `PluginContext` constructor pattern:

```csharp
public class WeatherTool : ITool
{
    private readonly string _apiKey;

    public WeatherTool(PluginContext context)
    {
        _apiKey = context.GetConfig("weather_api_key")
            ?? throw new InvalidOperationException("weather_api_key is required in Plugins.Config");
        context.Logger.LogInformation("WeatherTool initialised");
    }

    // ... rest of the implementation
}
```

Add the value to `appsettings.json`:

```json
{
  "Plugins": {
    "Config": {
      "weather_api_key": "your-api-key-here"
    }
  }
}
```

!!! tip "Constructor detection"
    The server checks for a `PluginContext` constructor first. If not found, it falls back to a parameterless constructor. Both patterns work fine — use the parameterless pattern for simple tools that need no configuration.

---

## Next Steps

- [API Reference — ITool, PluginContext, and more →](api-reference.md)
- [See an annotated real-world example →](examples.md)
