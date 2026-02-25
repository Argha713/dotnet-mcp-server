// Argha - 2026-02-25 - Phase 6.3: contract for per-tool rate limiting
namespace McpServer.RateLimiting;

/// <summary>
/// Enforces call-rate limits on individual tools.
/// Implementations must be thread-safe.
/// </summary>
public interface IRateLimiter
{
    /// <summary>
    /// Attempts to acquire a call token for <paramref name="toolName"/>.
    /// Returns <c>true</c> if the call is within the configured limit and should proceed.
    /// Returns <c>false</c> if the tool has exceeded its allowed calls per minute.
    /// </summary>
    bool TryAcquire(string toolName);
}
