// Argha - 2026-02-25 - Phase 6.1: SQL Server implementation of IDatabaseProvider
using System.Data.Common;
using System.Text;
using Microsoft.Data.SqlClient;

namespace McpServer.Data;

public class SqlServerProvider : IDatabaseProvider
{
    public string ProviderName => "SqlServer";

    public DbConnection CreateConnection(string connectionString)
        => new SqlConnection(connectionString);

    public string ClassifyError(Exception ex, string sanitizedConnectionString)
    {
        if (ex is SqlException sqlEx)
        {
            var advice = sqlEx.Number switch
            {
                18456 => "Login failed — wrong username or password. Check credentials in appsettings.json.",
                18452 => "Login failed — this account is not configured for SQL Server authentication.",
                4060  => "Database does not exist on this server. Check the Database name in your connection string.",
                2     => $"Cannot reach the server. Verify the Server address and that SQL Server is running.",
                -1    => "Network error reaching SQL Server. Check the Server address, port (default 1433), and firewall rules.",
                -2    => "Connection timed out. The server may be under load or unreachable.",
                _     => sqlEx.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
                         sqlEx.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase)
                             ? "SSL/TLS certificate error. For local/dev use, add TrustServerCertificate=True to your connection string."
                             : $"SQL Server error ({sqlEx.Number}): {sqlEx.Message}"
            };

            return $"{advice}\nConnection: {sanitizedConnectionString}";
        }

        return $"Connection failed: {ex.Message}\nConnection: {sanitizedConnectionString}";
    }

    public async Task<string> ListTablesAsync(DbConnection connection, string dbName, CancellationToken ct)
    {
        const string sql = @"
            SELECT TABLE_SCHEMA, TABLE_NAME, TABLE_TYPE
            FROM INFORMATION_SCHEMA.TABLES
            ORDER BY TABLE_SCHEMA, TABLE_NAME";

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
        var (schema, table) = ParseTableName(tableName);

        const string sql = @"
            SELECT c.COLUMN_NAME, c.DATA_TYPE, c.IS_NULLABLE, c.CHARACTER_MAXIMUM_LENGTH, c.COLUMN_DEFAULT
            FROM INFORMATION_SCHEMA.COLUMNS c
            WHERE c.TABLE_SCHEMA = @Schema AND c.TABLE_NAME = @Table
            ORDER BY c.ORDINAL_POSITION";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        var schemaParam = cmd.CreateParameter();
        schemaParam.ParameterName = "@Schema";
        schemaParam.Value = schema;
        cmd.Parameters.Add(schemaParam);

        var tableParam = cmd.CreateParameter();
        tableParam.ParameterName = "@Table";
        tableParam.Value = table;
        cmd.Parameters.Add(tableParam);

        using var reader = await cmd.ExecuteReaderAsync(ct);

        var sb = new StringBuilder($"Table: {schema}.{table}\n\n");
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

        return hasRows ? sb.ToString() : $"Table '{schema}.{table}' not found.";
    }

    public (string schema, string table) ParseTableName(string tableName)
    {
        var parts = tableName.Split('.');
        return parts.Length > 1 ? (parts[0], parts[1]) : ("dbo", parts[0]);
    }

    public string BuildPartialConnectionString(string host, string? port, string database, string username)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource          = string.IsNullOrEmpty(port) ? host : $"{host},{port}",
            InitialCatalog      = database,
            UserID              = username,
            Password            = "",
            TrustServerCertificate = true  // Argha - 2026-02-25 - default to true so new users don't hit TLS errors
        };

        // Argha - 2026-02-25 - remove the empty password key so user's appsettings.json has a clear placeholder
        var cs = builder.ConnectionString.Replace("Password=;", "Password=YOUR_PASSWORD_HERE;");
        return cs;
    }
}
