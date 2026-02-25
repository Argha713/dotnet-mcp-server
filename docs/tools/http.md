# HTTP Request Tool

Makes GET and POST requests to external APIs. All requests are validated against the `AllowedHosts` configuration.

!!! success "Authorization headers stay safe"
    If you include an `Authorization` header (e.g. a Bearer token or API key) in a request, it is sent directly to the target API. It is not stored, logged, or echoed back in the response. The AI does not see the value after it is used.

    For tokens you use on every request, store them in your application rather than passing them through the AI each time. See the [Security & Trust Guide](../security/trust.md).

!!! warning "Security constraints"
    - Only hosts in the `AllowedHosts` list are reachable (subdomains are automatically included)
    - Only `http://` and `https://` schemes are allowed
    - Response bodies are truncated at 10,000 characters

---

## Actions

### `get`

Sends an HTTP GET request.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `url` | string | Yes | The full URL to fetch |
| `headers` | object | No | Key-value pairs to include as request headers |

**Example:**

```json
{
  "name": "http_request",
  "arguments": {
    "action": "get",
    "url": "https://api.github.com/users/Argha713",
    "headers": {
      "User-Agent": "dotnet-mcp-server"
    }
  }
}
```

---

### `post`

Sends an HTTP POST request with a JSON body.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `url` | string | Yes | The full URL to post to |
| `body` | object | Yes | JSON body to send |
| `headers` | object | No | Key-value pairs to include as request headers |

**Example:**

```json
{
  "name": "http_request",
  "arguments": {
    "action": "post",
    "url": "https://httpbin.org/post",
    "body": {
      "key": "value"
    }
  }
}
```

---

## Configuration

```json
{
  "Http": {
    "AllowedHosts": [
      "api.github.com",
      "api.stripe.com",
      "your-internal-api.company.com"
    ],
    "TimeoutSeconds": 30
  }
}
```

!!! tip "Subdomain matching"
    Adding `github.com` also allows `api.github.com`, `raw.githubusercontent.com` and any other subdomain. You can add the apex domain to allow all subdomains, or add a specific subdomain to restrict access.

---

## Prompt Examples

- *"Get my GitHub profile info"*
- *"Fetch the latest posts from the JSONPlaceholder API"*
- *"What APIs can you access?"*
- *"Post this data to my webhook"*
- *"Check if the GitHub API is responding"*
