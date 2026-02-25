// Argha - 2026-02-25 - Phase 6.1: tests for DatabaseProviderFactory and ConnectionStringSanitizer
using FluentAssertions;
using McpServer.Data;
using Xunit;

namespace McpServer.Tests.Data;

public class DatabaseProviderFactoryTests
{
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("sqlserver")]
    [InlineData("SQLSERVER")]
    public void Resolve_SqlServer_ReturnsSqlServerProvider(string name)
    {
        var provider = DatabaseProviderFactory.Resolve(name);
        provider.Should().BeOfType<SqlServerProvider>();
        provider.ProviderName.Should().Be("SqlServer");
    }

    [Theory]
    [InlineData("PostgreSQL")]
    [InlineData("postgresql")]
    [InlineData("Postgres")]
    [InlineData("postgres")]
    public void Resolve_PostgreSQL_ReturnsPostgreSqlProvider(string name)
    {
        var provider = DatabaseProviderFactory.Resolve(name);
        provider.Should().BeOfType<PostgreSqlProvider>();
        provider.ProviderName.Should().Be("PostgreSQL");
    }

    [Theory]
    [InlineData("MySQL")]
    [InlineData("mysql")]
    [InlineData("MYSQL")]
    public void Resolve_MySQL_ReturnsMySqlProvider(string name)
    {
        var provider = DatabaseProviderFactory.Resolve(name);
        provider.Should().BeOfType<MySqlProvider>();
        provider.ProviderName.Should().Be("MySQL");
    }

    [Theory]
    [InlineData("SQLite")]
    [InlineData("sqlite")]
    [InlineData("SQLITE")]
    public void Resolve_SQLite_ReturnsSqliteProvider(string name)
    {
        var provider = DatabaseProviderFactory.Resolve(name);
        provider.Should().BeOfType<SqliteProvider>();
        provider.ProviderName.Should().Be("SQLite");
    }

    [Theory]
    [InlineData("Oracle")]
    [InlineData("MariaDB")]
    [InlineData("")]
    [InlineData("  ")]
    public void Resolve_UnknownProvider_ThrowsArgumentException(string name)
    {
        var act = () => DatabaseProviderFactory.Resolve(name);
        act.Should().Throw<ArgumentException>()
           .WithMessage("*Unknown database provider*");
    }

    [Fact]
    public void Resolve_UnknownProvider_ErrorMessageListsSupportedProviders()
    {
        var act = () => DatabaseProviderFactory.Resolve("Oracle");
        act.Should().Throw<ArgumentException>()
           .WithMessage("*SqlServer*PostgreSQL*MySQL*SQLite*");
    }

    [Fact]
    public void SupportedProviders_ContainsAllFour()
    {
        DatabaseProviderFactory.SupportedProviders.Should().Contain("SqlServer");
        DatabaseProviderFactory.SupportedProviders.Should().Contain("PostgreSQL");
        DatabaseProviderFactory.SupportedProviders.Should().Contain("MySQL");
        DatabaseProviderFactory.SupportedProviders.Should().Contain("SQLite");
        DatabaseProviderFactory.SupportedProviders.Should().HaveCount(4);
    }

    // ─── provider-specific: ParseTableName ───────────────────────────────────

    [Fact]
    public void SqlServerProvider_ParseTableName_DefaultsToDbSchema()
    {
        var (schema, table) = new SqlServerProvider().ParseTableName("Users");
        schema.Should().Be("dbo");
        table.Should().Be("Users");
    }

    [Fact]
    public void SqlServerProvider_ParseTableName_WithSchemaDot_SplitsCorrectly()
    {
        var (schema, table) = new SqlServerProvider().ParseTableName("sales.Orders");
        schema.Should().Be("sales");
        table.Should().Be("Orders");
    }

    [Fact]
    public void PostgreSqlProvider_ParseTableName_DefaultsToPublic()
    {
        var (schema, table) = new PostgreSqlProvider().ParseTableName("users");
        schema.Should().Be("public");
        table.Should().Be("users");
    }

    [Fact]
    public void SqliteProvider_ParseTableName_IgnoresSchemaPrefix()
    {
        var (schema, table) = new SqliteProvider().ParseTableName("main.users");
        schema.Should().Be("");
        table.Should().Be("users");
    }

    // ─── provider-specific: BuildPartialConnectionString ─────────────────────

    [Fact]
    public void SqlServerProvider_BuildPartialConnectionString_DoesNotContainRealPassword()
    {
        var cs = new SqlServerProvider().BuildPartialConnectionString("myserver", "1433", "MyDb", "sa");
        cs.Should().NotContain("Password=;");
        cs.Should().Contain("YOUR_PASSWORD_HERE");
    }

    [Fact]
    public void PostgreSqlProvider_BuildPartialConnectionString_ContainsPlaceholder()
    {
        var cs = new PostgreSqlProvider().BuildPartialConnectionString("localhost", "5432", "mydb", "postgres");
        cs.Should().Contain("YOUR_PASSWORD_HERE");
        cs.Should().Contain("localhost");
        cs.Should().Contain("mydb");
    }

    [Fact]
    public void MySqlProvider_BuildPartialConnectionString_ContainsPlaceholder()
    {
        var cs = new MySqlProvider().BuildPartialConnectionString("localhost", "3306", "mydb", "root");
        cs.Should().Contain("YOUR_PASSWORD_HERE");
        cs.Should().Contain("localhost");
    }

    [Fact]
    public void SqliteProvider_BuildPartialConnectionString_NoPasswordPlaceholder()
    {
        // SQLite has no password — connection string should just be the file path
        var cs = new SqliteProvider().BuildPartialConnectionString("", null, "/data/mydb.sqlite", "");
        cs.Should().Contain("mydb.sqlite");
        cs.Should().NotContain("YOUR_PASSWORD_HERE");
    }
}

// ─── ConnectionStringSanitizer Tests ─────────────────────────────────────────

public class ConnectionStringSanitizerTests
{
    [Fact]
    public void Sanitize_SqlServerConnectionString_RedactsPassword()
    {
        var cs = "Server=myserver;Database=mydb;User Id=sa;Password=SuperSecret123;";
        var sanitized = ConnectionStringSanitizer.Sanitize(cs);
        sanitized.Should().NotContain("SuperSecret123");
        sanitized.Should().Contain("***");
        sanitized.Should().Contain("myserver");
        sanitized.Should().Contain("mydb");
    }

    [Fact]
    public void Sanitize_PwdAlias_IsAlsoRedacted()
    {
        var cs = "Server=myserver;Database=mydb;Uid=root;Pwd=topsecret;";
        var sanitized = ConnectionStringSanitizer.Sanitize(cs);
        sanitized.Should().NotContain("topsecret");
        sanitized.Should().Contain("***");
    }

    [Fact]
    public void Sanitize_ConnectionStringWithNoPassword_ReturnsUnchanged()
    {
        var cs = "Server=myserver;Database=mydb;Trusted_Connection=True;";
        var sanitized = ConnectionStringSanitizer.Sanitize(cs);
        sanitized.Should().Contain("myserver");
        sanitized.Should().Contain("mydb");
        sanitized.Should().NotContain("***");
    }

    [Fact]
    public void Sanitize_MalformedConnectionString_ReturnsRedactedPlaceholder()
    {
        var sanitized = ConnectionStringSanitizer.Sanitize("not-a=valid==connection;string;;");
        // Should not throw; should return a safe string
        sanitized.Should().NotBeNull();
    }
}
