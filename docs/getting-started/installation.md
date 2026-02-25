# Installation

There are three ways to install dotnet-mcp-server. Choose the one that fits your setup.

---

## Option A — Global .NET Tool (Recommended)

The simplest path. Installs the server as a global CLI command.

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
dotnet tool install -g DotnetMcpServer
```

Run the interactive setup wizard to create your config file:

```bash
dotnet-mcp-server --init
```

The wizard writes `appsettings.json` to your user config directory:

| OS | Config location |
|----|----------------|
| Windows | `%APPDATA%\dotnet-mcp-server\appsettings.json` |
| Linux / macOS | `~/.config/dotnet-mcp-server/appsettings.json` |

Verify everything is wired up correctly:

```bash
dotnet-mcp-server --validate
```

To update later:

```bash
dotnet tool update -g DotnetMcpServer
```

---

## Option B — Clone and Build

For contributors or anyone who wants to run from source.

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), Git

```bash
git clone https://github.com/Argha713/dotnet-mcp-server.git
cd dotnet-mcp-server
dotnet build
```

Edit the local config file at `src/McpServer/appsettings.json`:

```json
{
  "FileSystem": {
    "AllowedPaths": [
      "C:\\Users\\YourName\\Documents",
      "C:\\Projects"
    ]
  },
  "Sql": {
    "Connections": {
      "MyDB": {
        "ConnectionString": "Server=localhost;Database=MyDB;Trusted_Connection=True;",
        "Description": "My local database"
      }
    }
  },
  "Http": {
    "AllowedHosts": [
      "api.github.com",
      "jsonplaceholder.typicode.com"
    ]
  }
}
```

Run the server:

```bash
dotnet run --project src/McpServer
```

When connecting a client in Option B mode, replace `"command": "dotnet-mcp-server"` with:

```json
"command": "dotnet",
"args": ["run", "--project", "C:\\path\\to\\dotnet-mcp-server\\src\\McpServer"]
```

---

## Option C — Docker (No .NET Required)

Run the server in a container alongside a demo SQL Server. No .NET SDK needed on your machine.

**Prerequisites:** Docker Desktop

**Step 1:** Copy the example config and env files:

```bash
cp docker/appsettings.example.json docker/appsettings.json
cp .env.example .env
```

**Step 2:** Edit `docker/appsettings.json` to set your allowed paths, SQL connections, and HTTP hosts. Edit `.env` to set a strong `SQL_SA_PASSWORD`.

**Step 3:** Build and start both services:

```bash
docker-compose up --build
```

!!! note
    The Docker build runs the full test suite. If any test fails, the build is aborted.

For Docker client config, see the [Connecting Clients → Claude Desktop](clients.md#claude-desktop) section and use the Docker run pattern shown there.

---

## Manual Testing (No Client Needed)

You can test the server from any terminal by sending raw JSON-RPC:

```bash
dotnet run --project src/McpServer
```

Paste these lines one at a time:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"manual-test","version":"1.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"datetime","arguments":{"action":"now"}}}
```

---

## Next Steps

- [Connect your MCP client →](clients.md)
- [Configure allowed paths, databases, and hosts →](configuration.md)
