// Argha - 2026-02-25 - Phase 6.1: refactored to support SqlServer, PostgreSQL, MySQL, and SQLite
// via IDatabaseProvider abstraction. Passwords never reach the AI — see docs/security/trust.md.
using McpServer.Data;
using McpServer.Progress;
using McpServer.Protocol;
using McpServer.Configuration;
using Microsoft.Extensions.Options;
using System.Text;

namespace McpServer.Tools;

/// <summary>
/// Tool for executing read-only SQL queries against configured databases.
/// Supports SQL Server, PostgreSQL, MySQL, and SQLite.
/// Database passwords are resolved from appsettings.json by the server — they are never passed
/// through the AI and are never included in any response.
/// </summary>
public class SqlQueryTool : ITool
{
    private readonly SqlSettings _settings;

    public SqlQueryTool(IOptions<SqlSettings> settings)
    {
        _settings = settings.Value;
    }

    public string Name => "sql_query";

    public string Description =>
        "Execute read-only SQL queries against configured databases. " +
        "Supports SQL Server, PostgreSQL, MySQL, and SQLite. " +
        "Only SELECT queries are allowed. " +
        "Database passwords are stored in appsettings.json and never sent through this assistant.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string>
                {
                    "query", "list_databases", "list_tables", "describe_table",
                    "configure_connection", "test_connection"
                }
            },
            ["database"] = new()
            {
                Type = "string",
                Description = "Name of the configured database connection to use. Use 'list_databases' to see available connections."
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
            },
            // configure_connection parameters — no password field by design
            ["provider"] = new()
            {
                Type = "string",
                Description = "Database provider for configure_connection: SqlServer | PostgreSQL | MySQL | SQLite",
                Enum = new List<string> { "SqlServer", "PostgreSQL", "MySQL", "SQLite" }
            },
            ["host"] = new()
            {
                Type = "string",
                Description = "Server host or IP address (for configure_connection)"
            },
            ["port"] = new()
            {
                Type = "string",
                Description = "Server port (for configure_connection). Leave empty to use provider default."
            },
            ["db_name"] = new()
            {
                Type = "string",
                Description = "Database/schema name (for configure_connection)"
            },
            ["username"] = new()
            {
                Type = "string",
                Description = "Database username (for configure_connection). The password is added by the user directly in appsettings.json."
            },
            ["connection_name"] = new()
            {
                Type = "string",
                Description = "Logical name for the connection in appsettings.json (for configure_connection)"
            },
            ["description"] = new()
            {
                Type = "string",
                Description = "Human-readable description for the connection (for configure_connection)"
            }
        },
        Required = new List<string> { "action" }
    };

    // Argha - 2026-02-24 - added IProgressReporter; used by ExecuteQueryAsync to emit per-row notifications
    public async Task<ToolCallResult> ExecuteAsync(
        Dictionary<string, object>? arguments,
        IProgressReporter? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action") ?? "list_databases";

            var result = action.ToLower() switch
            {
                "query"                => await ExecuteQueryAsync(arguments, progress, cancellationToken),
                "list_databases"       => ListDatabases(),
                "list_tables"          => await ListTablesAsync(arguments, cancellationToken),
                "describe_table"       => await DescribeTableAsync(arguments, cancellationToken),
                "configure_connection" => ConfigureConnection(arguments),
                "test_connection"      => await TestConnectionAsync(arguments, cancellationToken),
                _ => $"Unknown action: {action}. " +
                     $"Use: query, list_databases, list_tables, describe_table, configure_connection, test_connection."
            };

            return new ToolCallResult
            {
                Content = new List<ContentBlock> { new() { Type = "text", Text = result } }
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

    // ─── list_databases ────────────────────────────────────────────────────────

    private string ListDatabases()
    {
        if (_settings.Connections.Count == 0)
            return "No database connections configured. " +
                   "Use 'configure_connection' to set one up, or add entries to appsettings.json under Sql.Connections.";

        var sb = new StringBuilder("Configured database connections:\n");
        foreach (var conn in _settings.Connections)
        {
            sb.AppendLine($"  🗄️ {conn.Key} [{conn.Value.Provider}] — {conn.Value.Description ?? "No description"}");
        }

        return sb.ToString();
    }

    // ─── query ─────────────────────────────────────────────────────────────────

    // Argha - 2026-02-24 - progress added; reports every 50 rows to avoid notification flood
    private async Task<string> ExecuteQueryAsync(
        Dictionary<string, object>? arguments,
        IProgressReporter? progress,
        CancellationToken cancellationToken)
    {
        var dbName     = GetStringArg(arguments, "database");
        var query      = GetStringArg(arguments, "query");
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

        IDatabaseProvider provider;
        try
        {
            provider = DatabaseProviderFactory.Resolve(connConfig.Provider);
        }
        catch (ArgumentException ex)
        {
            return $"Configuration error: {ex.Message}";
        }

        await using var connection = provider.CreateConnection(connConfig.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var sanitized = ConnectionStringSanitizer.Sanitize(connConfig.ConnectionString);
            return $"Error: {provider.ClassifyError(ex, sanitized)}";
        }

        await using var command = connection.CreateCommand();
        command.CommandText = query;
        command.CommandTimeout = 30;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(i => reader.GetName(i))
            .ToList();

        var sb = new StringBuilder();
        // Header
        sb.AppendLine(string.Join(" | ", columns));
        sb.AppendLine(new string('-', columns.Sum(c => c.Length) + (columns.Count - 1) * 3));

        // Argha - 2026-02-24 - emit progress(0) at start so client knows work has begun
        progress?.Report(0, maxRows);
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
            // Argha - 2026-02-24 - throttle to every 50 rows to avoid flooding the client with notifications
            if (rowCount % 50 == 0)
                progress?.Report(rowCount, maxRows);
        }
        // Argha - 2026-02-24 - final progress after loop — signals completion to client
        progress?.Report(rowCount, maxRows);

        var moreRows = await reader.ReadAsync(cancellationToken);
        var footer = moreRows
            ? $"\n... (showing {maxRows} of more rows)"
            : $"\n({rowCount} row(s))";

        return sb.ToString() + footer;
    }

    // ─── list_tables ───────────────────────────────────────────────────────────

    private async Task<string> ListTablesAsync(
        Dictionary<string, object>? arguments,
        CancellationToken cancellationToken)
    {
        var dbName = GetStringArg(arguments, "database");

        if (string.IsNullOrEmpty(dbName))
            return "Error: 'database' parameter is required.";

        if (!_settings.Connections.TryGetValue(dbName, out var connConfig))
            return $"Error: Database '{dbName}' not found.";

        IDatabaseProvider provider;
        try
        {
            provider = DatabaseProviderFactory.Resolve(connConfig.Provider);
        }
        catch (ArgumentException ex)
        {
            return $"Configuration error: {ex.Message}";
        }

        await using var connection = provider.CreateConnection(connConfig.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var sanitized = ConnectionStringSanitizer.Sanitize(connConfig.ConnectionString);
            return $"Error: {provider.ClassifyError(ex, sanitized)}";
        }

        return await provider.ListTablesAsync(connection, dbName, cancellationToken);
    }

    // ─── describe_table ────────────────────────────────────────────────────────

    private async Task<string> DescribeTableAsync(
        Dictionary<string, object>? arguments,
        CancellationToken cancellationToken)
    {
        var dbName    = GetStringArg(arguments, "database");
        var tableName = GetStringArg(arguments, "table");

        if (string.IsNullOrEmpty(dbName))
            return "Error: 'database' parameter is required.";

        if (string.IsNullOrEmpty(tableName))
            return "Error: 'table' parameter is required.";

        if (!_settings.Connections.TryGetValue(dbName, out var connConfig))
            return $"Error: Database '{dbName}' not found.";

        IDatabaseProvider provider;
        try
        {
            provider = DatabaseProviderFactory.Resolve(connConfig.Provider);
        }
        catch (ArgumentException ex)
        {
            return $"Configuration error: {ex.Message}";
        }

        await using var connection = provider.CreateConnection(connConfig.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            var sanitized = ConnectionStringSanitizer.Sanitize(connConfig.ConnectionString);
            return $"Error: {provider.ClassifyError(ex, sanitized)}";
        }

        return await provider.DescribeTableAsync(connection, tableName, cancellationToken);
    }

    // ─── configure_connection ──────────────────────────────────────────────────

    // Argha - 2026-02-25 - no password parameter by design — user adds it to appsettings.json directly
    private string ConfigureConnection(Dictionary<string, object>? arguments)
    {
        var providerName   = GetStringArg(arguments, "provider");
        var host           = GetStringArg(arguments, "host");
        var port           = GetStringArg(arguments, "port");
        var dbName         = GetStringArg(arguments, "db_name");
        var username       = GetStringArg(arguments, "username");
        var connectionName = GetStringArg(arguments, "connection_name");
        var description    = GetStringArg(arguments, "description");

        if (string.IsNullOrEmpty(providerName))
            return $"Error: 'provider' is required. Supported: {string.Join(", ", DatabaseProviderFactory.SupportedProviders)}";

        if (string.IsNullOrEmpty(host))
            return "Error: 'host' is required. For SQLite, use the file path as the host.";

        if (string.IsNullOrEmpty(dbName))
            return "Error: 'db_name' is required.";

        if (string.IsNullOrEmpty(username) && providerName != "SQLite")
            return "Error: 'username' is required for this database provider.";

        if (string.IsNullOrEmpty(connectionName))
            return "Error: 'connection_name' is required. This is the name you'll use to reference this connection (e.g. 'production', 'analytics').";

        IDatabaseProvider provider;
        try
        {
            provider = DatabaseProviderFactory.Resolve(providerName);
        }
        catch (ArgumentException ex)
        {
            return $"Error: {ex.Message}";
        }

        var partialCs = provider.BuildPartialConnectionString(host, port, dbName, username ?? "");

        var sb = new StringBuilder();
        sb.AppendLine($"Connection template generated for '{connectionName}'.");
        sb.AppendLine();
        sb.AppendLine("Add the following entry to your Sql.Connections section in appsettings.json:");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine($"\"{connectionName}\": {{");
        sb.AppendLine($"  \"Provider\": \"{provider.ProviderName}\",");
        sb.AppendLine($"  \"ConnectionString\": \"{partialCs}\",");
        sb.AppendLine($"  \"Description\": \"{description ?? ""}\"");
        sb.AppendLine("}");
        sb.AppendLine("```");
        sb.AppendLine();

        if (providerName != "SQLite")
        {
            sb.AppendLine("IMPORTANT: The connection string above contains a placeholder password.");
            sb.AppendLine("Open appsettings.json and replace YOUR_PASSWORD_HERE with the real password.");
            sb.AppendLine("Never share your password through this assistant.");
        }

        sb.AppendLine();
        sb.AppendLine($"After saving the file, run 'test_connection' with database='{connectionName}' to verify.");

        return sb.ToString();
    }

    // ─── test_connection ───────────────────────────────────────────────────────

    // Argha - 2026-02-25 - sanitizes all error output; passwords are never shown
    private async Task<string> TestConnectionAsync(
        Dictionary<string, object>? arguments,
        CancellationToken cancellationToken)
    {
        var dbName = GetStringArg(arguments, "database");

        if (string.IsNullOrEmpty(dbName))
            return "Error: 'database' parameter is required.";

        if (!_settings.Connections.TryGetValue(dbName, out var connConfig))
            return $"Error: Connection '{dbName}' not found. Use 'list_databases' to see configured connections.";

        IDatabaseProvider provider;
        try
        {
            provider = DatabaseProviderFactory.Resolve(connConfig.Provider);
        }
        catch (ArgumentException ex)
        {
            return $"Configuration error: {ex.Message}";
        }

        var sanitized = ConnectionStringSanitizer.Sanitize(connConfig.ConnectionString);

        var sb = new StringBuilder();
        sb.AppendLine($"Testing: {dbName} ({provider.ProviderName})");
        sb.AppendLine($"  Connection: {sanitized}");  // password already stripped by Sanitize
        sb.AppendLine();

        await using var connection = provider.CreateConnection(connConfig.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            sb.AppendLine("Result: CONNECTED");
            sb.AppendLine($"  Server version: {connection.ServerVersion}");
        }
        catch (Exception ex)
        {
            sb.AppendLine("Result: FAILED");
            sb.AppendLine($"  {provider.ClassifyError(ex, sanitized)}");
        }

        return sb.ToString();
    }

    // ─── validation ────────────────────────────────────────────────────────────

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
        var dangerousKeywords = new[]
        {
            "INSERT", "UPDATE", "DELETE", "DROP", "ALTER", "CREATE", "TRUNCATE",
            "EXEC", "EXECUTE", "GRANT", "REVOKE", "MERGE", "OPENROWSET",
            "OPENDATASOURCE", "BULK", "XP_", "SP_"
        };
        if (dangerousKeywords.Any(k => trimmedQuery.Contains(k, StringComparison.OrdinalIgnoreCase)))
            return "Error: Query contains forbidden keywords. Only read-only SELECT queries are allowed.";

        return null; // validation passed
    }

    // ─── helpers ───────────────────────────────────────────────────────────────

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }
}
