# FileSystem Tool

Reads files and lists directory contents. All paths are validated against the `AllowedPaths` configuration before any operation is performed.

!!! success "Your files stay private"
    The AI can only access directories you explicitly list in `AllowedPaths`. It cannot browse your full filesystem, read files outside those directories, or access sensitive locations like SSH key folders, password manager databases, or `.env` files unless you intentionally allow those paths. Never add your home directory or credential directories to `AllowedPaths`.

    See the [Security & Trust Guide](../security/trust.md) for details.

!!! warning "Security constraint"
    Only paths that are a subdirectory of (or equal to) a configured `AllowedPaths` entry are accessible. Path traversal attempts (e.g. `../etc/passwd`) are blocked. Files larger than 1 MB cannot be read.

---

## Actions

### `read_file`

Reads the content of a file.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Absolute path to the file |

**Example:**

```json
{
  "name": "filesystem",
  "arguments": {
    "action": "read_file",
    "path": "C:\\Projects\\myapp\\README.md"
  }
}
```

---

### `list_directory`

Lists files and subdirectories at the given path.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Absolute path to the directory |

**Example:**

```json
{
  "name": "filesystem",
  "arguments": {
    "action": "list_directory",
    "path": "C:\\Projects\\myapp"
  }
}
```

---

### `search_files`

Recursively searches for files matching a pattern within an allowed directory.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `path` | string | Yes | Root directory to search from |
| `pattern` | string | Yes | Glob pattern (e.g. `"*.cs"`, `"**/*.json"`) |

**Example:**

```json
{
  "name": "filesystem",
  "arguments": {
    "action": "search_files",
    "path": "C:\\Projects\\myapp",
    "pattern": "*.cs"
  }
}
```

---

## Configuration

Add directories to the allowlist in `appsettings.json`:

```json
{
  "FileSystem": {
    "AllowedPaths": [
      "C:\\Users\\YourName\\Documents",
      "C:\\Projects",
      "/home/user/projects"
    ]
  }
}
```

Subdirectories are automatically included. You do not need to list them separately.

---

## Prompt Examples

- *"Read the contents of README.md"*
- *"List files in my Documents folder"*
- *"Search for all .cs files in my projects"*
- *"What's in the src directory?"*
- *"Show me the appsettings.json file"*
