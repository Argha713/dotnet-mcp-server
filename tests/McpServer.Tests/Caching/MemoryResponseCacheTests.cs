// Argha - 2026-02-25 - Phase 6.4: unit tests for MemoryResponseCache
using FluentAssertions;
using McpServer.Caching;
using McpServer.Configuration;
using McpServer.Protocol;
using Microsoft.Extensions.Options;
using Xunit;

namespace McpServer.Tests.Caching;

public class MemoryResponseCacheTests
{
    // Argha - 2026-02-25 - helper: build a cache with a controllable fake clock
    private static (MemoryResponseCache cache, Action<TimeSpan> advance) Build(
        int defaultTtl = 60,
        Dictionary<string, int>? toolTtls = null,
        int maxEntries = 1000)
    {
        var fakeNow = DateTimeOffset.UtcNow;
        Func<DateTimeOffset> clock = () => fakeNow;

        var settings = Options.Create(new CacheSettings
        {
            Enabled = true,
            DefaultTtlSeconds = defaultTtl,
            ToolTtls = toolTtls ?? new Dictionary<string, int>(),
            MaxEntries = maxEntries,
        });

        var cache = new MemoryResponseCache(settings, clock);
        Action<TimeSpan> advance = delta => fakeNow = fakeNow.Add(delta);
        return (cache, advance);
    }

    private static ToolCallResult SuccessResult(string text = "ok") => new()
    {
        Content = new List<ContentBlock> { new() { Type = "text", Text = text } }
    };

    private static ToolCallResult ErrorResult(string text = "err") => new()
    {
        Content = new List<ContentBlock> { new() { Type = "text", Text = text } },
        IsError = true
    };

    // -------------------------------------------------------
    // Basic get/set
    // -------------------------------------------------------

    [Fact]
    public void TryGet_AfterSet_ReturnsHit()
    {
        var (cache, _) = Build();
        var result = SuccessResult("hello");

        cache.Set("sql_query", "key1", result);
        var hit = cache.TryGet("sql_query", "key1", out var retrieved);

        hit.Should().BeTrue();
        retrieved.Should().BeSameAs(result);
    }

    [Fact]
    public void TryGet_BeforeSet_ReturnsMiss()
    {
        var (cache, _) = Build();

        var hit = cache.TryGet("sql_query", "nonexistent", out var result);

        hit.Should().BeFalse();
        result.Should().BeNull();
    }

    [Fact]
    public void TryGet_AfterTtlExpires_ReturnsMiss()
    {
        var (cache, advance) = Build(defaultTtl: 30);
        var result = SuccessResult();

        cache.Set("sql_query", "key1", result);

        // Advance past the TTL
        advance(TimeSpan.FromSeconds(31));

        var hit = cache.TryGet("sql_query", "key1", out var retrieved);

        hit.Should().BeFalse("entry should have expired");
        retrieved.Should().BeNull();
    }

    [Fact]
    public void TryGet_BeforeTtlExpires_ReturnsHit()
    {
        var (cache, advance) = Build(defaultTtl: 60);
        var result = SuccessResult();

        cache.Set("sql_query", "key1", result);

        // Advance to just before expiry
        advance(TimeSpan.FromSeconds(59));

        var hit = cache.TryGet("sql_query", "key1", out var retrieved);

        hit.Should().BeTrue("entry should still be valid");
        retrieved.Should().BeSameAs(result);
    }

    // -------------------------------------------------------
    // TTL 0 = bypass cache
    // -------------------------------------------------------

    [Fact]
    public void Set_WithZeroToolTtl_EntryNotStored()
    {
        var (cache, _) = Build(toolTtls: new Dictionary<string, int> { ["datetime"] = 0 });
        var result = SuccessResult("now");

        cache.Set("datetime", "key1", result);

        var hit = cache.TryGet("datetime", "key1", out _);
        hit.Should().BeFalse("TTL of 0 means bypass; nothing should be stored");
    }

    [Fact]
    public void Set_WithZeroDefaultTtl_EntryNotStored()
    {
        var (cache, _) = Build(defaultTtl: 0);
        var result = SuccessResult();

        cache.Set("sql_query", "key1", result);

        var hit = cache.TryGet("sql_query", "key1", out _);
        hit.Should().BeFalse("default TTL of 0 means bypass for all tools");
    }

