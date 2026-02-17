using McpServer.Protocol;
using McpServer.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Data.SqlClient;
using System.Text;

namespace McpServer.Tools;

/// <summary>
/// Tool for executing read-only SQL queries against configured databases
/// </summary>
public class SqlQueryTool : ITool
{
    private readonly SqlSettings _settings;

    public SqlQueryTool(IOptions<SqlSettings> settings)
    {
        _settings = settings.Value;
    }

    public string Name => "sql_query";

    public string Description => "Execute read-only SQL queries against configured databases. Use this to retrieve data from SQL Server databases. Only SELECT queries are allowed for safety.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "query", "list_databases", "list_tables", "describe_table" }
            },
            ["database"] = new()
            {
                Type = "string",
                Description = "Name of the configured database connection to use"
            },
            ["query"] = new()
            {
                Type = "string",
                Description = "SQL SELECT query to execute"
            },
            ["table"] = new()
            {
                Type = "string",
                Description = "Table name for 'describe_table' action"
            },
            ["max_rows"] = new()
            {
                Type = "string",
                Description = "Maximum number of rows to return (default: 100)",
                Default = "100"
            }
        },
        Required = new List<string> { "action" }
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action") ?? "list_databases";

            var result = action.ToLower() switch
            {
                "query" => await ExecuteQueryAsync(arguments, cancellationToken),
                "list_databases" => ListDatabases(),
                "list_tables" => await ListTablesAsync(arguments, cancellationToken),
                "describe_table" => await DescribeTableAsync(arguments, cancellationToken),
                _ => $"Unknown action: {action}. Use 'query', 'list_databases', 'list_tables', or 'describe_table'."
            };

            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = result }
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = $"Error: {ex.Message}" }
                },
                IsError = true
            };
        }
    }

    private string ListDatabases()
    {
        if (_settings.Connections.Count == 0)
            return "No database connections configured. Add connections to appsettings.json under Sql.Connections.";

        var sb = new StringBuilder("Configured database connections:\n");
        foreach (var conn in _settings.Connections)
        {
            sb.AppendLine($"  🗄️ {conn.Key}: {conn.Value.Description ?? "No description"}");
        }
        return sb.ToString();
    }

    private async Task<string> ExecuteQueryAsync(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var dbName = GetStringArg(arguments, "database");
        var query = GetStringArg(arguments, "query");
        var maxRowsStr = GetStringArg(arguments, "max_rows") ?? "100";

        if (string.IsNullOrEmpty(dbName))
            return "Error: 'database' parameter is required. Use 'list_databases' to see available connections.";

        if (string.IsNullOrEmpty(query))
            return "Error: 'query' parameter is required.";

        if (!_settings.Connections.TryGetValue(dbName, out var connConfig))
            return $"Error: Database '{dbName}' not found. Use 'list_databases' to see available connections.";

        // Argha - 2026-02-17 - comprehensive SQL injection prevention
        var validationError = ValidateQuery(query);
        if (validationError != null)
            return validationError;

        if (!int.TryParse(maxRowsStr, out var maxRows) || maxRows < 1)
            maxRows = 100;
        maxRows = Math.Min(maxRows, 1000); // Cap at 1000

        await using var connection = new SqlConnection(connConfig.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new SqlCommand(query, connection);
        command.CommandTimeout = 30;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var sb = new StringBuilder();
        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.GetName(i))
            .ToList();

        // Header
        sb.AppendLine(string.Join(" | ", columns));
        sb.AppendLine(new string('-', columns.Sum(c => c.Length) + (columns.Count - 1) * 3));

        // Data rows
        var rowCount = 0;
        while (await reader.ReadAsync(cancellationToken) && rowCount < maxRows)
        {
            var values = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "";
                values.Add(value.Length > 50 ? value.Substring(0, 47) + "..." : value);
            }
            sb.AppendLine(string.Join(" | ", values));
            rowCount++;
        }

        var moreRows = await reader.ReadAsync(cancellationToken);
        var footer = moreRows ? $"\n... (showing {maxRows} of more rows)" : $"\n({rowCount} row(s))";

        return sb.ToString() + footer;
    }

    private async Task<string> ListTablesAsync(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var dbName = GetStringArg(arguments, "database");

        if (string.IsNullOrEmpty(dbName))
            return "Error: 'database' parameter is required.";

        if (!_settings.Connections.TryGetValue(dbName, out var connConfig))
            return $"Error: Database '{dbName}' not found.";

        await using var connection = new SqlConnection(connConfig.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var query = @"
            SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE 
            FROM INFORMATION_SCHEMA.TABLES 
            ORDER BY TABLE_SCHEMA, TABLE_NAME";

        await using var command = new SqlCommand(query, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var sb = new StringBuilder($"Tables in '{dbName}':\n");
        while (await reader.ReadAsync(cancellationToken))
        {
            var schema = reader.GetString(0);
            var table = reader.GetString(1);
            var type = reader.GetString(2);
            var icon = type == "BASE TABLE" ? "📋" : "👁️";
            sb.AppendLine($"  {icon} {schema}.{table}");
        }

        return sb.ToString();
    }

    private async Task<string> DescribeTableAsync(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var dbName = GetStringArg(arguments, "database");
        var tableName = GetStringArg(arguments, "table");

        if (string.IsNullOrEmpty(dbName))
            return "Error: 'database' parameter is required.";

        if (string.IsNullOrEmpty(tableName))
            return "Error: 'table' parameter is required.";

        if (!_settings.Connections.TryGetValue(dbName, out var connConfig))
            return $"Error: Database '{dbName}' not found.";

        await using var connection = new SqlConnection(connConfig.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // Split schema.table if provided
        var parts = tableName.Split('.');
        var schema = parts.Length > 1 ? parts[0] : "dbo";
        var table = parts.Length > 1 ? parts[1] : parts[0];

        var query = @"
            SELECT 
                c.COLUMN_NAME,
                c.DATA_TYPE,
                c.IS_NULLABLE,
                c.CHARACTER_MAXIMUM_LENGTH,
                c.COLUMN_DEFAULT
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @Table
            ORDER BY c.ORDINAL_POSITION";

        await using var command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@Schema", schema);
        command.Parameters.AddWithValue("@Table", table);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var sb = new StringBuilder($"Table: {schema}.{table}\n\n");
        sb.AppendLine("Column | Type | Nullable | Default");
        sb.AppendLine("-------|------|----------|--------");

        var hasRows = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            hasRows = true;
            var colName = reader.GetString(0);
            var dataType = reader.GetString(1);
            var nullable = reader.GetString(2);
            var maxLen = reader.IsDBNull(3) ? "" : $"({reader.GetValue(3)})";
            var defaultVal = reader.IsDBNull(4) ? "" : reader.GetString(4);

            sb.AppendLine($"{colName} | {dataType}{maxLen} | {nullable} | {defaultVal}");
        }

        if (!hasRows)
            return $"Table '{schema}.{table}' not found.";

        return sb.ToString();
    }

    // Argha - 2026-02-17 - extracted SQL validation into a dedicated method for testability
    internal static string? ValidateQuery(string query)
    {
        var trimmedQuery = query.Trim();

        // Must start with SELECT
        if (!trimmedQuery.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return "Error: Only SELECT queries are allowed for safety. Use SELECT to retrieve data.";

        // Block semicolons — prevents compound statements like "SELECT 1; DROP TABLE x"
        if (trimmedQuery.Contains(';'))
            return "Error: Semicolons are not allowed in queries. Only single SELECT statements are permitted.";

        // Block SQL comments — prevents hiding malicious code
        if (trimmedQuery.Contains("--") || trimmedQuery.Contains("/*") || trimmedQuery.Contains("*/"))
            return "Error: SQL comments (-- or /* */) are not allowed in queries.";

        // Check for dangerous keywords
        var dangerousKeywords = new[] { "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE", "EXEC", "EXECUTE", "GRANT", "REVOKE", "MERGE", "OPENROWSET", "OPENDATASOURCE", "BULK", "XP_", "SP_" };
        if (dangerousKeywords.Any(k => trimmedQuery.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return "Error: Query contains forbidden keywords. Only read-only SELECT queries are allowed.";

        return null; // validation passed
    }

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }
}
