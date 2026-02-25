// Argha - 2026-02-25 - Phase 6.4: unit tests for CacheKeyBuilder
using FluentAssertions;
using McpServer.Caching;
using Xunit;

namespace McpServer.Tests.Caching;

public class CacheKeyBuilderTests
{
    [Fact]
    public void Build_SameArgs_SameKey()
    {
        var args = new Dictionary<string, object> { ["connection"] = "MyDB", ["query"] = "SELECT 1" };

        var key1 = CacheKeyBuilder.Build("sql_query", "execute_query", args);
        var key2 = CacheKeyBuilder.Build("sql_query", "execute_query", args);

        key1.Should().Be(key2);
    }

    [Fact]
    public void Build_DifferentArgs_DifferentKey()
    {
        var args1 = new Dictionary<string, object> { ["query"] = "SELECT 1" };
        var args2 = new Dictionary<string, object> { ["query"] = "SELECT 2" };

        var key1 = CacheKeyBuilder.Build("sql_query", "execute_query", args1);
        var key2 = CacheKeyBuilder.Build("sql_query", "execute_query", args2);

        key1.Should().NotBe(key2);
    }

    [Fact]
    public void Build_ArgOrderIrrelevant_SameKey()
    {
        // Argha - 2026-02-25 - keys are sorted before hashing; insertion order must not affect the result
        var args1 = new Dictionary<string, object> { ["connection"] = "DB", ["query"] = "SELECT 1" };
        var args2 = new Dictionary<string, object> { ["query"] = "SELECT 1", ["connection"] = "DB" };

        var key1 = CacheKeyBuilder.Build("sql_query", "execute_query", args1);
        var key2 = CacheKeyBuilder.Build("sql_query", "execute_query", args2);

        key1.Should().Be(key2, "argument order must not affect the cache key");
    }

    [Fact]
    public void Build_MetaKeyExcluded_KeyUnchanged()
    {
        // Argha - 2026-02-25 - _meta is a protocol key; it must not affect the content-based cache key
        var args = new Dictionary<string, object> { ["query"] = "SELECT 1" };
        var argsWithMeta = new Dictionary<string, object> { ["query"] = "SELECT 1", ["_meta"] = "progress_token_xyz" };

        var key1 = CacheKeyBuilder.Build("sql_query", "execute_query", args);
        var key2 = CacheKeyBuilder.Build("sql_query", "execute_query", argsWithMeta);

        key1.Should().Be(key2, "_meta must be excluded from the hash");
    }

    [Fact]
    public void Build_ActionKeyExcluded_ActionAlreadyInPrefix()
    {
        // Argha - 2026-02-25 - "action" is already captured in the key prefix; hashing it again would differ when absent
        var argsWithAction = new Dictionary<string, object> { ["action"] = "execute_query", ["query"] = "SELECT 1" };
        var argsWithoutAction = new Dictionary<string, object> { ["query"] = "SELECT 1" };

        var key1 = CacheKeyBuilder.Build("sql_query", "execute_query", argsWithAction);
        var key2 = CacheKeyBuilder.Build("sql_query", "execute_query", argsWithoutAction);

        key1.Should().Be(key2, "action key must be stripped before hashing");
    }

    [Fact]
    public void Build_DifferentTools_DifferentKey()
    {
        var args = new Dictionary<string, object> { ["query"] = "SELECT 1" };

        var key1 = CacheKeyBuilder.Build("sql_query", "execute_query", args);
        var key2 = CacheKeyBuilder.Build("http_request", "execute_query", args);

        key1.Should().NotBe(key2, "tool name is part of the key");
    }

    [Fact]
    public void Build_DifferentActions_DifferentKey()
    {
        var args = new Dictionary<string, object> { ["query"] = "SELECT 1" };

        var key1 = CacheKeyBuilder.Build("sql_query", "execute_query", args);
        var key2 = CacheKeyBuilder.Build("sql_query", "list_tables", args);

        key1.Should().NotBe(key2, "action is part of the key");
    }

    [Fact]
    public void Build_NullArgs_ReturnsStableKey()
    {
        var key1 = CacheKeyBuilder.Build("datetime", "now", null);
        var key2 = CacheKeyBuilder.Build("datetime", "now", null);

        key1.Should().Be(key2, "null args should produce a stable key");
    }

    [Fact]
    public void Build_NullAction_ReturnsStableKey()
    {
        var args = new Dictionary<string, object> { ["x"] = "1" };

        var key1 = CacheKeyBuilder.Build("text", null, args);
        var key2 = CacheKeyBuilder.Build("text", null, args);

        key1.Should().Be(key2);
    }

    [Fact]
    public void Build_KeyFormat_ContainsToolNameAndAction()
    {
        var key = CacheKeyBuilder.Build("sql_query", "execute_query", null);

        key.Should().StartWith("sql_query:execute_query:");
    }

    [Fact]
    public void Build_ToolName_LowercasedInKey()
    {
        var key = CacheKeyBuilder.Build("SQL_QUERY", "execute_query", null);

        key.Should().StartWith("sql_query:");
    }

    [Fact]
    public void Build_HashSegment_Is16HexChars()
    {
        var key = CacheKeyBuilder.Build("sql_query", "execute_query", null);
        var parts = key.Split(':');

        parts.Should().HaveCount(3, "format is toolName:action:hash");
        parts[2].Should().HaveLength(16, "hash is first 16 hex chars of SHA256");
        parts[2].Should().MatchRegex("^[0-9a-f]{16}$", "hash must be lowercase hex");
    }
}
