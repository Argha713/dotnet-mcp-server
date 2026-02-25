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

## Phase 6 — Advanced Features

- [ ] Multi-database support (PostgreSQL, MySQL, SQLite)
- [ ] Response caching with configurable TTL
- [ ] Audit logging (every tool call logged to file)
- [ ] Rate limiting per tool
- [ ] Tool-level authentication & permissions
