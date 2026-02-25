// Argha - 2026-02-25 - Phase 6.2: contract for writing tool-call audit entries
namespace McpServer.Audit;

/// <summary>
/// Persists audit records for every tool call processed by the server.
/// Implementations must never throw — audit failures are non-fatal.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Persists an audit record. Must complete without throwing even on I/O errors.
    /// </summary>
    Task LogCallAsync(AuditRecord record);
}
