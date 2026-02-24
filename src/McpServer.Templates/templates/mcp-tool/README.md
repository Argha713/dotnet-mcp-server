# MyMcpTool

A custom tool plugin for [dotnet-mcp-server](https://github.com/Argha713/dotnet-mcp-server).

## Building

```bash
dotnet build -c Release
```

## Installing

Copy the compiled DLL into the `plugins/` folder of your dotnet-mcp-server config directory,
then restart the server. Your tool will appear in `tools/list` automatically.

**Windows:**
```powershell
copy bin\Release\net8.0\MyMcpTool.dll "$env:APPDATA\dotnet-mcp-server\plugins\"
```

**Linux / macOS:**
```bash
cp bin/Release/net8.0/MyMcpTool.dll ~/.config/dotnet-mcp-server/plugins/
```

> The plugins directory is created automatically on first run. If it does not exist yet,
> create it manually before copying.

## Verifying

Start the server and send a `tools/list` request to confirm your tool appears:

```bash
dotnet-mcp-server
```

Paste these JSON-RPC messages (one per line):

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"test","version":"1.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
```

## Configuring (optional)

To pass configuration values to your plugin, add a `Plugins.Config` section in the server's
`appsettings.json`:

```json
{
  "Plugins": {
    "Config": {
      "my_api_key": "your-value-here"
    }
  }
}
```

Then use the `PluginContext` constructor pattern (see the commented block in `MyMcpTool.cs`).

## Reference

- [dotnet-mcp-server — Plugin Architecture](https://github.com/Argha713/dotnet-mcp-server)
- `McpServer.Plugin.Abstractions` — NuGet package providing the `ITool` contract
