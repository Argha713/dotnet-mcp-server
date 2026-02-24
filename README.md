# 🔌 dotnet-mcp-server

A **Model Context Protocol (MCP)** server built with **.NET 8** that exposes enterprise tools to AI assistants like Claude Desktop, VS Code (Copilot/Continue/Cline), Cursor, Windsurf, and more.

> MCP is Anthropic's open protocol that lets AI assistants connect to external data sources and tools. This project brings MCP to the .NET ecosystem. It works with **any MCP-compatible client** — not just Claude Desktop.

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet)
![C#](https://img.shields.io/badge/C%23-12-239120?style=flat&logo=csharp)
![MCP](https://img.shields.io/badge/MCP-2024--11--05-blue?style=flat)
![License](https://img.shields.io/badge/License-MIT-green.svg)
![CI](https://github.com/Argha713/dotnet-mcp-server/actions/workflows/ci.yml/badge.svg)

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

This server provides nine enterprise-ready tools:

| Tool | Description |
|------|-------------|
| 🕐 **datetime** | Get current time, convert between timezones |
| 📁 **filesystem** | Read files, list directories (within allowed paths) |
| 🗄️ **sql_query** | Execute read-only SQL queries against configured databases |
| 🌐 **http_request** | Make GET/POST requests to allowed APIs |
| 📝 **text** | Regex match/replace, word count, text diff, format JSON/XML |
| 🔄 **data_transform** | JSON query, CSV/JSON/XML conversion, base64, hashing |
| 🌍 **environment** | Get/list/check environment variables (sensitive values masked) |
| 💻 **system_info** | OS details, running processes, network interfaces |
| 🔀 **git** | Read-only Git: status, log, diff, branches, blame |

### Security Features

- ✅ **File access restricted** to configured directories only
- ✅ **SQL queries are read-only** (SELECT only, dangerous keywords blocked)
- ✅ **HTTP requests limited** to allowed hosts
- ✅ **Environment variables** with sensitive value masking (passwords, tokens, keys)
- ✅ **Git operations are read-only** with path validation and argument sanitization
- ✅ **Regex timeout protection** against ReDoS attacks
- ✅ **XXE prevention** in XML parsing
- ✅ **No arbitrary code execution**

---

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Any MCP client (see [Supported Clients](#supported-clients) below)

### Option A — Install as a Global Tool (Recommended)

```bash
dotnet tool install -g Argha.dev.McpServer
```

Then run the interactive setup wizard to create your config:

```bash
dotnet-mcp-server --init
```

The wizard writes `appsettings.json` to your user config directory:
- **Windows:** `%APPDATA%\dotnet-mcp-server\appsettings.json`
- **Linux/macOS:** `~/.config/dotnet-mcp-server/appsettings.json`

After configuring, you can verify everything is working:

```bash
dotnet-mcp-server --validate
```

To update to the latest version later:

```bash
dotnet tool update -g Argha.dev.McpServer
```

---

### Option B — Clone and Build

```bash
git clone https://github.com/Argha713/dotnet-mcp-server.git
cd dotnet-mcp-server
dotnet build
```

### Configure (Option B only)

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

---

### Option C — Docker (No .NET Required)

Run the server in a container alongside a demo SQL Server — no .NET SDK needed on your machine.

**Step 1:** Copy the example config and env files:

```bash
cp docker/appsettings.example.json docker/appsettings.json
cp .env.example .env
```

**Step 2:** Edit `docker/appsettings.json` to set your allowed paths, SQL connections, and HTTP hosts. Edit `.env` to set a strong `SQL_SA_PASSWORD`.

**Step 3:** Build the image and start both services:

```bash
docker-compose up --build
```

The `docker build` step runs all tests — if any test fail, the build is aborted.

**Claude Desktop integration** (docker run pattern):

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-v", "/path/to/docker/appsettings.json:/home/app/.config/dotnet-mcp-server/appsettings.json:ro",
        "dotnet-mcp-server"
      ]
    }
  }
}
```

> **Note:** Replace `/path/to/docker/appsettings.json` with the absolute path to your `docker/appsettings.json` file. On Windows use forward slashes or escape backslashes.

---

### 3. Connect to Your MCP Client

Pick your client below and follow the setup instructions.

> **Note:** All client config examples below use the global tool command `dotnet-mcp-server`. If you are using Option B (clone & build), replace `"command": "dotnet-mcp-server"` with `"command": "dotnet", "args": ["run", "--project", "C:\\path\\to\\dotnet-mcp-server\\src\\McpServer"]` instead.

---

## Supported Clients

### Claude Desktop

**Config file location:**
| OS | Path |
|----|------|
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |

**Add this to the config file:**

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "dotnet-mcp-server"
    }
  }
}
```

Restart Claude Desktop. You should see the tools available in the chat.

---

### VS Code — GitHub Copilot (Built-in)

VS Code has **native MCP support** via GitHub Copilot (agent mode). No extension needed — just VS Code 1.99+ with Copilot enabled.

**Step 1:** Open your project in VS Code.

**Step 2:** Create a `.vscode/mcp.json` file in your workspace root:

```json
{
  "servers": {
    "dotnet-mcp-server": {
      "command": "dotnet-mcp-server"
    }
  }
}
```

**Step 3:** Open the **Copilot Chat** panel (`Ctrl+Shift+I` or `Cmd+Shift+I`).

**Step 4:** Switch to **Agent mode** (click the dropdown at the top of the chat panel and select "Agent").

**Step 5:** You should see the MCP tools listed. Ask Copilot questions like *"What time is it in Tokyo?"* or *"List files in my Documents folder"*.

> **Tip:** You can also add the server globally via VS Code settings (`settings.json`):
> ```json
> {
>   "mcp": {
>     "servers": {
>       "dotnet-mcp-server": {
>         "command": "dotnet-mcp-server"
>       }
>     }
>   }
> }
> ```

---

### VS Code — Continue.dev (Open Source)

[Continue](https://continue.dev) is a free, open-source AI coding assistant for VS Code and JetBrains.

**Step 1:** Install the **Continue** extension from VS Code Marketplace.

**Step 2:** Open Continue config: press `Ctrl+Shift+P` → type `Continue: Open Config` → select it.

**Step 3:** This opens `~/.continue/config.json`. Add the MCP server under `mcpServers`:

```json
{
  "mcpServers": [
    {
      "name": "dotnet-mcp-server",
      "command": "dotnet-mcp-server"
    }
  ]
}
```

**Step 4:** Reload VS Code (`Ctrl+Shift+P` → `Developer: Reload Window`).

**Step 5:** Open Continue chat panel. The tools will be available in agent mode.

---

### VS Code — Cline (Open Source)

[Cline](https://github.com/cline/cline) is a free, open-source autonomous AI coding agent for VS Code.

**Step 1:** Install the **Cline** extension from VS Code Marketplace.

**Step 2:** Open Cline settings: click the Cline icon in the sidebar → click the **gear icon** → go to **MCP Servers**.

**Step 3:** Click **"Edit MCP Settings"** which opens `~/Documents/Cline/cline_mcp_settings.json`. Add:

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "dotnet-mcp-server"
    }
  }
}
```

**Step 4:** Restart Cline. The tools should appear in the MCP Servers section.

---

### Cursor

[Cursor](https://cursor.com) is an AI-first code editor with built-in MCP support.

**Step 1:** Open Cursor Settings → go to **MCP** section (or press `Ctrl+Shift+J`).

**Step 2:** Click **"Add new MCP Server"**.

**Step 3:** Alternatively, create/edit `~/.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "dotnet-mcp-server"
    }
  }
}
```

**Step 4:** Restart Cursor. Use the tools in Composer (Agent mode).

---

### Windsurf (Codeium)

[Windsurf](https://codeium.com/windsurf) is an AI-powered editor by Codeium with MCP support.

**Step 1:** Open Windsurf and go to **Settings** → **MCP**.

**Step 2:** Click **"Add Server"** and configure:

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "dotnet-mcp-server"
    }
  }
}
```

