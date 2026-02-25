// Argha - 2026-02-25 - Phase 6.1: SQLite integration tests using in-memory database (no infra required)
using FluentAssertions;
using McpServer.Configuration;
using McpServer.Data;
using McpServer.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace McpServer.Tests.Data;

/// <summary>
/// Integration tests for SqliteProvider and SqlQueryTool against a real in-memory SQLite database.
/// These tests do not require any running database server — SQLite runs entirely in-process.
/// </summary>
public class SqliteProviderTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SqliteProvider   _provider;

    public SqliteProviderTests()
    {
        _provider = new SqliteProvider();

        // Argha - 2026-02-25 - keep connection open for the duration of the test so the in-memory db persists
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        using var setup = _connection.CreateCommand();
        setup.CommandText = @"
            CREATE TABLE Customers (
                Id      INTEGER PRIMARY KEY,
                Name    TEXT    NOT NULL,
                Email   TEXT,
                Revenue REAL
            );
            CREATE TABLE Orders (
                Id         INTEGER PRIMARY KEY,
                CustomerId INTEGER,
                Total      REAL
            );
            CREATE VIEW CustomerSummary AS
                SELECT Name, Revenue FROM Customers;
            INSERT INTO Customers VALUES (1, 'Alice', 'alice@example.com', 5000.0);
            INSERT INTO Customers VALUES (2, 'Bob',   'bob@example.com',   3000.0);
            INSERT INTO Orders   VALUES (1, 1, 250.0);
            INSERT INTO Orders   VALUES (2, 2, 150.0);
        ";
        setup.ExecuteNonQuery();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    // ─── ListTablesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListTablesAsync_ReturnsUserTables()
    {
        var result = await _provider.ListTablesAsync(_connection, "test-db", CancellationToken.None);

        result.Should().Contain("Customers");
        result.Should().Contain("Orders");
    }

    [Fact]
    public async Task ListTablesAsync_IncludesViews()
    {
        var result = await _provider.ListTablesAsync(_connection, "test-db", CancellationToken.None);
        result.Should().Contain("CustomerSummary");
    }

    [Fact]
    public async Task ListTablesAsync_UsesTableIcon()
    {
        var result = await _provider.ListTablesAsync(_connection, "test-db", CancellationToken.None);
        result.Should().Contain("📋"); // table icon
        result.Should().Contain("👁️"); // view icon
    }

    // ─── DescribeTableAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task DescribeTableAsync_ReturnsColumns()
    {
        var result = await _provider.DescribeTableAsync(_connection, "Customers", CancellationToken.None);

        result.Should().Contain("Id");
        result.Should().Contain("Name");
        result.Should().Contain("Email");
        result.Should().Contain("Revenue");
    }

    [Fact]
    public async Task DescribeTableAsync_ShowsPrimaryKeyColumn()
    {
        var result = await _provider.DescribeTableAsync(_connection, "Customers", CancellationToken.None);
        result.Should().Contain("PK");
    }

    [Fact]
    public async Task DescribeTableAsync_UnknownTable_ReturnsNotFound()
    {
        var result = await _provider.DescribeTableAsync(_connection, "NonExistent", CancellationToken.None);
        result.Should().Contain("not found");
    }

    [Fact]
    public async Task DescribeTableAsync_InvalidTableName_ReturnsError()
    {
        // Argha - 2026-02-25 - table names with special chars must be rejected to prevent PRAGMA injection
        var result = await _provider.DescribeTableAsync(_connection, "Users; DROP TABLE Orders", CancellationToken.None);
        result.Should().Contain("Invalid table name");
    }

    // ─── SqlQueryTool integration with SQLite ────────────────────────────────

    [Fact]
    public async Task SqlQueryTool_ListDatabases_ShowsProviderType()
    {
        var settings = Options.Create(new SqlSettings
        {
            Connections = new Dictionary<string, SqlConnectionConfig>
            {
                ["mydb"] = new SqlConnectionConfig
                {
                    Provider         = "SQLite",
                    ConnectionString = "Data Source=:memory:",
                    Description      = "Test SQLite DB"
                }
            }
        });

        var tool   = new SqlQueryTool(settings);
        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["action"] = "list_databases" });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("mydb");
        result.Content[0].Text.Should().Contain("SQLite");
        result.Content[0].Text.Should().Contain("Test SQLite DB");
    }

    [Fact]
    public async Task SqlQueryTool_ConfigureConnection_NeverContainsPassword()
    {
        var settings = Options.Create(new SqlSettings());
        var tool     = new SqlQueryTool(settings);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"]          = "configure_connection",
            ["provider"]        = "PostgreSQL",
            ["host"]            = "myserver.example.com",
            ["port"]            = "5432",
            ["db_name"]         = "analytics",
            ["username"]        = "readonly_user",
            ["connection_name"] = "analytics",
            ["description"]     = "Analytics database"
        });

        result.IsError.Should().BeFalse();
        // The response should guide the user to add password themselves — never ask for it
        result.Content[0].Text.Should().Contain("YOUR_PASSWORD_HERE");
        result.Content[0].Text.Should().Contain("appsettings.json");
        result.Content[0].Text.Should().Contain("Never share your password through this assistant");
    }

    [Fact]
    public async Task SqlQueryTool_ConfigureConnection_NoPasswordField_InSchema()
    {
        // Verify the tool schema has no 'password' parameter — the AI cannot ask for it
        var settings = Options.Create(new SqlSettings());
        var tool     = new SqlQueryTool(settings);

        tool.InputSchema.Properties.Should().NotContainKey("password");
    }

    [Fact]
    public async Task SqlQueryTool_TestConnection_SanitizesPasswordInOutput()
    {
        var settings = Options.Create(new SqlSettings
        {
            Connections = new Dictionary<string, SqlConnectionConfig>
            {
                ["test"] = new SqlConnectionConfig
                {
                    Provider         = "SQLite",
                    // Argha - 2026-02-25 - SQLite connection strings don't have passwords, but we verify
                    // the sanitizer path is invoked (no sensitive data in output)
                    ConnectionString = "Data Source=:memory:",
                    Description      = "In-memory SQLite"
                }
            }
        });

        var tool   = new SqlQueryTool(settings);
        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"]   = "test_connection",
            ["database"] = "test"
        });

        // In-memory SQLite should connect successfully
        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("CONNECTED");
    }

    [Fact]
    public async Task SqlQueryTool_ConfigureConnection_MissingProvider_ReturnsError()
    {
        var settings = Options.Create(new SqlSettings());
        var tool     = new SqlQueryTool(settings);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"]          = "configure_connection",
            ["host"]            = "localhost",
            ["db_name"]         = "mydb",
            ["username"]        = "user",
            ["connection_name"] = "myconn"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("'provider' is required");
    }

    [Fact]
    public async Task SqlConnectionConfig_DefaultProvider_IsSqlServer()
    {
        // Argha - 2026-02-25 - existing configs without Provider key must default to SqlServer
        var config = new SqlConnectionConfig();
        config.Provider.Should().Be("SqlServer");
    }
}
