# 🔌 dotnet-mcp-server

A **Model Context Protocol (MCP)** server built with **.NET 8** that exposes enterprise tools to AI assistants like Claude Desktop.

> MCP is Anthropic's open protocol that lets AI assistants connect to external data sources and tools. This project brings MCP to the .NET ecosystem.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-12-239120?style=flat&logo=csharp)
![MCP](https://img.shields.io/badge/MCP-2024--11--05-blue?style=flat)
![License](https://img.shields.io/badge/License-MIT-green.svg)

---

## What is MCP?

The **Model Context Protocol** allows AI assistants to:
- 🔍 Query your databases
- 📁 Read files from your system
- 🌐 Call external APIs
- ⏰ Get real-time information

Instead of copying data into prompts, the AI can directly access the tools it needs.

---

## Features

This server provides four enterprise-ready tools:

| Tool | Description |
|------|-------------|
| 🕐 **datetime** | Get current time, convert between timezones |
| 📁 **filesystem** | Read files, list directories (within allowed paths) |
| 🗄️ **sql_query** | Execute read-only SQL queries against configured databases |
| 🌐 **http_request** | Make GET/POST requests to allowed APIs |

### Security Features

- ✅ **File access restricted** to configured directories only
- ✅ **SQL queries are read-only** (SELECT only, dangerous keywords blocked)
- ✅ **HTTP requests limited** to allowed hosts
- ✅ **No arbitrary code execution**

---

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Claude Desktop](https://claude.ai/download) (for testing)

### 1. Clone and Build

```bash
git clone https://github.com/Argha713/dotnet-mcp-server.git
cd dotnet-mcp-server
dotnet build
```

### 2. Configure

Edit `src/McpServer/appsettings.json`:

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

### 3. Connect to Claude Desktop

Add to your Claude Desktop config (`%APPDATA%\Claude\claude_desktop_config.json` on Windows):

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\path\\to\\dotnet-mcp-server\\src\\McpServer"]
    }
  }
}
```

Or use the published executable:

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "C:\\path\\to\\dotnet-mcp-server.exe"
    }
  }
}
```

### 4. Restart Claude Desktop

Close and reopen Claude Desktop. You should see the tools available!

---

## Tool Usage Examples

### DateTime Tool

Ask Claude:
- *"What time is it in Tokyo?"*
- *"Convert 3pm EST to IST"*
- *"What's the current UTC time?"*

### File System Tool

Ask Claude:
- *"List files in my Documents folder"*
- *"Read the contents of README.md"*
- *"Search for all .cs files in my projects"*

### SQL Query Tool

Ask Claude:
- *"Show me the tables in MyDB database"*
- *"Query the top 10 customers by revenue"*
- *"Describe the structure of the Orders table"*

### HTTP Tool

Ask Claude:
- *"Get my GitHub profile info"*
- *"Fetch the latest posts from JSONPlaceholder API"*
- *"What APIs can you access?"*

---

## Configuration Reference

### File System Settings

```json
{
  "FileSystem": {
    "AllowedPaths": [
      "/home/user/documents",
      "/projects"
    ]
  }
}
```

### SQL Settings

```json
{
  "Sql": {
    "Connections": {
      "Production": {
        "ConnectionString": "Server=...;Database=...;",
        "Description": "Production database (read-only)"
      },
      "Analytics": {
        "ConnectionString": "Server=...;Database=...;",
        "Description": "Analytics warehouse"
      }
    }
  }
}
```

### HTTP Settings

```json
{
  "Http": {
    "AllowedHosts": [
      "api.github.com",
      "api.stripe.com",
      "your-internal-api.com"
    ],
    "TimeoutSeconds": 30
  }
}
```

---

## Architecture

```
┌─────────────────────┐
│   Claude Desktop    │
│   (MCP Client)      │
└──────────┬──────────┘
           │ JSON-RPC over stdio
           ▼
┌─────────────────────┐
│  dotnet-mcp-server  │
│  (MCP Server)       │
├─────────────────────┤
│  ┌───────────────┐  │
│  │ DateTime Tool │  │
│  ├───────────────┤  │
│  │ FileSystem    │  │
│  │ Tool          │  │
│  ├───────────────┤  │
│  │ SQL Query     │  │
│  │ Tool          │  │
│  ├───────────────┤  │
│  │ HTTP Tool     │  │
│  └───────────────┘  │
└─────────────────────┘
           │
     ┌─────┴─────┐
     ▼     ▼     ▼
   Files  SQL  APIs
```

---

## Project Structure

