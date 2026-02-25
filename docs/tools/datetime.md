# DateTime Tool

Returns the current date and time, or converts a time between timezones.

---

## Actions

### `now`

Returns the current UTC time and the server's local time.

**Parameters:** none required.

**Example:**

```json
{
  "name": "datetime",
  "arguments": {
    "action": "now"
  }
}
```

---

### `convert`

Converts a time from one timezone to another.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `time` | string | Yes | Time to convert (e.g. `"3:00 PM"`, `"15:00"`, `"2026-02-25T15:00:00"`) |
| `from_timezone` | string | Yes | Source timezone (e.g. `"EST"`, `"America/New_York"`, `"UTC"`) |
| `to_timezone` | string | Yes | Target timezone (e.g. `"IST"`, `"Asia/Kolkata"`, `"UTC+5:30"`) |

**Example:**

```json
{
  "name": "datetime",
  "arguments": {
    "action": "convert",
    "time": "3:00 PM",
    "from_timezone": "EST",
    "to_timezone": "IST"
  }
}
```

---

## Prompt Examples

Ask your AI assistant:

- *"What time is it right now?"*
- *"What time is it in Tokyo?"*
- *"Convert 3pm EST to IST"*
- *"What's the current UTC time?"*
- *"If it's 9am in New York, what time is it in London?"*
