# dotnet-mcp-server

A **.NET 8 MCP server** that exposes enterprise tools to AI assistants — Claude Desktop, VS Code, Cursor, Windsurf, and more.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![C#](https://img.shields.io/badge/C%23-12-239120?style=flat&logo=csharp)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![MCP](https://img.shields.io/badge/MCP-2024--11--05-blue?style=flat)](https://modelcontextprotocol.io)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](https://github.com/Argha713/dotnet-mcp-server/blob/master/LICENSE)
[![CI](https://github.com/Argha713/dotnet-mcp-server/actions/workflows/ci.yml/badge.svg)](https://github.com/Argha713/dotnet-mcp-server/actions/workflows/ci.yml)

---

## Install in 60 seconds

```bash
dotnet tool install -g DotnetMcpServer
dotnet-mcp-server --init
```

The `--init` wizard creates your config file. Then connect any MCP-compatible client and start asking questions.

[Full Installation Guide →](getting-started/installation.md){ .md-button .md-button--primary }
[Connect Your Client →](getting-started/clients.md){ .md-button }

---

## What is MCP?

The **Model Context Protocol** is Anthropic's open standard that lets AI assistants connect to external data sources and tools. Instead of copying data into prompts, the AI calls tools directly.

This server brings MCP to the .NET ecosystem. It works with **any MCP-compatible client**.

---

## Nine Built-in Tools

| Tool | What it does |
|------|-------------|
| [DateTime](tools/datetime.md) | Current time, timezone conversions |
| [FileSystem](tools/filesystem.md) | Read files, list directories (within allowed paths) |
| [SQL Query](tools/sql.md) | Read-only SQL queries against configured databases |
| [HTTP Request](tools/http.md) | GET/POST to allowed external APIs |
| [Text](tools/text.md) | Regex, word count, diff, JSON/XML formatting |
| [Data Transform](tools/data-transform.md) | JSON query, CSV/JSON/XML conversion, base64, hashing |
| [Environment](tools/environment.md) | Get/list environment variables (sensitive values masked) |
| [System Info](tools/system-info.md) | OS details, processes, network interfaces |
| [Git](tools/git.md) | Read-only Git: status, log, diff, branches, blame |

---

## Security by Default

Every tool ships with security constraints that are **on by default** and cannot be bypassed at runtime:

- File access restricted to configured directories only
- SQL queries are SELECT-only (17 dangerous keywords blocked)
- HTTP requests limited to an allowed host list
- Environment variable masking for secrets
- Git operations are read-only with argument sanitization
- Regex timeout protection against ReDoS attacks
- XXE prevention in XML parsing

[Full Security Reference →](configuration/security.md)

---

## Extend with Plugins

Write your own tool as a .NET class library and drop the DLL in the `plugins/` folder — no fork required.

```bash
dotnet new install DotnetMcpServer.Templates
dotnet new mcp-tool -n WeatherTool
```

[Plugin Development Guide →](plugins/quickstart.md)
