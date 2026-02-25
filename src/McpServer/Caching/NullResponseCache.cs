// Argha - 2026-02-25 - Phase 6.4: no-op response cache used when caching is disabled
using McpServer.Protocol;

namespace McpServer.Caching;

/// <summary>
/// No-op implementation of <see cref="IResponseCache"/> used when caching is disabled.
/// TryGet always misses; Set is a no-op.
/// </summary>
public sealed class NullResponseCache : IResponseCache
{
    // Argha - 2026-02-25 - singleton with private ctor; same pattern as NullRateLimiter / NullAuditLogger
    private NullResponseCache() { }

    public static NullResponseCache Instance { get; } = new();

    /// <inheritdoc />
    public bool TryGet(string toolName, string cacheKey, out ToolCallResult? result)
    {
        result = null;
        return false;
    }

    /// <inheritdoc />
    public void Set(string toolName, string cacheKey, ToolCallResult result)
    {
        // Argha - 2026-02-25 - intentionally no-op
    }
}
