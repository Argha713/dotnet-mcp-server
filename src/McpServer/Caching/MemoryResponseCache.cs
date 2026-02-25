// Argha - 2026-02-25 - Phase 6.4: in-memory response cache with per-tool TTL and bounded capacity
using McpServer.Configuration;
using McpServer.Protocol;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace McpServer.Caching;

/// <summary>
/// In-memory <see cref="IResponseCache"/> backed by a <see cref="ConcurrentDictionary{TKey,TValue}"/>.
///
/// Eviction strategy (on <see cref="Set"/>):
/// <list type="number">
///   <item>If the cache is at capacity, expired entries are removed first.</item>
///   <item>If still at capacity after removing expired entries, the oldest inserted entry is removed.</item>
///   <item>The new entry is then stored.</item>
/// </list>
///
/// Expiry is checked lazily on <see cref="TryGet"/>; stale entries are removed on each write.
/// </summary>
public sealed class MemoryResponseCache : IResponseCache
{
    private readonly CacheSettings _settings;
    // Argha - 2026-02-25 - injectable clock allows unit tests to control time without external packages
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    // Argha - 2026-02-25 - lock used only during eviction to guarantee atomicity of the capacity check + insert
    private readonly object _evictionLock = new();

    public MemoryResponseCache(IOptions<CacheSettings> options)
        : this(options, null) { }

    // Argha - 2026-02-25 - internal ctor used by unit tests to inject a controllable fake clock
    internal MemoryResponseCache(IOptions<CacheSettings> options, Func<DateTimeOffset>? now)
    {
        _settings = options.Value;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public bool TryGet(string toolName, string cacheKey, out ToolCallResult? result)
    {
        if (_cache.TryGetValue(cacheKey, out var entry) && entry.ExpiresAt > _now())
        {
            result = entry.Result;
            return true;
        }

        result = null;
        return false;
    }

    /// <inheritdoc />
    public void Set(string toolName, string cacheKey, ToolCallResult result)
    {
        var ttl = ResolveTtl(toolName);

        // Argha - 2026-02-25 - TTL of 0 means bypass cache for this tool
        if (ttl == 0) return;

        var now = _now();
        var entry = new CacheEntry(result, now.AddSeconds(ttl), now);

        lock (_evictionLock)
        {
            if (_cache.Count >= _settings.MaxEntries)
                Evict();

            _cache[cacheKey] = entry;
        }
    }

    // Argha - 2026-02-25 - resolve per-tool TTL; fall back to default if not configured
    private int ResolveTtl(string toolName)
    {
        if (_settings.ToolTtls.TryGetValue(toolName, out var ttl))
            return ttl;

        return _settings.DefaultTtlSeconds;
    }

    // Argha - 2026-02-25 - evict expired entries first; if still at capacity, remove the oldest inserted entry
    private void Evict()
    {
        var now = _now();

        // Pass 1: remove expired entries
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.ExpiresAt <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
            _cache.TryRemove(key, out _);

        // Pass 2: if still at capacity, remove the oldest inserted entry
        if (_cache.Count >= _settings.MaxEntries)
        {
            var oldestKey = _cache
                .OrderBy(kvp => kvp.Value.InsertedAt)
                .Select(kvp => kvp.Key)
                .FirstOrDefault();

            if (oldestKey != null)
                _cache.TryRemove(oldestKey, out _);
        }
    }

    // Argha - 2026-02-25 - immutable cache entry; thread-safe by construction
    private sealed record CacheEntry(ToolCallResult Result, DateTimeOffset ExpiresAt, DateTimeOffset InsertedAt);
}
