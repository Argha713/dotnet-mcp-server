// Argha - 2026-02-25 - Phase 6.1: SQLite implementation of IDatabaseProvider
// SQLite has no INFORMATION_SCHEMA — uses sqlite_master and PRAGMA table_info() instead
using System.Data.Common;
using System.Text;
using Microsoft.Data.Sqlite;

namespace McpServer.Data;

public class SqliteProvider : IDatabaseProvider
{
    public string ProviderName => "SQLite";

    public DbConnection CreateConnection(string connectionString)
        => new SqliteConnection(connectionString);

    public string ClassifyError(Exception ex, string sanitizedConnectionString)
    {
        if (ex is SqliteException sqliteEx)
        {
            var advice = sqliteEx.SqliteErrorCode switch
            {
                14  => "Cannot open database file. Check that the file path in your connection string exists and the process has read permission.",
                23  => "Access denied — the database file or directory is read-only.",
                _   => $"SQLite error ({sqliteEx.SqliteErrorCode}): {sqliteEx.Message}"
            };

            return $"{advice}\nConnection: {sanitizedConnectionString}";
        }

        return $"Connection failed: {ex.Message}\nConnection: {sanitizedConnectionString}";
    }

    public async Task<string> ListTablesAsync(DbConnection connection, string dbName, CancellationToken ct)
    {
        // Argha - 2026-02-25 - sqlite_master is the system catalog for SQLite
        const string sql = "SELECT name, type FROM sqlite_master WHERE type IN ('table','view') ORDER BY name";

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync(ct);
        var sb = new StringBuilder($"Tables in '{dbName}':\n");
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            var icon = type == "table" ? "📋" : "👁️";
            sb.AppendLine($"  {icon} {name}");
        }

        return sb.Length > $"Tables in '{dbName}':\n".Length ? sb.ToString() : $"No tables found in '{dbName}'.";
    }

    public async Task<string> DescribeTableAsync(DbConnection connection, string tableName, CancellationToken ct)
    {
        var (_, table) = ParseTableName(tableName);

        // Argha - 2026-02-25 - PRAGMA table_info does not accept parameters; we validate the name first
        // to prevent SQL injection via table name before interpolating into the PRAGMA statement
        if (!IsValidIdentifier(table))
            return $"Invalid table name: '{table}'. Table names may only contain letters, numbers, and underscores.";

        var sql = $"PRAGMA table_info(\"{table}\")";
        // Pragma returns: cid, name, type, notnull, dflt_value, pk

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync(ct);

        var sb = new StringBuilder($"Table: {table}\n\n");
        sb.AppendLine("Column | Type | Nullable | Default | PK");
        sb.AppendLine("-------|------|----------|---------|---");

        var hasRows = false;
        while (await reader.ReadAsync(ct))
        {
            hasRows = true;
            var col      = reader.GetString(1);  // name
            var dataType = reader.GetString(2);  // type
            var notNull  = reader.GetInt32(3);   // notnull (1 = NOT NULL)
            var def      = reader.IsDBNull(4) ? "" : reader.GetString(4); // dflt_value
            var pk       = reader.GetInt32(5);   // pk (>0 means primary key)
            var nullable = notNull == 1 ? "NO" : "YES";
            var pkMark   = pk > 0 ? "PK" : "";
            sb.AppendLine($"{col} | {dataType} | {nullable} | {def} | {pkMark}");
        }

        return hasRows ? sb.ToString() : $"Table '{table}' not found.";
    }

    public (string schema, string table) ParseTableName(string tableName)
    {
        // Argha - 2026-02-25 - SQLite has no schema concept; ignore any schema prefix
        var parts = tableName.Split('.');
        return ("", parts.Length > 1 ? parts[1] : parts[0]);
    }

    public string BuildPartialConnectionString(string host, string? port, string database, string username)
    {
        // Argha - 2026-02-25 - for SQLite, 'host' is the file path; port/username are unused
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = database.Length > 0 ? database : host
        };

        return builder.ConnectionString;
    }

    // Argha - 2026-02-25 - safeguard before interpolating table name into PRAGMA statement
    private static bool IsValidIdentifier(string name)
        => !string.IsNullOrWhiteSpace(name) &&
           name.All(c => char.IsLetterOrDigit(c) || c == '_');
}
