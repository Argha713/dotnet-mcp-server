# Environment Tool

Reads environment variables from the server process. Sensitive values are automatically masked.

!!! success "Sensitive variable values are always masked"
    Variables whose names suggest they contain secrets — passwords, tokens, API keys, private keys, credentials — are automatically masked. The AI receives `***` instead of the real value. This masking happens at the server level and cannot be bypassed by the AI.

    You can extend the blocklist in configuration to cover custom variable names specific to your environment. See the [Security & Trust Guide](../security/trust.md).

!!! warning "Security constraint"
    A hardcoded blocklist prevents exposing common secret variable names. Variables whose names contain words like `PASSWORD`, `SECRET`, `TOKEN`, `KEY`, `CREDENTIAL`, `PRIVATE`, `API_KEY`, and similar patterns return a masked value (`***`) rather than the real content.

---

## Actions

### `get`

Returns the value of a single environment variable.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `name` | string | Yes | Environment variable name |

**Example:**

```json
{
  "name": "environment",
  "arguments": {
    "action": "get",
    "name": "JAVA_HOME"
  }
}
```

---

### `list`

Lists environment variables, with optional name filtering.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `filter` | string | No | Substring filter on variable names (case-insensitive) |

**Example — list all Node-related variables:**

```json
{
  "name": "environment",
  "arguments": {
    "action": "list",
    "filter": "NODE"
  }
}
```

---

### `has`

Checks whether an environment variable exists (without revealing its value).

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `name` | string | Yes | Environment variable name to check |

---

## Prompt Examples

- *"What is my JAVA_HOME set to?"*
- *"Show me all Node-related environment variables"*
- *"Is DOCKER_HOST configured?"*
- *"What PATH directories are set?"*
- *"List all environment variables with 'HOME' in the name"*
