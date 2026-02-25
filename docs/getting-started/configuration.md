# Configuration

The server is configured via `appsettings.json`. The location depends on how you installed it.

| Install method | Config location |
|---------------|----------------|
| Global tool (Windows) | `%APPDATA%\dotnet-mcp-server\appsettings.json` |
| Global tool (Linux/macOS) | `~/.config/dotnet-mcp-server/appsettings.json` |
| Clone & build | `src/McpServer/appsettings.json` |
| Docker | `docker/appsettings.json` (mounted into the container) |

---

## Create Your Config

Run the interactive wizard — it asks you questions and writes the file:

```bash
dotnet-mcp-server --init
```

Or start from one of the [ready-to-use profiles](../configuration/profiles.md) in `examples/configs/`.

---

## Validate Your Config

After editing, check that all connections and paths are reachable:

```bash
dotnet-mcp-server --validate
```

This tests every SQL connection, checks that allowed paths exist, and confirms HTTP hosts are valid.

---

## Full Config Reference

### FileSystem

```json
{
  "FileSystem": {
    "AllowedPaths": [
      "C:\\Users\\YourName\\Documents",
      "C:\\Projects",
      "/home/user/projects"
    ]
  }
}
```

| Key | Type | Description |
|-----|------|-------------|
| `AllowedPaths` | `string[]` | Directories the server is allowed to read from. Subdirectories are included. |

!!! warning "Security constraint"
    Path traversal attacks are blocked. A path like `C:\AllowedPathEvil` will never match `C:\AllowedPath`. Only exact prefix matches (with separator check) are accepted.

---

### SQL

```json
{
  "Sql": {
    "Connections": {
      "Production": {
        "ConnectionString": "Server=prod-server;Database=MyApp;Trusted_Connection=True;",
        "Description": "Production database (read-only)"
      },
      "Analytics": {
        "ConnectionString": "Server=...;Database=Analytics;User Id=reader;Password=...;",
        "Description": "Analytics warehouse"
      }
    }
  }
}
```

| Key | Type | Description |
|-----|------|-------------|
| `Connections` | `object` | Map of named connections. The key (e.g. `"Production"`) is what you refer to in queries. |
| `ConnectionString` | `string` | Standard ADO.NET connection string. |
| `Description` | `string` | Human-readable label shown to the AI. |

!!! warning "Security constraint"
    All SQL queries are SELECT-only. INSERT, UPDATE, DELETE, DROP, ALTER, CREATE, TRUNCATE, EXEC and others are blocked. Queries time out after 30 seconds and return at most 1,000 rows.

---

### HTTP

```json
{
  "Http": {
    "AllowedHosts": [
      "api.github.com",
      "your-internal-api.company.com"
    ],
    "TimeoutSeconds": 30
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `AllowedHosts` | `string[]` | `[]` | Hostnames the server may call. Subdomains are automatically included (e.g. `github.com` also allows `api.github.com`). |
| `TimeoutSeconds` | `int` | `30` | HTTP request timeout in seconds. |

!!! warning "Security constraint"
    Only HTTP and HTTPS schemes are allowed. Responses are truncated at 10,000 characters.

---

### Server

```json
{
  "Server": {
    "Name": "dotnet-mcp-server",
    "Version": "1.0.0"
  }
}
```

---

### Logging

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "McpServer": "Information"
    }
  }
}
```

Logs are written to **stderr** so they never interfere with the JSON-RPC protocol on stdout. Capture them with:

```bash
dotnet-mcp-server 2> server.log
```

---

### Plugins

```json
{
  "Plugins": {
    "Enabled": true,
    "Directory": "plugins",
    "Config": {
      "my_api_key": "your-value-here",
      "greeting_prefix": "Hey"
    }
  }
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | `bool` | `true` | Whether to load plugin DLLs from the plugins directory. |
| `Directory` | `string` | `"plugins"` | Path to the plugins directory (relative to config dir, or absolute). |
| `Config` | `object` | `{}` | Flat key-value map passed to plugins via `PluginContext.GetConfig()`. |

---

## Environment Variable Overrides

Any config value can be overridden via environment variables using the standard .NET naming convention: replace `:` with `__` (double underscore).

```bash
# Override the SQL connection string for "Production"
export Sql__Connections__Production__ConnectionString="Server=...;..."

# Change the log level
export Logging__LogLevel__Default=Debug
```

---

## Next Steps

- [Connect your MCP client →](clients.md)
- [Explore ready-to-use profiles →](../configuration/profiles.md)
- [Understand security constraints →](../configuration/security.md)