    // -------------------------------------------------------
    // Per-tool TTL override
    // -------------------------------------------------------

    [Fact]
    public void Set_PerToolTtl_OverridesDefault()
    {
        // sql_query gets 300s TTL, default is 10s
        var (cache, advance) = Build(
            defaultTtl: 10,
            toolTtls: new Dictionary<string, int> { ["sql_query"] = 300 });
        var result = SuccessResult();

        cache.Set("sql_query", "key1", result);

        // Advance past the default TTL but within the per-tool TTL
        advance(TimeSpan.FromSeconds(15));

        var hit = cache.TryGet("sql_query", "key1", out _);
        hit.Should().BeTrue("per-tool TTL of 300s should still be valid at t=15s");
    }

    // -------------------------------------------------------
    // MaxEntries eviction
    // -------------------------------------------------------

    [Fact]
    public void Set_AtMaxCapacity_EvictsExpiredFirst()
    {
        // Argha - 2026-02-25 - fill cache with 2 entries (TTL 5s), then advance time so they expire,
        // then add a 3rd entry — the expired ones should be evicted, not the oldest-by-insertion
        var (cache, advance) = Build(defaultTtl: 5, maxEntries: 2);

        cache.Set("sql_query", "key_a", SuccessResult("a"));
        cache.Set("sql_query", "key_b", SuccessResult("b"));

        // Expire both entries
        advance(TimeSpan.FromSeconds(6));

        // Adding a 3rd entry should evict the expired ones
        cache.Set("sql_query", "key_c", SuccessResult("c"));

        // Expired entries should be gone
        cache.TryGet("sql_query", "key_a", out _).Should().BeFalse("expired entry should have been evicted");
        cache.TryGet("sql_query", "key_b", out _).Should().BeFalse("expired entry should have been evicted");

        // New entry should be present
        cache.TryGet("sql_query", "key_c", out _).Should().BeTrue("newly inserted entry should be present");
    }

    [Fact]
    public void Set_AtMaxCapacity_NoExpiredEntries_EvictsOldest()
    {
        // Argha - 2026-02-25 - fill cache with 2 non-expired entries, then add a 3rd;
        // the oldest inserted (key_a) should be evicted
        var (cache, advance) = Build(defaultTtl: 3600, maxEntries: 2);

        cache.Set("sql_query", "key_a", SuccessResult("a"));

        // Advance time so key_b has a later insertion timestamp
        advance(TimeSpan.FromSeconds(1));
        cache.Set("sql_query", "key_b", SuccessResult("b"));

        // Add a third entry — key_a should be evicted (oldest)
        cache.Set("sql_query", "key_c", SuccessResult("c"));

        cache.TryGet("sql_query", "key_a", out _).Should().BeFalse("oldest entry should have been evicted");
        cache.TryGet("sql_query", "key_b", out _).Should().BeTrue("newer entry should still be present");
        cache.TryGet("sql_query", "key_c", out _).Should().BeTrue("newly inserted entry should be present");
    }

    // -------------------------------------------------------
    // Error results are not cached
    // Note: MemoryResponseCache caches whatever is passed to Set.
    // The filtering of error results is done in McpServerHandler.
    // This test documents that the cache itself does not filter.
    // -------------------------------------------------------

    [Fact]
    public void Set_ErrorResult_CacheStoresIt()
    {
        // Argha - 2026-02-25 - the cache itself does not know about IsError; McpServerHandler guards this
        var (cache, _) = Build();
        var errResult = ErrorResult("connection failed");

        cache.Set("sql_query", "key_err", errResult);

        // The cache stores it — McpServerHandler is responsible for not calling Set on errors
        var hit = cache.TryGet("sql_query", "key_err", out var retrieved);
        hit.Should().BeTrue("cache itself does not filter errors");
        retrieved.Should().BeSameAs(errResult);
    }

    // -------------------------------------------------------
    // Thread safety
    // -------------------------------------------------------

    [Fact]
    public async Task ConcurrentSetAndGet_NoExceptionsThrown()
    {
        var (cache, _) = Build(defaultTtl: 3600, maxEntries: 100);

        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            var key = $"key_{i % 10}";
            cache.Set("sql_query", key, SuccessResult($"result_{i}"));
            cache.TryGet("sql_query", key, out _);
        }));

        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync("concurrent access must be thread-safe");
    }
}
