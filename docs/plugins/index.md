# Plugin System

dotnet-mcp-server has a drop-in plugin system. You can add your own tools to the server without forking or modifying the core project.

---

## How It Works

1. You write a .NET class library that implements the `ITool` interface
2. You build it to a DLL
3. You copy the DLL into the `plugins/` directory
4. The server discovers and loads it automatically on the next start

The server scans for DLLs in the `plugins/` folder at startup, instantiates any class implementing `ITool`, and registers them alongside the built-in tools. Your plugin tool appears in `tools/list` exactly like a built-in.

---

## Plugin vs. Built-in Tool

| | Built-in tool | Plugin tool |
|--|--------------|-------------|
| Where code lives | `src/McpServer/Tools/` | Your own repository |
| How to install | Always included | Copy DLL to `plugins/` |
| Requires server fork | No (it's in the server) | No |
| Requires server restart | N/A | Yes, once after installing |
| Config access | Via `IOptions<T>` | Via `PluginContext.GetConfig()` |

---

## Plugin Directory Location

| Install method | Plugins directory |
|---------------|------------------|
| Global tool (Windows) | `%APPDATA%\dotnet-mcp-server\plugins\` |
| Global tool (Linux/macOS) | `~/.config/dotnet-mcp-server/plugins\` |
| Clone & build | `plugins\` next to `appsettings.json` |

You can override the directory via `appsettings.json`:

```json
{
  "Plugins": {
    "Directory": "C:\\MyCustomPlugins"
  }
}
```

---

## Getting Started

The fastest way to write a plugin is with the project template:

```bash
dotnet new install DotnetMcpServer.Templates
dotnet new mcp-tool -n MyTool
```

[Full Plugin Quickstart →](quickstart.md){ .md-button .md-button--primary }

---

## Plugin Configuration

Pass values to your plugin via the `Plugins.Config` section in `appsettings.json`:

```json
{
  "Plugins": {
    "Config": {
      "api_base_url": "https://api.example.com",
      "api_key": "your-key-here"
    }
  }
}
```

Read them in your plugin using the `PluginContext` constructor:

```csharp
public class MyTool : ITool
{
    private readonly string _apiBaseUrl;

    public MyTool(PluginContext context)
    {
        _apiBaseUrl = context.GetConfig("api_base_url") ?? "https://default.example.com";
    }
}
```

[API Reference →](api-reference.md)