**Step 3:** Restart Windsurf. Tools are available in Cascade (the AI chat).

---

### Claude Code (CLI)

[Claude Code](https://docs.anthropic.com/en/docs/claude-code) is Anthropic's CLI tool. It supports MCP servers natively.

```bash
claude mcp add dotnet-mcp-server dotnet-mcp-server
```

That's it — Claude Code will auto-start the server when needed.

---

### ChatGPT Desktop

OpenAI's ChatGPT desktop app supports MCP servers (requires Plus plan).

**Step 1:** Open ChatGPT Desktop → **Settings** → **Beta Features** → enable **MCP Servers**.

**Step 2:** Go to **Settings** → **MCP Servers** → click **"Add Server"**.

**Step 3:** Configure:
- **Name:** `dotnet-mcp-server`
- **Command:** `dotnet-mcp-server`
- **Arguments:** *(leave blank)*

**Step 4:** Restart ChatGPT. Tools appear in the chat.

---

### Manual Testing (No Client Needed)

You can test the server directly from any terminal:

```bash
dotnet run --project src/McpServer
```

Then paste JSON-RPC messages line by line:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","clientInfo":{"name":"manual-test","version":"1.0"}}}
{"jsonrpc":"2.0","method":"notifications/initialized"}
{"jsonrpc":"2.0","id":2,"method":"tools/list"}
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"datetime","arguments":{"action":"now"}}}
```

> **Note:** Replace `C:\path\to\dotnet-mcp-server` with the actual path where you cloned the repo in all examples above.

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

### Text Tool

Ask Claude:
- *"Find all email addresses in this text"*
- *"Replace localhost:3000 with api.prod.com in my config"*
- *"How many words are in this document?"*
- *"Show me the diff between these two configs"*
- *"Pretty-print this minified JSON"*

### Data Transform Tool

Ask Claude:
- *"Convert this CSV to JSON"*
- *"Extract all user emails from this JSON"*
- *"Base64 encode this string"*
- *"Generate a SHA256 hash of this text"*
- *"Convert this XML response to JSON"*

### Environment Tool

Ask Claude:
- *"What is my JAVA_HOME set to?"*
- *"Show me all Node-related environment variables"*
- *"Is DOCKER_HOST configured?"*

### System Info Tool

Ask Claude:
- *"How much disk space do I have?"*
- *"What processes are using the most memory?"*
- *"What's my OS version and .NET runtime?"*
- *"Show me my network interfaces"*

### Git Tool

Ask Claude:
- *"What files have I changed in this repo?"*
- *"Show me the last 10 commits"*
- *"What's the diff of my current changes?"*
- *"Who last modified line 42 of Program.cs?"*

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
┌──────────────┐ ┌──────────┐ ┌────────┐ ┌──────────┐
│Claude Desktop│ │ VS Code  │ │ Cursor │ │ ChatGPT  │
│              │ │(Copilot/ │ │        │ │ Desktop  │
│              │ │Continue/ │ │        │ │          │
│              │ │ Cline)   │ │        │ │          │
└──────┬───────┘ └────┬─────┘ └───┬────┘ └────┬─────┘
       │              │           │            │
       └──────────────┴─────┬─────┴────────────┘
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
                 │  ├───────────────┤  │
                 │  │ SQL Query     │  │
                 │  ├───────────────┤  │
                 │  │ HTTP Tool     │  │
                 │  ├───────────────┤  │
                 │  │ Text Tool     │  │
                 │  ├───────────────┤  │
                 │  │ Data Transform│  │
                 │  ├───────────────┤  │
                 │  │ Environment   │  │
                 │  ├───────────────┤  │
                 │  │ System Info   │  │
                 │  ├───────────────┤  │
                 │  │ Git Tool      │  │
                 │  └───────────────┘  │
                 └─────────────────────┘
                            │
               ┌────┬───┬───┴───┬────┐
               ▼    ▼   ▼       ▼    ▼
             Files SQL  APIs   Git  OS
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
├── docker/
│   └── appsettings.example.json  # Docker config template
├── Dockerfile                  # Multi-stage Alpine build
├── docker-compose.yml          # mcp-server + SQL Server 2022
├── .env.example                # SQL SA password template
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
| Client doesn't see the tools | Check the config file path is correct. Restart the client. Make sure the path to `dotnet-mcp-server` is absolute. |
| "Access denied" errors | Add the path to `AllowedPaths` in `appsettings.json` |
| SQL connection fails | Verify connection string. Ensure SQL Server is running. |
| HTTP requests blocked | Add the host to `AllowedHosts` in `appsettings.json` |
| "Server not initialized" error | Your client must send `initialize` before calling tools. Most clients do this automatically. |
| VS Code Copilot doesn't show tools | Make sure you're in **Agent mode** (not Ask/Edit mode). Check `.vscode/mcp.json` syntax. |
| Tools not loading in Cursor | Go to Settings → MCP and check the server shows a green status. Restart Cursor if needed. |

### View Logs

Logs are written to stderr. To see them:
```bash
dotnet run 2> log.txt
```

---

## Roadmap

### Phase 1 — Security & Stability ✅ Complete
- [x] Fix SQL injection via subqueries (block `;`, `--`, `/* */`, compound statements, 17 dangerous keywords)
- [x] Fix path traversal edge case (trailing separator check prevents `C:\AllowedPathEvil` matching `C:\AllowedPath`)
- [x] Add initialization gate (reject `tools/list` and `tools/call` before `initialize` handshake)
- [x] Config validation on startup (warn about missing paths, empty connection strings, malformed hosts)
- [x] Expand test coverage — **8 → 63 tests** (SqlQueryValidation, FileSystemTool, HttpTool, McpServerHandler)

### Phase 2 — New Tools ✅ Complete
- [x] **Text Tool** — `regex_match`, `regex_replace`, `word_count`, `diff_text`, `format_json/xml`
- [x] **Data Transform Tool** — `json_query`, `csv_to_json`, `json_to_csv`, `xml_to_json`, `base64_encode/decode`, `hash`
- [x] **Environment Tool** — `get`, `list`, `has` (with hardcoded blocklist for sensitive vars)
- [x] **System Info Tool** — `system_info` (OS/CPU/RAM/disk), `processes`, `network`
- [x] **Git Tool** — `status`, `log`, `diff`, `branch_list`, `blame` (read-only with path validation)
- Added 5 new tools (4 → 9 total), **63 → 150 tests**, zero new NuGet dependencies

### Phase 3 — Production Readiness ✅ Complete
- [x] Dockerfile + docker-compose (one-command setup)
- [x] GitHub Actions CI/CD (build, test on push/PR via `ci.yml`; release pipeline via `release.yml`)
- [x] Self-contained single-file executables (win-x64, linux-x64, osx-arm64 — published on `v*` tags)
- [x] `dotnet tool install -g dotnet-mcp-server` distribution
- [x] `--init` config wizard for first-run setup
- [x] `--validate` health check for all configured connections

### Phase 4 — MCP Protocol Completeness ✅
- [x] Resources support (`resources/list`, `resources/read`)
- [x] Prompts support (`prompts/list`, `prompts/get`) with built-in templates
- [x] Logging protocol (`logging/setLevel`, `notifications/message`)
- [x] Progress notifications for long-running operations

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
