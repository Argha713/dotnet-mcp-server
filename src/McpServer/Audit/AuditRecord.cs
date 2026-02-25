// Argha - 2026-02-25 - Phase 6.2: immutable data model for a single tool-call audit entry
namespace McpServer.Audit;

/// <summary>
/// Captures the full context of a single tool call for the audit log.
/// </summary>
public sealed record AuditRecord
{
    /// <summary>UTC timestamp when the tool call completed.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>Random identifier tying this entry to one tools/call request.</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>Name of the tool that was invoked (e.g. "sql_query").</summary>
    public string ToolName { get; init; } = string.Empty;

    /// <summary>Value of the "action" argument, if present (e.g. "execute_query").</summary>
    public string? Action { get; init; }

    /// <summary>
    /// Sanitized copy of the tool arguments. Sensitive keys (password, token, etc.)
    /// are replaced with "[REDACTED]" before this record is written to disk.
    /// </summary>
    public Dictionary<string, object>? Arguments { get; init; }

    /// <summary>"Success", "Failure", or "Timeout".</summary>
    public string Outcome { get; init; } = string.Empty;

    /// <summary>Exception message when Outcome is "Failure"; null otherwise.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Wall-clock duration of the tool execution in milliseconds.</summary>
    public long DurationMs { get; init; }
}
