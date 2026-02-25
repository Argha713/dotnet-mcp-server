# Plugin Examples

The `examples/SamplePlugin/` directory in the repository contains a reference implementation showing both constructor patterns side by side.

---

## Pattern 1 — Parameterless Constructor

Use this when your tool needs no external configuration.

```csharp
// examples/SamplePlugin/GreetingTool.cs

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
```

**Key points:**

- No constructor needed — the server calls `new GreetingTool()` automatically
- Arguments are extracted with the `TryGetValue` pattern
- Both a success path (`Ok`) and an error path (`Error`) are implemented as private helpers
- `IsError = true` signals to the AI that the call failed

---

## Pattern 2 — PluginContext Constructor

Use this when your tool needs configuration values from `appsettings.json`.

```csharp
public class ConfigurableGreetingTool : ITool
{
    private readonly string _prefix;

    // The server detects this constructor and injects PluginContext automatically.
    public ConfigurableGreetingTool(PluginContext context)
    {
        // Read from: { "Plugins": { "Config": { "greeting_prefix": "Hey" } } }
        _prefix = context.GetConfig("greeting_prefix") ?? "Hello";
        context.Logger.LogInformation(
            "ConfigurableGreetingTool initialised with prefix '{Prefix}'", _prefix);
    }

    public string Name => "greeting_configurable";
    public string Description =>
        "Returns a configurable greeting. Prefix is read from Plugins:Config:greeting_prefix.";

    // ... InputSchema and ExecuteAsync follow the same pattern
}
```

**Key points:**

- The `PluginContext` constructor takes priority over a parameterless constructor
- `context.GetConfig("key")` returns `null` if the key is not in `appsettings.json` — use `??` to provide a default
- `context.Logger` is pre-scoped to your class name — logs go to the same stderr as the server

---

## Both Patterns in One DLL

You can put multiple tool classes in a single DLL. The server will discover and register all of them:

```
SamplePlugin.dll
├── GreetingTool          → registered as "greeting"
└── ConfigurableGreetingTool → registered as "greeting_configurable"
```

The DLL name does not matter — tool names come from each class's `Name` property.

---

## Configuring the Sample Plugin

To use the configurable greeting prefix, add to `appsettings.json`:

```json
{
  "Plugins": {
    "Config": {
      "greeting_prefix": "Hey"
    }
  }
}
```

Without this, the default prefix `"Hello"` is used.

---

## Testing Your Plugin

Write unit tests directly against your `ITool` implementation — no server process needed:

```csharp
[Fact]
public async Task Greet_WithValidName_ReturnsGreeting()
{
    var tool = new GreetingTool();
    var args = new Dictionary<string, object>
    {
        ["action"] = "greet",
        ["name"] = "Alice"
    };

    var result = await tool.ExecuteAsync(args);

    result.IsError.Should().BeFalse();
    result.Content[0].Text.Should().Contain("Alice");
}
```

- Reference `McpServer.Plugin.Abstractions` in your test project
- Use `NullProgressReporter` if you need to pass a progress reporter
- No mocking or server setup required
