# SQL Query Tool

Executes read-only SQL queries against configured database connections. Supports **SQL Server, PostgreSQL, MySQL, and SQLite**. Only SELECT statements are allowed.

!!! success "Your passwords never reach the AI"
    Database passwords are stored in `appsettings.json` on your machine. The AI only ever receives a connection **name** (e.g. `"production"`). The server resolves the password internally. Passwords are never passed through the AI conversation and are stripped from all error messages before any response is sent.

    See the [Security & Trust Guide](../security/trust.md) for the full explanation.

!!! warning "Security constraints"
    - Only `SELECT` statements are permitted
    - The following keywords are blocked: `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `CREATE`, `TRUNCATE`, `EXEC`, `EXECUTE`, `MERGE`, `REPLACE`, `CALL`, `GRANT`, `REVOKE`, `DENY`, `USE`, `BULK`
    - Semicolons and inline comments (`--`, `/* */`) are blocked to prevent statement chaining
    - Queries time out after 30 seconds
    - Results are capped at 1,000 rows

---

## Supported Databases

| Provider key | Database engine | Default port |
|-------------|----------------|-------------|
| `SqlServer` | Microsoft SQL Server (2017+) | 1433 |
| `PostgreSQL` | PostgreSQL (12+) | 5432 |
| `MySQL` | MySQL / MariaDB (5.7+) | 3306 |
| `SQLite` | SQLite (any version) | — (file-based) |

---

## Actions

### `list_databases`

Lists all configured connection names and their provider types.

**Parameters:** none required.

**Example response:**
```
Configured database connections:
  🗄️ production [SqlServer] — Production database
  🗄️ analytics [PostgreSQL] — Analytics warehouse
  🗄️ local [SQLite] — Local dev database
```

---

### `query`

Executes a SELECT query against a named connection.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `database` | string | Yes | Connection name as defined in `appsettings.json` |
| `query` | string | Yes | The SELECT query to execute |
| `max_rows` | string | No | Maximum rows to return (default: 100, max: 1000) |

**Example:**

```json
{
  "name": "sql_query",
  "arguments": {
    "action": "query",
    "database": "production",
    "query": "SELECT TOP 10 CustomerName, Revenue FROM Customers ORDER BY Revenue DESC"
  }
}
```

---

### `list_tables`

Lists all user tables and views in a configured database.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `database` | string | Yes | Connection name |

---

### `describe_table`

Returns column names, types, nullability, and defaults for a table.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `database` | string | Yes | Connection name |
| `table` | string | Yes | Table name (optionally schema-qualified, e.g. `sales.Orders`) |

---

### `configure_connection`

Guides you through setting up a new database connection. Generates a partial connection string (without password) that you paste into `appsettings.json`.

!!! info "Why no password parameter?"
    This action intentionally has no `password` field. Passwords must never be passed through an AI conversation. The action generates a connection string template with a `YOUR_PASSWORD_HERE` placeholder. You add the real password directly to `appsettings.json`.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `provider` | string | Yes | `SqlServer`, `PostgreSQL`, `MySQL`, or `SQLite` |
| `host` | string | Yes | Server hostname or IP (for SQLite: the file path) |
| `port` | string | No | Port number. Leave empty for provider default |
| `db_name` | string | Yes | Database or schema name |
| `username` | string | No | Database username (not required for SQLite) |
| `connection_name` | string | Yes | Name for this connection in your config (e.g. `"production"`) |
| `description` | string | No | Human-readable description |

**Example:**

```json
{
  "name": "sql_query",
  "arguments": {
    "action": "configure_connection",
    "provider": "PostgreSQL",
    "host": "myserver.example.com",
    "port": "5432",
    "db_name": "analytics",
    "username": "readonly",
    "connection_name": "analytics",
    "description": "Analytics database"
  }
}
```

**Example response:**

```
Connection template generated for 'analytics'.

Add the following entry to your Sql.Connections section in appsettings.json:

"analytics": {
  "Provider": "PostgreSQL",
  "ConnectionString": "Host=myserver.example.com;Port=5432;Database=analytics;Username=readonly;Password=YOUR_PASSWORD_HERE",
  "Description": "Analytics database"
}

IMPORTANT: The connection string above contains a placeholder password.
Open appsettings.json and replace YOUR_PASSWORD_HERE with the real password.
Never share your password through this assistant.
```

---

### `test_connection`

Tests whether a configured connection is working. Returns a human-readable result with the error cause if it fails. Passwords are never shown.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `database` | string | Yes | Connection name to test |

**Example response (success):**

```
Testing: analytics (PostgreSQL)
  Connection: Host=myserver.example.com;Port=5432;Database=analytics;Username=readonly;Password=***

Result: CONNECTED
  Server version: 15.3
```

**Example response (failure):**

```
Testing: analytics (PostgreSQL)
  Connection: Host=myserver.example.com;Port=5432;Database=analytics;Username=readonly;Password=***

Result: FAILED
  Login failed — wrong username or password. Check credentials in appsettings.json.
  Connection: Host=myserver.example.com;...;Password=***
```

---

## Configuration

```json
{
  "Sql": {
    "Connections": {
      "production": {
        "Provider": "SqlServer",
        "ConnectionString": "Server=prod-server;Database=MyApp;User Id=reader;Password=YOUR_PASSWORD_HERE;TrustServerCertificate=True",
        "Description": "Production SQL Server (read-only account)"
      },
      "analytics": {
        "Provider": "PostgreSQL",
        "ConnectionString": "Host=dw.example.com;Port=5432;Database=analytics;Username=reader;Password=YOUR_PASSWORD_HERE",
        "Description": "Analytics PostgreSQL warehouse"
      },
      "app-mysql": {
        "Provider": "MySQL",
        "ConnectionString": "Server=mysql.example.com;Port=3306;Database=appdb;Uid=reader;Pwd=YOUR_PASSWORD_HERE",
        "Description": "Application MySQL database"
      },
      "local": {
        "Provider": "SQLite",
        "ConnectionString": "Data Source=C:\\Users\\You\\data\\local.db",
        "Description": "Local SQLite database"
      }
    }
  }
}
```

!!! tip "Backwards compatibility"
    Existing configurations without a `Provider` field automatically use `SqlServer`. No migration required.

---

## Prompt Examples

- *"What databases are configured?"*
- *"Set up a new PostgreSQL connection for my analytics database"*
- *"Test the production connection"*
- *"Show me the tables in the analytics database"*
- *"Query the top 10 customers by revenue from production"*
- *"Describe the structure of the Orders table"*
- *"How many rows are in the Products table?"*
- *"Show me orders placed in the last 30 days"*
