// Argha - 2026-02-25 - Phase 6.2: no-op audit logger used when Audit:Enabled is false
namespace McpServer.Audit;

/// <summary>
/// Discards every audit record. Used when audit logging is disabled in configuration
/// and in unit tests that do not care about audit output.
/// </summary>
public sealed class NullAuditLogger : IAuditLogger
{
    /// <summary>Shared singleton — no state, safe to reuse everywhere.</summary>
    public static readonly NullAuditLogger Instance = new();

    // Argha - 2026-02-25 - private ctor enforces singleton usage pattern
    private NullAuditLogger() { }

    /// <inheritdoc />
    public Task LogCallAsync(AuditRecord record) => Task.CompletedTask;
}
