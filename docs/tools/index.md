# Tools Overview

dotnet-mcp-server ships with nine built-in tools. Each tool uses an `action` parameter to dispatch sub-operations.

| Tool | Actions | Best for |
|------|---------|----------|
| [DateTime](datetime.md) | `now`, `convert` | Current time, timezone conversion |
| [FileSystem](filesystem.md) | `read_file`, `list_directory`, `search_files` | Reading files and navigating directories |
| [SQL Query](sql.md) | `query`, `list_tables`, `describe_table`, `list_connections` | Read-only database queries |
| [HTTP Request](http.md) | `get`, `post` | Calling external REST APIs |
| [Text](text.md) | `regex_match`, `regex_replace`, `word_count`, `diff_text`, `format_json`, `format_xml` | Text processing and formatting |
| [Data Transform](data-transform.md) | `json_query`, `csv_to_json`, `json_to_csv`, `xml_to_json`, `base64_encode`, `base64_decode`, `hash` | Format conversion and encoding |
| [Environment](environment.md) | `get`, `list`, `has` | Reading environment variables |
| [System Info](system-info.md) | `system_info`, `processes`, `network` | OS details, running processes, network |
| [Git](git.md) | `status`, `log`, `diff`, `branch_list`, `blame` | Read-only Git repository inspection |

---

## How Tools Work

All tools communicate via JSON-RPC 2.0. The AI sends a `tools/call` request:

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/call",
  "params": {
    "name": "datetime",
    "arguments": {
      "action": "now"
    }
  }
}
```

The server routes by `name`, then dispatches by `action` inside the tool.

---

## Security Model

Each tool has its own security constraints that are enforced at the server level and cannot be bypassed:

- **FileSystem** — paths validated against an allowlist; 1 MB read limit
- **SQL** — SELECT-only; 17 dangerous keywords blocked; 30s timeout; max 1,000 rows
- **HTTP** — host allowlist with subdomain support; HTTP/HTTPS only; 10K char response limit
- **Git** — read-only operations; argument sanitization
- **Environment** — hardcoded blocklist for sensitive variable names

See the [Security Constraints](../configuration/security.md) page for full details.

---

## Plugin Tools

You can add your own tools without modifying the server. See the [Plugin Development](../plugins/index.md) section.
