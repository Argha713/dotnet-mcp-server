# Security & Trust

This page explains exactly how dotnet-mcp-server protects your passwords, API keys, and other secrets — and why you can trust it when connecting AI assistants to your real systems.

---

## The Core Promise

> **Your passwords, connection strings, and API keys never leave your machine and are never seen by the AI.**

This is not a policy. It is a structural guarantee built into how the server works.

---

## How It Works — The Architecture

When you ask an AI assistant to query your database, the flow looks like this:

```
You → AI Assistant → dotnet-mcp-server → Your Database
                          ↑
                    reads appsettings.json
                    (never sent to AI)
```

The AI assistant only ever sees **names** — not secrets.

For example, when you say *"query the production database"*, the AI sends this to the server:

```json
{
  "action": "query",
  "database": "production",
  "query": "SELECT * FROM Orders"
}
```

The AI sends the **name** `"production"`. The server looks up the actual connection string — including the password — from `appsettings.json` on your machine. That lookup happens inside the server process. The password never travels through the AI conversation.

---

## Why The AI Cannot See Your Secrets

There are three layers that enforce this:

### 1. The tool schema has no password field

Every tool exposes a schema that describes what parameters it accepts. The `sql_query` tool has parameters for `action`, `database`, `query`, `table` — and **no `password` field**. The AI literally cannot ask for a password because the parameter does not exist in the protocol.

### 2. Configuration is offline

Your `appsettings.json` file is read directly by the server process on startup. It is never read through the AI, never passed as a tool argument, and never sent anywhere. The AI has no way to request the contents of your config file.

### 3. Errors are sanitized before reaching the AI

If a connection fails, the raw error message from the database driver can sometimes contain the full connection string — including the password. dotnet-mcp-server strips all sensitive fields from every error message before sending it back. The AI sees:

```
Login failed. Connection: Server=myserver;Database=mydb;User Id=sa;Password=***
```

Not:

```
Login failed. Connection: Server=myserver;Database=mydb;User Id=sa;Password=MyRealPassword123
```

This sanitization runs unconditionally on every error path. There is no code path that can accidentally leak a password.

---

## Per-Tool Guarantees

### SQL Query Tool

| What stays safe | How |
|----------------|-----|
| Database passwords | Stored in `appsettings.json`, never in AI conversation |
| Connection strings | Resolved by server using connection **name** — string never sent to AI |
| Error messages | Passwords stripped from all error output before response |
| Dangerous queries | Only `SELECT` allowed — AI cannot instruct the server to delete or modify data |

When you configure a database, use the `configure_connection` action to provide the host, port, database name, and username. The server writes a partial connection string to `appsettings.json`. You then **add the password directly to that file yourself** — it never goes through the AI.

### FileSystem Tool

| What stays safe | How |
|----------------|-----|
| Files outside your allowed paths | Path allowlist enforced server-side — AI cannot access files outside configured directories |
| SSH private keys, `.env` files, credential stores | Only accessible if you explicitly add those directories to `AllowedPaths` (which you should not do) |

The AI can only access files you explicitly permit. It cannot browse your entire filesystem.

### HTTP Request Tool

| What stays safe | How |
|----------------|-----|
| Authorization headers and Bearer tokens | If you pass an `Authorization` header in a request, it goes directly to the target API — it does not get stored, logged, or echoed back |
| Internal network endpoints | Host allowlist prevents the AI from probing internal services not on your approved list |

!!! note "Recommendation"
    For API keys used on every request (e.g. a service token), configure them in `appsettings.json` as a default header rather than passing them through the AI each time. This keeps the key out of your conversation history entirely. *(Coming in Phase 7 — Authentication & Permissions)*

### Environment Tool

| What stays safe | How |
|----------------|-----|
| Passwords, tokens, API keys in env vars | Variables whose names contain `PASSWORD`, `SECRET`, `TOKEN`, `KEY`, `CREDENTIAL`, `PRIVATE`, `PWD`, `APIKEY` return `***` instead of the real value |
| Custom sensitive variables | Add variable name patterns to `AdditionalBlockedVariables` in config to extend the blocklist |

The masking happens at the server level — the value is replaced before being included in any response. There is no way for the AI to bypass this.

### Git Tool

| What stays safe | How |
|----------------|-----|
| Git credentials (username/password, SSH keys) | The tool only reads repository state (status, log, diff). It never touches credential storage |
| Commit content | Read-only — the AI cannot push, commit, or modify history |

---

## What We Don't Do

- We do not send telemetry. The server is a local subprocess with no outbound connections of its own.
- We do not log your queries or file reads to any external service.
- We do not have a cloud component. Everything runs on your machine.
- We do not require an account or registration.

---

## Coming Next — Authentication & Permissions

Phase 7 of the roadmap adds explicit per-tool authentication and permissions:

- **Per-connection API keys** — store keys in config, never pass through AI
- **Tool-level access control** — restrict which AI clients can call which tools
- **Audit logging** — every tool call written to a local log file with timestamp and arguments

When Phase 7 lands, this page will be updated with the full details.

[View Roadmap →](../roadmap.md){ .md-button }

---

## Reporting a Security Issue

If you find a vulnerability, **do not open a public GitHub issue**. Use the [GitHub Security Advisory](https://github.com/Argha713/dotnet-mcp-server/security/advisories/new) to report it privately.
