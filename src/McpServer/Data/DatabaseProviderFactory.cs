// Argha - 2026-02-25 - Phase 6.1: resolves the correct IDatabaseProvider from the Provider string in config
namespace McpServer.Data;

public static class DatabaseProviderFactory
{
    // Argha - 2026-02-25 - provider instances are stateless so we share them
    private static readonly SqlServerProvider  _sqlServer  = new();
    private static readonly PostgreSqlProvider _postgreSql = new();
    private static readonly MySqlProvider      _mySql      = new();
    private static readonly SqliteProvider     _sqlite     = new();

    /// <summary>
    /// Returns the provider for the given name (case-insensitive).
    /// Throws ArgumentException with a helpful message if the name is not recognised.
    /// </summary>
    public static IDatabaseProvider Resolve(string providerName)
    {
        return providerName.Trim() switch
        {
            var n when n.Equals("SqlServer",  StringComparison.OrdinalIgnoreCase) => _sqlServer,
            var n when n.Equals("PostgreSQL", StringComparison.OrdinalIgnoreCase) => _postgreSql,
            var n when n.Equals("Postgres",   StringComparison.OrdinalIgnoreCase) => _postgreSql,
            var n when n.Equals("MySQL",      StringComparison.OrdinalIgnoreCase) => _mySql,
            var n when n.Equals("SQLite",     StringComparison.OrdinalIgnoreCase) => _sqlite,
            _ => throw new ArgumentException(
                $"Unknown database provider '{providerName}'. " +
                $"Supported values: SqlServer, PostgreSQL, MySQL, SQLite")
        };
    }

    /// <summary>
    /// Returns all known provider names — used in error messages and configure_connection hints.
    /// </summary>
    public static IReadOnlyList<string> SupportedProviders =>
        ["SqlServer", "PostgreSQL", "MySQL", "SQLite"];
}
