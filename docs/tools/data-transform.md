# Data Transform Tool

Converts between data formats, queries JSON, encodes/decodes data, and computes hashes.

!!! info "XXE prevention"
    XML parsing uses a hardened `XmlReader` configuration to prevent XML External Entity (XXE) injection attacks.

---

## Actions

### `json_query`

Queries a JSON structure using a JSONPath-like expression.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `json` | string | Yes | JSON string to query |
| `query` | string | Yes | Query expression (e.g. `$.users[*].email`) |

**Example:**

```json
{
  "name": "data_transform",
  "arguments": {
    "action": "json_query",
    "json": "{\"users\": [{\"email\": \"a@b.com\"}, {\"email\": \"c@d.com\"}]}",
    "query": "$.users[*].email"
  }
}
```

---

### `csv_to_json`

Converts a CSV string to a JSON array of objects.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `csv` | string | Yes | CSV input (first row is treated as the header) |
| `delimiter` | string | No | Column delimiter (default: `","`) |

---

### `json_to_csv`

Converts a JSON array of objects to CSV.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `json` | string | Yes | JSON array of objects |

---

### `xml_to_json`

Converts XML to a JSON representation.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `xml` | string | Yes | XML string to convert |

---

### `base64_encode`

Encodes a UTF-8 string to Base64.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `text` | string | Yes | Text to encode |

---

### `base64_decode`

Decodes a Base64 string back to UTF-8.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `text` | string | Yes | Base64-encoded string to decode |

---

### `hash`

Computes a cryptographic hash of the input text.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `text` | string | Yes | Text to hash |
| `algorithm` | string | No | Hash algorithm: `"sha256"` (default), `"sha512"`, `"md5"` |

---

## Prompt Examples

- *"Convert this CSV to JSON"*
- *"Extract all user emails from this JSON"*
- *"Base64 encode this string"*
- *"Generate a SHA256 hash of this text"*
- *"Convert this XML API response to JSON"*
- *"Transform this JSON data into a CSV file"*
