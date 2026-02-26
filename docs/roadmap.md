# Roadmap

---

## Phase 1 — Security & Stability ✅

- [x] Fix SQL injection via subqueries (block `;`, `--`, `/* */`, compound statements, 17 dangerous keywords)
- [x] Fix path traversal edge case (trailing separator check)
- [x] Add initialization gate (reject `tools/list` and `tools/call` before `initialize` handshake)
- [x] Config validation on startup (warn about missing paths, empty connection strings, malformed hosts)
- [x] Expand test coverage — **8 → 63 tests**

---

## Phase 2 — New Tools ✅

- [x] **Text Tool** — `regex_match`, `regex_replace`, `word_count`, `diff_text`, `format_json/xml`
- [x] **Data Transform Tool** — `json_query`, `csv_to_json`, `json_to_csv`, `xml_to_json`, `base64_encode/decode`, `hash`
- [x] **Environment Tool** — `get`, `list`, `has` (with sensitive variable masking)
- [x] **System Info Tool** — `system_info`, `processes`, `network`
- [x] **Git Tool** — `status`, `log`, `diff`, `branch_list`, `blame` (read-only)
- [x] Added 5 new tools (4 → 9 total), **63 → 150 tests**, zero new NuGet dependencies

---

## Phase 3 — Production Readiness ✅

- [x] Dockerfile + docker-compose (one-command setup)
- [x] GitHub Actions CI/CD (`ci.yml` on push/PR; `release.yml` on `v*` tags)
- [x] Self-contained single-file executables (win-x64, linux-x64, osx-arm64)
- [x] `dotnet tool install -g DotnetMcpServer` distribution
- [x] `--init` config wizard for first-run setup
- [x] `--validate` health check for all configured connections

---

## Phase 4 — MCP Protocol Completeness ✅

- [x] Resources support (`resources/list`, `resources/read`)
- [x] Prompts support (`prompts/list`, `prompts/get`) with built-in templates
- [x] Logging protocol (`logging/setLevel`, `notifications/message`)
- [x] Progress notifications for long-running operations

---

## Phase 5 — Developer Experience ✅

- [x] Plugin architecture (drop-in tool DLLs from `plugins/` folder)
- [x] `dotnet new mcp-tool` project template for custom tools
- [x] Documentation site (this site)
- [x] Example configurations (`developer.json`, `data-analyst.json`, `api-integrator.json`)
- [x] `CONTRIBUTING.md` + issue templates

---

## Phase 6 — Advanced Features ✅

### Phase 6.1 — Multi-Database Support ✅

- [x] `IDatabaseProvider` abstraction — SQL tool is now engine-agnostic
- [x] SQL Server provider (refactored from original hardcoded implementation)
- [x] PostgreSQL provider (Npgsql 8.x)
- [x] MySQL / MariaDB provider (MySqlConnector 2.x)
- [x] SQLite provider (Microsoft.Data.Sqlite 8.x, uses `sqlite_master` + `PRAGMA table_info`)
- [x] `configure_connection` action — guided setup without password, writes template to appsettings.json
- [x] `test_connection` action — human-readable diagnostics, passwords never shown
- [x] `ConnectionStringSanitizer` — passwords stripped from all error output
- [x] Per-provider error classification (maps error codes to plain-English fix instructions)
- [x] Security & Trust documentation — explains why secrets never reach the AI
- [x] Per-tool "Secrets Stay Safe" callouts across all tool docs

---

### Phase 6.2 — Audit Logging ✅

- [x] Every tool call recorded to rolling daily JSONL files (`audit-yyyy-MM-dd.jsonl`)
- [x] Captures: timestamp, correlation ID, tool name, action, sanitized arguments, outcome, error message, duration (ms)
- [x] Sensitive argument values (password, token, api_key, etc.) replaced with `[REDACTED]` before writing to disk
- [x] Configurable retention — files older than `Audit:RetentionDays` (default: 30) deleted on startup
- [x] Log directory defaults to `{configDir}/audit/`, fully configurable via `Audit:LogDirectory`
- [x] Audit failures are non-fatal — tool response always returned even if disk write fails
- [x] Disable entirely with `Audit:Enabled: false`

---

### Phase 6.3 — Rate Limiting ✅

- [x] Per-tool sliding window rate limiter (1-minute window)
- [x] `RateLimit:DefaultLimitPerMinute` (default: 60) applies to any tool without an explicit override
- [x] `RateLimit:ToolLimits` — per-tool overrides (e.g. `"sql_query": 20`, `"datetime": 0` for unlimited)
- [x] Rate-limited calls return a clear `IsError` response with the tool name — AI can read and understand it
- [x] Rate-limited calls are recorded in the audit log with outcome `"RateLimited"`
- [x] Thread-safe per-tool buckets (`lock` per bucket, `ConcurrentDictionary` across tools)
- [x] Disable entirely with `RateLimit:Enabled: false`

---

### Phase 6.4 — Response Caching ✅

- [x] In-memory response cache with per-tool TTL configuration
- [x] `Cache:DefaultTtlSeconds` (default: 60s) applies to any tool without an explicit override
- [x] `Cache:ToolTtls` — per-tool TTL overrides (`"datetime": 0` to bypass, `"sql_query": 300` for 5 min, etc.)
- [x] TTL of 0 bypasses the cache entirely for that tool (volatile tools: datetime, system_info, git, environment)
- [x] Only successful results cached (`IsError = false`); transient errors always re-executed
- [x] Cache hits audited with outcome `"CacheHit"` in the audit log
- [x] Bounded cache capacity (`Cache:MaxEntries`, default 1000); evicts expired entries first, then oldest-inserted
- [x] Thread-safe `ConcurrentDictionary` backing store with lock-guarded eviction
- [x] Disable entirely with `Cache:Enabled: false`

---

## Phase 7 — Tool-level Authentication & Permissions ✅

- [x] API key authentication layer — clients present `MCP_API_KEY` env var (set in Claude Desktop's `env` block)
- [x] Per-key authorization — each key maps to an explicit `AllowedTools` list (`"*"` = all tools)
- [x] Per-tool action allowlists — `AllowedActions` restricts individual actions within a tool
- [x] `NullAuthorizationService` singleton — auth disabled by default (`Auth:Enabled: false`); fully backwards-compatible
- [x] Auth check happens before rate limiting — unauthorized calls don't consume rate-limit tokens
- [x] Unauthorized calls return `IsError=true` with a clear message — AI can read and understand it
- [x] Audit log gains `clientIdentity` field — shows which key made each call
- [x] Unauthorized calls audited with outcome `"Unauthorized"`
- [x] Key is case-sensitive; tool names and action names are case-insensitive
