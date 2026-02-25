# System Info Tool

Returns information about the host machine: OS details, running processes, and network interfaces.

---

## Actions

### `system_info`

Returns a summary of the host operating system and hardware.

**Parameters:** none required.

**Returns:** OS name and version, .NET runtime version, architecture, processor count, total/available RAM, disk drive information.

**Example:**

```json
{
  "name": "system_info",
  "arguments": {
    "action": "system_info"
  }
}
```

---

### `processes`

Lists currently running processes.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `filter` | string | No | Substring filter on process name |
| `sort_by` | string | No | Sort field: `"name"` (default), `"memory"`, `"cpu"` |
| `top` | integer | No | Return only the top N processes (default: all) |

**Example — top 10 processes by memory:**

```json
{
  "name": "system_info",
  "arguments": {
    "action": "processes",
    "sort_by": "memory",
    "top": 10
  }
}
```

---

### `network`

Lists network interfaces and their IP addresses.

**Parameters:** none required.

**Returns:** Interface name, type, status, and associated IP addresses (IPv4 and IPv6).

---

## Prompt Examples

- *"How much disk space do I have?"*
- *"What processes are using the most memory?"*
- *"What's my OS version and .NET runtime?"*
- *"Show me my network interfaces"*
- *"Is the node process running?"*
- *"How many CPU cores does this machine have?"*
