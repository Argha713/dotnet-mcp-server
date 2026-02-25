# Plugin API Reference

All plugin contracts are in the `McpServer.Plugin.Abstractions` NuGet package.

```bash
dotnet add package DotnetMcpServer.Abstractions
```

The scaffolded template adds this reference automatically.

---

## ITool

The core interface every plugin must implement.

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    JsonSchema InputSchema { get; }

    Task<ToolCallResult> ExecuteAsync(
        Dictionary<string, object>? arguments,
        IProgressReporter? progress = null,
        CancellationToken cancellationToken = default);
}
```

| Member | Description |
|--------|-------------|
| `Name` | Unique tool name used in `tools/call` requests (e.g. `"weather"`) |
| `Description` | Human-readable description shown to the AI in `tools/list` |
| `InputSchema` | JSON Schema describing accepted parameters |
| `ExecuteAsync` | Called when the AI invokes this tool |

**Naming convention:** Use `snake_case` for tool names (e.g. `weather_tool`, `my_api`).

---

## JsonSchema

Describes the parameters your tool accepts.

```csharp
public class JsonSchema
{
    public string Type { get; set; }                                      // "object"
    public Dictionary<string, JsonSchemaProperty> Properties { get; set; }
    public List<string> Required { get; set; }
}

public class JsonSchemaProperty
{
    public string Type { get; set; }          // "string", "integer", "boolean", "number"
    public string Description { get; set; }
    public List<string>? Enum { get; set; }   // Allowed values (optional)
}
```

**Example schema — tool with an `action` enum and a string `city` parameter:**

```csharp
public JsonSchema InputSchema => new()
{
    Type = "object",
    Properties = new Dictionary<string, JsonSchemaProperty>
    {
        ["action"] = new()
        {
            Type = "string",
            Description = "The operation to perform",
            Enum = new List<string> { "current", "forecast" }
        },
        ["city"] = new()
        {
            Type = "string",
            Description = "City name (e.g. London, Tokyo)"
        }
    },
    Required = new List<string> { "action", "city" }
};
```

---

## ToolCallResult

The return type of `ExecuteAsync`. Contains a list of content blocks.

```csharp
public class ToolCallResult
{
    public List<ContentBlock> Content { get; set; }
    public bool IsError { get; set; }
}
```

| Property | Description |
|----------|-------------|
| `Content` | One or more content blocks to return to the AI |
| `IsError` | Set to `true` to signal a tool error (the AI will see this as a failure) |

---

## ContentBlock

A single piece of content in a tool result.

```csharp
public class ContentBlock
{
    public string Type { get; set; }    // "text" or "image"
    public string? Text { get; set; }   // For Type = "text"
}
```

For most plugins, you will only use `Type = "text"`.

**Helper pattern (recommended):**

```csharp
private static ToolCallResult Ok(string text) => new()
{
    Content = new List<ContentBlock> { new() { Type = "text", Text = text } }
};

private static ToolCallResult Error(string message) => new()
{
    Content = new List<ContentBlock> { new() { Type = "text", Text = message } },
    IsError = true
};
```

---

## IProgressReporter

Reports progress for long-running operations. Passed as a parameter to `ExecuteAsync`.

```csharp
public interface IProgressReporter
{
    void Report(int percent, string? message = null);
}
```

!!! tip "Always check for null"
    The `progress` parameter may be `null` if the client did not provide a `progressToken`. Always null-check before calling `Report`.

```csharp
public async Task<ToolCallResult> ExecuteAsync(
    Dictionary<string, object>? arguments,
    IProgressReporter? progress = null,
    CancellationToken cancellationToken = default)
{
    progress?.Report(0, "Starting...");
    // ... do work ...
    progress?.Report(50, "Halfway done...");
    // ... more work ...
    progress?.Report(100, "Complete");
    return Ok("Done");
}
```

---

## PluginContext

Passed to your tool's constructor if it accepts a `PluginContext` parameter. Provides config access and a logger.

```csharp
public class PluginContext
{
    public ILogger Logger { get; }
    public string? GetConfig(string key);
}
```

| Member | Description |
|--------|-------------|
| `Logger` | `Microsoft.Extensions.Logging.ILogger` scoped to your tool class |
| `GetConfig(key)` | Returns the value at `Plugins.Config.<key>` in `appsettings.json`, or `null` if not set |

**Example:**

```csharp
public MyTool(PluginContext context)
{
    var apiKey = context.GetConfig("my_api_key");
    if (string.IsNullOrEmpty(apiKey))
        throw new InvalidOperationException("my_api_key must be set in Plugins.Config");

    context.Logger.LogInformation("MyTool loaded, api_key length={Length}", apiKey.Length);
}
```

---

## NullProgressReporter

A no-op implementation of `IProgressReporter` for use in tests.

```csharp
var reporter = new NullProgressReporter();
var result = await myTool.ExecuteAsync(args, reporter, CancellationToken.None);
```

---

## Constructor Resolution Order

When loading a plugin DLL, the server checks:

1. Is there a public constructor taking `(PluginContext ctx)`? → Use it, inject `PluginContext`
2. Is there a public parameterless constructor? → Use it
3. Neither found → Log a warning and skip the type

You can have both in a single file (e.g. the `SamplePlugin`) and the server will use the right one for each class.
