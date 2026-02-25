# Connecting Clients

dotnet-mcp-server works with any MCP-compatible client. Choose your client below.

!!! tip "Global tool vs. clone & build"
    All examples below use `"command": "dotnet-mcp-server"` (global tool). If you installed via Option B (clone & build), replace it with:
    ```json
    "command": "dotnet",
    "args": ["run", "--project", "C:\\path\\to\\dotnet-mcp-server\\src\\McpServer"]
    ```

---

## Claude Desktop

**Config file location:**

| OS | Path |
|----|------|
| Windows | `%APPDATA%\Claude\claude_desktop_config.json` |
| macOS | `~/Library/Application Support/Claude/claude_desktop_config.json` |

Add this to the config file:

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "dotnet-mcp-server"
    }
  }
}
```

Restart Claude Desktop. The tools appear in the chat interface.

**Docker variant:**

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "docker",
      "args": [
        "run", "--rm", "-i",
        "-v", "/absolute/path/to/docker/appsettings.json:/home/app/.config/dotnet-mcp-server/appsettings.json:ro",
        "dotnet-mcp-server"
      ]
    }
  }
}
```

---

## VS Code — GitHub Copilot (Built-in)

VS Code 1.99+ has native MCP support via GitHub Copilot agent mode. No extension required.

**Step 1:** Create `.vscode/mcp.json` in your workspace root:

```json
{
  "servers": {
    "dotnet-mcp-server": {
      "command": "dotnet-mcp-server"
    }
  }
}
```

**Step 2:** Open Copilot Chat (`Ctrl+Shift+I` / `Cmd+Shift+I`).

**Step 3:** Switch to **Agent mode** via the dropdown at the top of the chat panel.

**Step 4:** Ask a question — the tools will be listed and callable.

To enable globally instead of per-workspace, add to your VS Code `settings.json`:

```json
{
  "mcp": {
    "servers": {
      "dotnet-mcp-server": {
        "command": "dotnet-mcp-server"
      }
    }
  }
}
```

---

## VS Code — Continue.dev

[Continue](https://continue.dev) is a free, open-source AI coding assistant.

**Step 1:** Install the Continue extension from the VS Code Marketplace.

**Step 2:** Press `Ctrl+Shift+P` → `Continue: Open Config` to open `~/.continue/config.json`.

**Step 3:** Add the server under `mcpServers`:

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

---

## VS Code — Cline

[Cline](https://github.com/cline/cline) is a free, open-source autonomous AI coding agent.

**Step 1:** Install the Cline extension from the VS Code Marketplace.

**Step 2:** Click the Cline icon → gear icon → **MCP Servers** → **Edit MCP Settings**.

**Step 3:** This opens `~/Documents/Cline/cline_mcp_settings.json`. Add:

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "dotnet-mcp-server"
    }
  }
}
```

**Step 4:** Restart Cline.

---

## Cursor

[Cursor](https://cursor.com) is an AI-first code editor with built-in MCP support.

**Option 1:** Create or edit `~/.cursor/mcp.json`:

```json
{
  "mcpServers": {
    "dotnet-mcp-server": {
      "command": "dotnet-mcp-server"
    }
  }
}
```

**Option 2:** Open Cursor Settings → **MCP** section → click **"Add new MCP Server"** and fill in the fields.

Restart Cursor. Use the tools in Composer (Agent mode).

---

## Windsurf (Codeium)

[Windsurf](https://codeium.com/windsurf) is an AI-powered editor by Codeium.

**Step 1:** Open Settings → **MCP** → click **"Add Server"**.

**Step 2:** Configure:

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

## Claude Code (CLI)

[Claude Code](https://docs.anthropic.com/en/docs/claude-code) is Anthropic's CLI tool.

```bash
claude mcp add dotnet-mcp-server dotnet-mcp-server
```

Claude Code auto-starts the server when needed.

---

## ChatGPT Desktop

OpenAI's ChatGPT desktop app supports MCP servers (requires Plus plan).

**Step 1:** Open Settings → **Beta Features** → enable **MCP Servers**.

**Step 2:** Go to Settings → **MCP Servers** → **"Add Server"**.

**Step 3:** Configure:

- **Name:** `dotnet-mcp-server`
- **Command:** `dotnet-mcp-server`
- **Arguments:** *(leave blank)*

**Step 4:** Restart ChatGPT.
