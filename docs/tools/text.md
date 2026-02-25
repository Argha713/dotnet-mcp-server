# Text Tool

Performs text processing operations: regex matching and replacement, word counting, diff, and JSON/XML formatting.

!!! info "ReDoS protection"
    All regex operations run with a timeout to prevent Regular Expression Denial of Service (ReDoS) attacks caused by catastrophically backtracking patterns.

---

## Actions

### `regex_match`

Finds all matches of a regex pattern in the input text.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `text` | string | Yes | The input text to search |
| `pattern` | string | Yes | Regular expression pattern |
| `case_insensitive` | boolean | No | Whether to ignore case (default: `false`) |

**Example:**

```json
{
  "name": "text",
  "arguments": {
    "action": "regex_match",
    "text": "Contact us at hello@example.com or support@test.org",
    "pattern": "[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\\.[a-zA-Z]{2,}"
  }
}
```

---

### `regex_replace`

Replaces all matches of a regex pattern with a replacement string.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `text` | string | Yes | The input text |
| `pattern` | string | Yes | Regular expression pattern to match |
| `replacement` | string | Yes | Replacement string (supports `$1`, `$2` capture group references) |
| `case_insensitive` | boolean | No | Whether to ignore case (default: `false`) |

**Example:**

```json
{
  "name": "text",
  "arguments": {
    "action": "regex_replace",
    "text": "Server=localhost:3000;",
    "pattern": "localhost:3000",
    "replacement": "api.prod.com"
  }
}
```

---

### `word_count`

Counts words, characters, lines, and sentences in the text.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `text` | string | Yes | The input text |

---

### `diff_text`

Computes a line-by-line diff between two text inputs.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `original` | string | Yes | Original text |
| `modified` | string | Yes | Modified text |

---

### `format_json`

Pretty-prints minified or poorly formatted JSON.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `text` | string | Yes | JSON string to format |

---

### `format_xml`

Pretty-prints minified or poorly formatted XML.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `text` | string | Yes | XML string to format |

---

## Prompt Examples

- *"Find all email addresses in this text"*
- *"Replace localhost:3000 with api.prod.com in my config"*
- *"How many words are in this document?"*
- *"Show me the diff between these two configs"*
- *"Pretty-print this minified JSON"*
- *"Format this XML response"*
