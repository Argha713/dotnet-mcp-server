// Argha - 2026-02-25 - Phase 6.1: abstraction over ADO.NET providers so SqlQueryTool is engine-agnostic
using System.Data.Common;

namespace McpServer.Data;

/// <summary>
/// Abstracts a database engine. Each provider knows how to create connections and introspect schema.
/// </summary>
public interface IDatabaseProvider
{
    /// <summary>
    /// Provider key used in appsettings.json (e.g. "SqlServer", "PostgreSQL", "MySQL", "SQLite").
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    /// Creates and returns an unopened DbConnection for the given connection string.
    /// </summary>
    DbConnection CreateConnection(string connectionString);

    /// <summary>
    /// Classifies a connection exception into a plain-English message safe to send to the AI.
    /// Passwords are never included — the sanitized connection string is provided by the caller.
    /// </summary>
    string ClassifyError(Exception ex, string sanitizedConnectionString);

    /// <summary>
    /// Lists all user tables/views using the open connection. Returns formatted text.
    /// </summary>
    Task<string> ListTablesAsync(DbConnection connection, string dbName, CancellationToken ct);

    /// <summary>
    /// Describes columns for the given table using the open connection. Returns formatted text.
    /// </summary>
    Task<string> DescribeTableAsync(DbConnection connection, string tableName, CancellationToken ct);

    /// <summary>
    /// Parses "schema.table" notation into (schema, table). Falls back to a provider-specific default schema.
    /// </summary>
    (string schema, string table) ParseTableName(string tableName);

    /// <summary>
    /// Builds a partial connection string from individual parameters (no password).
    /// Used by the configure_connection action so passwords are never passed through the AI.
    /// </summary>
    string BuildPartialConnectionString(string host, string? port, string database, string username);
}
