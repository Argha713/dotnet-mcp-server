# SQL Query Tool

Executes read-only SQL queries against configured database connections. Only SELECT statements are allowed.

!!! warning "Security constraints"
    - Only `SELECT` statements are permitted
    - The following keywords are blocked: `INSERT`, `UPDATE`, `DELETE`, `DROP`, `ALTER`, `CREATE`, `TRUNCATE`, `EXEC`, `EXECUTE`, `MERGE`, `REPLACE`, `CALL`, `GRANT`, `REVOKE`, `DENY`, `USE`, `BULK`
    - Semicolons and inline comments (`--`, `/* */`) are blocked to prevent statement chaining
    - Queries time out after 30 seconds
    - Results are capped at 1,000 rows

---

## Actions

### `query`

Executes a SELECT query against a named connection.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `connection` | string | Yes | Connection name as defined in `appsettings.json` |
| `sql` | string | Yes | The SELECT query to execute |

**Example:**

```json
{
  "name": "sql_query",
  "arguments": {
    "action": "query",
    "connection": "Production",
    "sql": "SELECT TOP 10 CustomerName, Revenue FROM Customers ORDER BY Revenue DESC"
  }
}
```

---

### `list_tables`

Lists all user tables in the database.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `connection` | string | Yes | Connection name |

---

### `describe_table`

Returns the column names, types, and nullability for a table.

**Parameters:**

| Parameter | Type | Required | Description |
|-----------|------|----------|-------------|
| `connection` | string | Yes | Connection name |
| `table` | string | Yes | Table name |

---

### `list_connections`

Returns the list of configured connection names and their descriptions.

**Parameters:** none required.

---

## Configuration

```json
{
  "Sql": {
    "Connections": {
      "Production": {
        "ConnectionString": "Server=prod;Database=MyApp;Trusted_Connection=True;",
        "Description": "Production database (read-only)"
      },
      "Analytics": {
        "ConnectionString": "Server=dw;Database=Analytics;User Id=reader;Password=secret;",
        "Description": "Data warehouse"
      }
    }
  }
}
```

---

## Prompt Examples

- *"Show me the tables in the Production database"*
- *"Query the top 10 customers by revenue"*
- *"Describe the structure of the Orders table"*
- *"What databases are configured?"*
- *"How many rows are in the Products table?"*
- *"Show me orders placed in the last 30 days"*
