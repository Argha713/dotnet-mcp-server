// Argha - 2026-02-25 - Phase 6.1: PostgreSQL implementation of IDatabaseProvider
using System.Data.Common;
using System.Text;
using Npgsql;

namespace McpServer.Data;

public class PostgreSqlProvider : IDatabaseProvider
{
    public string ProviderName => "PostgreSQL";

    public DbConnection CreateConnection(string connectionString)
        => new NpgsqlConnection(connectionString);

    public string ClassifyError(Exception ex, string sanitizedConnectionString)
    {
        // Argha - 2026-02-25 - map Npgsql error codes to user-friendly messages
        if (ex is NpgsqlException npgEx)
        {
            var sqlState = npgEx.SqlState ?? "";
            var advice   = sqlState switch
            {
                "28P01" => "Login failed — wrong username or password. Check credentials in appsettings.json.",
                "28000" => "Login failed — this user is not allowed to connect. Check pg_hba.conf or user permissions.",
                "3D000" => "Database does not exist. Check the Database name in your connection string.",
                "42501" => "Permission denied. The user does not have access to the requested object.",
                "57P03" => "The PostgreSQL server is starting up. Try again in a moment.",
                _       => ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                               ? "Connection timed out. Check the Host address and that PostgreSQL is running on the expected port (default 5432)."
                               : ex.Message.Contains("refused", StringComparison.OrdinalIgnoreCase)
                                   ? "Connection refused. PostgreSQL may not be running, or the port is blocked by a firewall."
                                   : $"PostgreSQL error ({sqlState}): {npgEx.Message}"
            };

            return $"{advice}\nConnection: {sanitizedConnectionString}";
        }

        return $"Connection failed: {ex.Message}\nConnection: {sanitizedConnectionString}";
    }

    public async Task<string> ListTablesAsync(DbConnection connection, string dbName, CancellationToken ct)
    {
        // Argha - 2026-02-25 - exclude pg_catalog and information_schema (internal PostgreSQL schemas)
        const string sql = @"
            SELECT table_schema, table_name, table_type
            FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY table_schema, table_name";

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
            SELECT column_name, data_type, is_nullable, character_maximum_length, column_default
            FROM information_schema.columns
            WHERE table_schema = @Schema AND table_name = @Table
            ORDER BY ordinal_position";

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
        // Argha - 2026-02-25 - PostgreSQL default schema is 'public'
        return parts.Length > 1 ? (parts[0], parts[1]) : ("public", parts[0]);
    }

    public string BuildPartialConnectionString(string host, string? port, string database, string username)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host     = host,
            Port     = int.TryParse(port, out var p) ? p : 5432,
            Database = database,
            Username = username,
            Password = "YOUR_PASSWORD_HERE"
        };

        return builder.ConnectionString;
    }
}
