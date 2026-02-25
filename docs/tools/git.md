# Git Tool

Performs read-only Git operations on local repositories. All paths and arguments are validated before execution.

!!! success "Your Git credentials are never exposed"
    This tool only reads repository state. It never interacts with your Git credential store, SSH keys, or authentication tokens. The AI cannot push, commit, or modify your repository in any way — the tool is strictly read-only.

    See the [Security & Trust Guide](../security/trust.md).

!!! warning "Security constraint"
    Only read-only Git commands are allowed. Write operations (commit, push, reset, checkout, merge, etc.) are not exposed. Repository paths are validated and argument injection is prevented.

---

## Actions

### `status`

Returns the working tree status of a repository.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Absolute path to the Git repository root |

---

### `log`

Returns recent commits from the repository history.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Absolute path to the repository root |
| `count` | integer | No | Number of commits to return (default: `10`) |
| `branch` | string | No | Branch name (default: current branch) |

---

### `diff`

Returns the diff of uncommitted changes, or the diff between two commits/branches.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Absolute path to the repository root |
| `from` | string | No | Base commit/branch (default: working tree diff) |
| `to` | string | No | Target commit/branch |

---

### `branch_list`

Lists all local (and optionally remote) branches.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Absolute path to the repository root |
| `include_remote` | boolean | No | Include remote-tracking branches (default: `false`) |

---

### `blame`

Returns the blame annotation for a file, showing who last modified each line.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Absolute path to the repository root |
| `file` | string | Yes | Path to the file, relative to the repository root |
| `from_line` | integer | No | Starting line number |
| `to_line` | integer | No | Ending line number |

---

## Prompt Examples

- *"What files have I changed in this repo?"*
- *"Show me the last 10 commits"*
- *"What's the diff of my current changes?"*
- *"Who last modified line 42 of Program.cs?"*
- *"List all branches in this repository"*
- *"Show me the commit history for the main branch"*
