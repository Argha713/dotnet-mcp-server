# Contributing to dotnet-mcp-server

Thank you for your interest in contributing! This document explains how to report bugs, suggest features, and submit pull requests.

---

## Table of Contents

- [Reporting Bugs](#reporting-bugs)
- [Suggesting Features](#suggesting-features)
- [Development Setup](#development-setup)
- [Making a Pull Request](#making-a-pull-request)
- [Code Style](#code-style)
- [Running Tests](#running-tests)
- [Security Constraints](#security-constraints)

---

## Reporting Bugs

Use the **Bug Report** issue template on GitHub. Include:

- What you did (steps to reproduce)
- What you expected to happen
- What actually happened (error message, logs)
- Your OS, .NET version, and how you installed the server (`global tool`, `clone & build`, or `docker`)

Logs go to stderr. Capture them with:

```bash
dotnet run --project src/McpServer 2> debug.log
```

---

## Suggesting Features

Use the **Feature Request** issue template. Describe:

- The problem you're trying to solve
- Your proposed solution (or just the problem if you don't have one)
- Which tool or area it affects (e.g., a new `sql_query` action, a new tool, a protocol feature)

---

## Development Setup

**Prerequisites:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
git clone https://github.com/Argha713/dotnet-mcp-server.git
cd dotnet-mcp-server
dotnet build
dotnet test
```

**Run the server locally:**

```bash
dotnet run --project src/McpServer
```

**Project layout:**

```
src/
  McpServer/                  # Main server (entry point, handler, built-in tools)
  McpServer.Plugin.Abstractions/  # ITool contract (shared with plugins)
  McpServer.Templates/        # dotnet new mcp-tool template package
tests/
  McpServer.Tests/            # xUnit tests (275+)
examples/
  configs/                    # Ready-to-use appsettings profiles
  SamplePlugin/               # Reference plugin implementation
```

---

## Making a Pull Request

1. **Fork** the repo and create a branch from `master`.
2. Branch naming: `feature/short-description` or `fix/short-description`.
3. Make your changes (see [Code Style](#code-style) below).
4. Add or update tests for any logic you change.
5. Run `dotnet test` and ensure all tests pass before opening a PR.
6. Open a PR against `master`. Fill in the PR template.

---

## Code Style

- **Target framework:** .NET 8 / C# 12. No external NuGet dependencies without discussion.
- **Comment format:** All code comments must follow this exact format:
  ```csharp
  // Argha - YYYY-MM-DD - explanation of why this exists
  ```
- **Never delete code.** Comment it out using the format above, e.g.:
  ```csharp
  // Argha - 2026-02-25 - disabled old validation logic
  // if (oldCondition) { ... }
  ```
- No auto-formatters are configured. Match the surrounding style.
- No `Co-Authored-By: Claude` or similar AI attribution in commits.

---

## Running Tests

```bash
# All tests
dotnet test

# A single test class
dotnet test --filter "FullyQualifiedName~McpServer.Tests.DateTimeToolTests"

# A single test method
dotnet test --filter "FullyQualifiedName~McpServer.Tests.DateTimeToolTests.ExecuteAsync_Now_ShouldReturnCurrentTime"
```

Test framework: **xUnit** with **FluentAssertions** and **Moq**.

---

## Security Constraints

The following constraints are **intentional** and must not be weakened:

| Area | Constraint |
|------|-----------|
| FileSystem | Paths validated against `AllowedPaths` allowlist; 1 MB read limit |
| SQL | SELECT-only; 17 dangerous keywords blocked; 30s timeout; max 1000 rows |
| HTTP | Host allowlist with subdomain support; HTTP/HTTPS only; 10K char response limit |
| Git | Read-only operations; path and argument validation |
| Environment | Hardcoded blocklist for sensitive variable names |

If you believe a constraint needs changing, open an issue to discuss before submitting a PR.

---

## Adding a New Tool

1. Create a class implementing `ITool` in `src/McpServer/Tools/`.
2. Register it in `src/McpServer/Program.cs`:
   ```csharp
   services.AddSingleton<ITool, MyTool>();
   ```
3. Add tests in `tests/McpServer.Tests/`.
4. Document it in `README.md` (features table + usage examples section).

See the [Plugin Development](README.md#plugin-development) section if you want to ship a tool as a drop-in DLL without modifying the core project.