```
dotnet-mcp-server/
├── src/
│   └── McpServer/
│       ├── Protocol/           # MCP/JSON-RPC types
│       ├── Tools/              # Tool implementations
│       ├── Configuration/      # Settings classes
│       ├── McpServerHandler.cs # Main server logic
│       └── Program.cs          # Entry point
├── tests/
│   └── McpServer.Tests/        # Unit tests
└── README.md
```

---

## Adding Custom Tools

Create a new class implementing `ITool`:

```csharp
public class MyCustomTool : ITool
{
    public string Name => "my_tool";
    public string Description => "Does something useful";
    
    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["input"] = new() { Type = "string", Description = "Input value" }
        },
        Required = new List<string> { "input" }
    };

    public async Task<ToolCallResult> ExecuteAsync(
        Dictionary<string, object>? arguments, 
        CancellationToken cancellationToken)
    {
        var input = arguments?["input"]?.ToString();
        // Do something...
        return new ToolCallResult
        {
            Content = new List<ContentBlock>
            {
                new() { Type = "text", Text = $"Result: {input}" }
            }
        };
    }
}
```

Register in `Program.cs`:
```csharp
services.AddSingleton<ITool, MyCustomTool>();
```

---

## Troubleshooting

| Problem | Solution |
|---------|----------|
| Claude doesn't see the tools | Check `claude_desktop_config.json` path is correct. Restart Claude Desktop. |
| "Access denied" errors | Add the path to `AllowedPaths` in config |
| SQL connection fails | Verify connection string. Ensure SQL Server is running. |
| HTTP requests blocked | Add the host to `AllowedHosts` in config |

### View Logs

Logs are written to stderr. To see them:
```bash
dotnet run 2> log.txt
```

---

## Roadmap

### Phase 1 — Security & Stability
- [ ] Fix SQL injection via subqueries (block `;` and compound statements)
- [ ] Add initialization gate (reject tool calls before handshake)
- [ ] Config validation on startup (warn about missing paths, bad connection strings)
- [ ] Expand test coverage (FileSystemTool, HttpTool, SqlQueryTool, McpServerHandler)

### Phase 2 — New Tools
- [ ] **Git Tool** — `status`, `log`, `diff`, `branch_list`, `blame` (read-only)
- [ ] **System Info Tool** — `processes`, `system_info` (CPU/RAM/disk), `network`
- [ ] **Data Transform Tool** — `json_query`, `csv_to_json`, `json_to_csv`, `xml_to_json`, `base64_encode/decode`, `hash`
- [ ] **Text Tool** — `regex_match`, `regex_replace`, `word_count`, `diff_text`, `format_json/xml`
- [ ] **Environment Tool** — `get`, `list`, `has` (with blocklist for sensitive vars)

### Phase 3 — Production Readiness
- [ ] Dockerfile + docker-compose (one-command setup)
- [ ] GitHub Actions CI/CD (build, test, release binaries)
- [ ] Self-contained single-file executables (win-x64, linux-x64, osx-arm64)
- [ ] `dotnet tool install -g dotnet-mcp-server` distribution
- [ ] `--init` config wizard for first-run setup
- [ ] `--validate` health check for all configured connections

### Phase 4 — MCP Protocol Completeness
- [ ] Resources support (`resources/list`, `resources/read`)
- [ ] Prompts support (`prompts/list`, `prompts/get`) with built-in templates
- [ ] Logging protocol (`logging/setLevel`, `notifications/message`)
- [ ] Progress notifications for long-running operations

### Phase 5 — Developer Experience
- [ ] Plugin architecture (drop-in tool DLLs from `/plugins` folder)
- [ ] `dotnet new mcp-tool` project template for custom tools
- [ ] Documentation site (Getting Started, Tool Reference, Custom Tools guide)
- [ ] Example configurations (`developer.json`, `data-analyst.json`, `api-integrator.json`)
- [ ] `CONTRIBUTING.md` + issue templates

### Phase 6 — Advanced Features
- [ ] Multi-database support (PostgreSQL, MySQL, SQLite)
- [ ] Response caching with configurable TTL
- [ ] Audit logging (every tool call logged to file)
- [ ] Rate limiting per tool
- [ ] Tool-level authentication & permissions

---

## Related Projects

- [dotnet-rag-api](https://github.com/Argha713/dotnet-rag-api) — RAG system in .NET 8

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

---

## Author

**Argha Sarkar**

- LinkedIn: [argha-sarkar](https://www.linkedin.com/in/argha-sarkar-12538a21a)
- GitHub: [@Argha713](https://github.com/Argha713)

---

⭐ If you found this project helpful, please give it a star!
