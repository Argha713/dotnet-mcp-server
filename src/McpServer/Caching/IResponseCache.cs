// Argha - 2026-02-25 - Phase 6.4: response cache abstraction
using McpServer.Protocol;

namespace McpServer.Caching;

/// <summary>
/// Caches tool execution results keyed by (tool name + action + canonical argument hash).
/// Implementations must be thread-safe.
/// </summary>
public interface IResponseCache
{
    /// <summary>
    /// Attempts to retrieve a previously cached result.
    /// Returns true and sets <paramref name="result"/> if a valid (non-expired) entry exists.
    /// </summary>
    bool TryGet(string toolName, string cacheKey, out ToolCallResult? result);

    /// <summary>
    /// Stores a successful tool result under the given key.
    /// If TTL for <paramref name="toolName"/> is 0, the entry is silently dropped.
    /// </summary>
    void Set(string toolName, string cacheKey, ToolCallResult result);
}
