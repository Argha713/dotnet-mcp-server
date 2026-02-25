// Argha - 2026-02-25 - Phase 6.3: per-tool sliding window rate limiter
using McpServer.Configuration;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace McpServer.RateLimiting;

/// <summary>
/// Enforces a per-tool sliding window rate limit over a 1-minute window.
///
/// Each tool maintains its own call queue. On every <see cref="TryAcquire"/> call:
/// <list type="number">
///   <item>Entries older than 1 minute are evicted from the front of the queue.</item>
///   <item>If the remaining count is at or above the configured limit, the call is rejected.</item>
///   <item>Otherwise the current timestamp is recorded and the call is allowed.</item>
/// </list>
///
/// Thread safety is guaranteed by a dedicated <c>lock</c> object per tool bucket.
/// </summary>
public sealed class SlidingWindowRateLimiter : IRateLimiter
{
    private readonly RateLimitSettings _settings;
    // Argha - 2026-02-25 - injectable clock lets unit tests control time without external packages
    private readonly Func<DateTimeOffset> _now;
    // Argha - 2026-02-25 - one bucket per tool, created lazily on first call
    private readonly ConcurrentDictionary<string, ToolBucket> _buckets = new(StringComparer.OrdinalIgnoreCase);

    public SlidingWindowRateLimiter(IOptions<RateLimitSettings> options)
        : this(options, null) { }

    // Argha - 2026-02-25 - internal ctor used by unit tests to inject a fake clock
    internal SlidingWindowRateLimiter(IOptions<RateLimitSettings> options, Func<DateTimeOffset>? now)
    {
        _settings = options.Value;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <inheritdoc />
    public bool TryAcquire(string toolName)
    {
        var limit = GetLimit(toolName);

        // Argha - 2026-02-25 - limit of 0 means unlimited; skip bucket creation entirely
        if (limit == 0) return true;

        var bucket = _buckets.GetOrAdd(toolName, _ => new ToolBucket());
        return bucket.TryAcquire(limit, _now());
    }

    // Argha - 2026-02-25 - per-tool limit lookup: tool-specific setting takes precedence over the default
    private int GetLimit(string toolName)
    {
        if (_settings.ToolLimits.TryGetValue(toolName, out var toolLimit))
            return toolLimit;

        return _settings.DefaultLimitPerMinute;
    }

    // Argha - 2026-02-25 - one ToolBucket per tool; contains the sliding-window call queue
    private sealed class ToolBucket
    {
        private readonly Queue<DateTimeOffset> _calls = new();
        private readonly object _lock = new();

        public bool TryAcquire(int limit, DateTimeOffset now)
        {
            var windowStart = now.AddMinutes(-1);

            lock (_lock)
            {
                // Evict calls that have fallen outside the 1-minute window
                while (_calls.Count > 0 && _calls.Peek() <= windowStart)
                    _calls.Dequeue();

                if (_calls.Count >= limit)
                    return false;

                _calls.Enqueue(now);
                return true;
            }
        }
    }
}
