# Contributing

Thank you for your interest in contributing! This page explains how to report bugs, suggest features, and submit pull requests.

---

## Reporting Bugs

Use the **[Bug Report](https://github.com/Argha713/dotnet-mcp-server/issues/new?template=bug_report.yml)** issue template. Include:

- Steps to reproduce
- Expected vs. actual behavior
- Error message or logs
- Your OS, .NET version, and installation method (`global tool`, `clone & build`, or `docker`)

Capture logs with:

```bash
dotnet run --project src/McpServer 2> debug.log
```

---

## Suggesting Features

Use the **[Feature Request](https://github.com/Argha713/dotnet-mcp-server/issues/new?template=feature_request.yml)** template. Describe:

- The problem you want to solve
- Your proposed solution (or just the problem — a solution is not required)
- Which tool or area it affects

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
  McpServer/                       # Main server (entry point, handler, built-in tools)
  McpServer.Plugin.Abstractions/   # ITool contract NuGet package
  McpServer.Templates/             # dotnet new mcp-tool template package
tests/
  McpServer.Tests/                 # xUnit tests (275+)
examples/
  configs/                         # Ready-to-use appsettings profiles
  SamplePlugin/                    # Reference plugin implementation
```

---

## Making a Pull Request

1. Fork the repo and create a branch from `master`
2. Use branch naming: `feature/short-description` or `fix/short-description`
3. Make your changes following the code style below
4. Add or update tests for any logic you change
5. Run `dotnet test` — all tests must pass
6. Open a PR against `master` and fill in the PR template

---

## Code Style

- **Target framework:** .NET 8 / C# 12. No new NuGet dependencies without prior discussion in an issue.
- **Comment format:** All code comments must follow this exact format:
  ```csharp
  // Argha - YYYY-MM-DD - explanation of why this exists
  ```
- **Never delete code.** Comment it out using the format above:
  ```csharp
  // Argha - 2026-02-25 - disabled old validation logic
  // if (oldCondition) { ... }
  ```
- No auto-formatters — match the surrounding style
- No `Co-Authored-By: Claude` or similar AI attribution in commits

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

The following constraints are **intentional** and must not be weakened without an issue discussion:

| Area | Constraint |
|------|-----------|
| FileSystem | Path allowlist; 1 MB read limit |
| SQL | SELECT-only; 17 dangerous keywords blocked; 30s timeout; max 1,000 rows |
| HTTP | Host allowlist with subdomain support; HTTP/HTTPS only; 10K char response limit |
| Git | Read-only operations; path and argument validation |
| Environment | Hardcoded blocklist for sensitive variable names |

See the [Security Constraints](configuration/security.md) page for the full rationale.

---

## Adding a New Tool

1. Create a class implementing `ITool` in `src/McpServer/Tools/`
2. Register it in `src/McpServer/Program.cs`:
   ```csharp
   services.AddSingleton<ITool, MyTool>();
   ```
3. Add tests in `tests/McpServer.Tests/`
4. Document it in `README.md` (features table + usage examples section)

For a drop-in DLL without modifying the core project, see [Plugin Development](plugins/quickstart.md).
