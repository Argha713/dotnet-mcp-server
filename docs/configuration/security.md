# Security Constraints

dotnet-mcp-server is designed with a **defense-in-depth** approach. Every tool has security constraints enforced at the server level. These are **intentional design decisions** — they exist to make the server safe to run with AI clients that could be manipulated by prompt injection.

!!! danger "Do not weaken these constraints"
    If you believe a constraint needs changing, open an issue to discuss before submitting a PR. The security model is intentional.

---

## FileSystem

| Constraint | Detail |
|-----------|--------|
| **Path allowlist** | Only paths in `AllowedPaths` (and their subdirectories) are accessible |
| **Traversal prevention** | A path like `C:\AllowedPathEvil` will never match `C:\AllowedPath` — separator boundary checks prevent this |
| **Read limit** | Files larger than 1 MB cannot be read |
| **No write operations** | The tool is read-only — no create, write, delete, or rename |

**Why this matters:** Without a path allowlist, a prompt injection attack could instruct the AI to read `/etc/passwd`, SSH private keys, or application credentials. The 1 MB read limit prevents the AI from being used to exfiltrate large files.

---

## SQL

| Constraint | Detail |
|-----------|--------|
| **SELECT-only** | All queries must be `SELECT` statements |
| **Keyword blocklist** | 17 dangerous keywords blocked: `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `CREATE`, `TRUNCATE`, `EXEC`, `EXECUTE`, `MERGE`, `REPLACE`, `CALL`, `GRANT`, `REVOKE`, `DENY`, `USE`, `BULK` |
| **Compound statement prevention** | Semicolons blocked to prevent statement chaining |
| **Comment blocking** | `--` and `/* */` blocked to prevent injection via comments |
| **Query timeout** | 30-second timeout on all queries |
| **Row limit** | Maximum 1,000 rows returned |

**Why this matters:** An AI client under prompt injection could attempt to run destructive queries. By restricting to SELECT and blocking all write keywords at the application layer (in addition to any database-level permissions you configure), the risk is significantly reduced.

---

## HTTP

| Constraint | Detail |
|-----------|--------|
| **Host allowlist** | Only hosts in `AllowedHosts` are reachable |
| **Subdomain matching** | Host matching includes exact match and subdomain match only |
| **Scheme restriction** | Only `http://` and `https://` are allowed — no `file://`, `ftp://`, etc. |
| **Response truncation** | Responses are truncated at 10,000 characters |

**Why this matters:** Without a host allowlist, the server could be used to probe internal network services, exfiltrate data to arbitrary endpoints, or perform server-side request forgery (SSRF). The scheme restriction prevents access to local file system via `file://` URLs.

---

## Git

| Constraint | Detail |
|-----------|--------|
| **Read-only operations** | Only `status`, `log`, `diff`, `branch_list`, and `blame` are available |
| **Argument sanitization** | All arguments are validated before being passed to Git to prevent injection |
| **Path validation** | Repository paths are validated the same way as filesystem paths |

**Why this matters:** Git commands with write access (push, commit, reset, merge) could destroy history or exfiltrate code. The tool intentionally exposes only introspection operations.

---

## Environment

| Constraint | Detail |
|-----------|--------|
| **Sensitive value masking** | Variables matching sensitive name patterns return `***` instead of their value |
| **Blocked patterns** | Names containing `PASSWORD`, `SECRET`, `TOKEN`, `KEY`, `CREDENTIAL`, `PRIVATE`, `PWD`, `APIKEY`, and similar words are masked |

**Why this matters:** Environment variables often contain database passwords, API tokens, and private keys. Masking prevents the AI from inadvertently exposing these in a conversation transcript.

---

## Text & Data Transform

| Constraint | Detail |
|-----------|--------|
| **ReDoS protection** | Regex operations run with a timeout to prevent catastrophic backtracking |
| **XXE prevention** | XML parsing uses a hardened `XmlReader` that disables external entity resolution |

---

## General

- **No arbitrary code execution** — there is no `eval`, `exec`, or shell-command tool
- **No network discovery** — the system_info tool reports local interfaces only
- **JSON-RPC over stdio** — the server is a subprocess; it has no listening TCP port to attack directly

---

## Reporting Security Issues

If you discover a security vulnerability, **do not open a public issue**. Please use the [GitHub Security Advisory](https://github.com/Argha713/dotnet-mcp-server/security/advisories/new) feature to report it privately.
