// Argha - 2026-02-25 - Phase 6.3: no-op rate limiter used when RateLimit:Enabled is false
namespace McpServer.RateLimiting;

/// <summary>
/// Always allows every call through. Used when rate limiting is disabled in configuration
/// and in unit tests that do not care about rate-limit behaviour.
/// </summary>
public sealed class NullRateLimiter : IRateLimiter
{
    /// <summary>Shared singleton — no state, safe to reuse everywhere.</summary>
    public static readonly NullRateLimiter Instance = new();

    // Argha - 2026-02-25 - private ctor enforces singleton usage
    private NullRateLimiter() { }

    /// <inheritdoc />
    public bool TryAcquire(string toolName) => true;
}
