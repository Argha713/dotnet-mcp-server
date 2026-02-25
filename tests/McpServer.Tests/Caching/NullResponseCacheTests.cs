// Argha - 2026-02-25 - Phase 6.4: unit tests for NullResponseCache
using FluentAssertions;
using McpServer.Caching;
using McpServer.Protocol;
using Xunit;

namespace McpServer.Tests.Caching;

public class NullResponseCacheTests
{
    [Fact]
    public void TryGet_AlwaysReturnsFalse()
    {
        var cache = NullResponseCache.Instance;

        var hit = cache.TryGet("sql_query", "any_key", out var result);

        hit.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryGet_RepeatedCalls_AlwaysReturnsFalse()
    {
        var cache = NullResponseCache.Instance;

        for (int i = 0; i < 100; i++)
        {
            cache.TryGet("datetime", $"key_{i}", out var result);
            result.Should().BeNull();
        }
    }

    [Fact]
    public void Set_IsNoOp_DoesNotThrow()
    {
        var cache = NullResponseCache.Instance;
        var result = new ToolCallResult
        {
            Content = new List<ContentBlock> { new() { Type = "text", Text = "ok" } }
        };

        var act = () => cache.Set("sql_query", "some_key", result);

        act.Should().NotThrow();
    }

    [Fact]
    public void Set_ThenTryGet_StillMisses()
    {
        var cache = NullResponseCache.Instance;
        var storedResult = new ToolCallResult
        {
            Content = new List<ContentBlock> { new() { Type = "text", Text = "stored" } }
        };

        cache.Set("http_request", "key123", storedResult);
        var hit = cache.TryGet("http_request", "key123", out var retrieved);

        hit.Should().BeFalse("NullResponseCache never stores anything");
        retrieved.Should().BeNull();
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        NullResponseCache.Instance.Should().BeSameAs(NullResponseCache.Instance);
    }
}
