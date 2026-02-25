// Argha - 2026-02-25 - Phase 6.1: MySQL implementation of IDatabaseProvider
using System.Data.Common;
using System.Text;
using MySqlConnector;

namespace McpServer.Data;

public class MySqlProvider : IDatabaseProvider
{
    public string ProviderName => "MySQL";

    public DbConnection CreateConnection(string connectionString)
        => new MySqlConnection(connectionString);

    public string ClassifyError(Exception ex, string sanitizedConnectionString)
    {
        if (ex is MySqlException mysqlEx)
        {
            var advice = mysqlEx.ErrorCode switch
            {
                MySqlErrorCode.AccessDenied         => "Access denied — wrong username or password. Check credentials in appsettings.json.",
                MySqlErrorCode.UnknownDatabase       => "Database does not exist. Check the Database name in your connection string.",
                MySqlErrorCode.UnableToConnectToHost => "Cannot connect to MySQL host. Verify the Server address and port (default 3306).",
                // Argha - 2026-02-25 - error code 1040 = ER_CON_COUNT_ERROR (too many connections)
                (MySqlErrorCode)1040                 => "MySQL server has too many connections. Try again later.",
                _                                    => $"MySQL error ({(int)mysqlEx.ErrorCode}): {mysqlEx.Message}"
            };

            return $"{advice}\nConnection: {sanitizedConnectionString}";
        }

        return $"Connection failed: {ex.Message}\nConnection: {sanitizedConnectionString}";
    }

    public async Task<string> ListTablesAsync(DbConnection connection, string dbName, CancellationToken ct)
    {
        // Argha - 2026-02-25 - DATABASE() returns the current schema from the connection string
        const string sql = @"
            SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = DATABASE()
            ORDER BY TABLE_NAME";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var sb = new StringBuilder($"Tables in '{dbName}':\n");
        while (await reader.ReadAsync(ct))
        {
            var schema = reader.GetString(0);
            var table  = reader.GetString(1);
            var type   = reader.GetString(2);
            var icon   = type == "BASE TABLE" ? "📋" : "👁️";
            sb.AppendLine($"  {icon} {schema}.{table}");
        }

        return sb.Length > $"Tables in '{dbName}':\n".Length ? sb.ToString() : $"No tables found in '{dbName}'.";
    }

    public async Task<string> DescribeTableAsync(DbConnection connection, string tableName, CancellationToken ct)
    {
        var (_, table) = ParseTableName(tableName);

        // Argha - 2026-02-25 - use DATABASE() to scope to current schema; MySQL doesn't use @Schema the same way
        const string sql = @"
            SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, CHARACTER_MAXIMUM_LENGTH, COLUMN_DEFAULT
            FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @Table
            ORDER BY ORDINAL_POSITION";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        var tableParam = cmd.CreateParameter();
        tableParam.ParameterName = "@Table";
        tableParam.Value = table;
        cmd.Parameters.Add(tableParam);

        using var reader = await cmd.ExecuteReaderAsync(ct);

        var sb = new StringBuilder($"Table: {table}\n\n");
        sb.AppendLine("Column | Type | Nullable | Default");
        sb.AppendLine("-------|------|----------|--------");

        var hasRows = false;
        while (await reader.ReadAsync(ct))
        {
            hasRows = true;
            var col      = reader.GetString(0);
            var dataType = reader.GetString(1);
            var nullable = reader.GetString(2);
            var maxLen   = reader.IsDBNull(3) ? "" : $"({reader.GetValue(3)})";
            var def      = reader.IsDBNull(4) ? "" : reader.GetString(4);
            sb.AppendLine($"{col} | {dataType}{maxLen} | {nullable} | {def}");
        }

        return hasRows ? sb.ToString() : $"Table '{table}' not found.";
    }

    public (string schema, string table) ParseTableName(string tableName)
    {
        var parts = tableName.Split('.');
        // Argha - 2026-02-25 - MySQL schema == database name; we ignore it here and use DATABASE()
        return parts.Length > 1 ? (parts[0], parts[1]) : ("", parts[0]);
    }

    public string BuildPartialConnectionString(string host, string? port, string database, string username)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server   = host,
            Port     = uint.TryParse(port, out var p) ? p : 3306,
            Database = database,
            UserID   = username,
            Password = "YOUR_PASSWORD_HERE"
        };

        return builder.ConnectionString;
    }
}
