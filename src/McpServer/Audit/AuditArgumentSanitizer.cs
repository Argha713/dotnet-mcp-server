// Argha - 2026-02-25 - Phase 6.2: strips sensitive values from tool arguments before they are written to the audit file
namespace McpServer.Audit;

/// <summary>
/// Produces a sanitized copy of a tool-arguments dictionary, replacing values whose
/// keys look like credentials with "[REDACTED]". This ensures passwords, tokens, and
/// API keys never appear in the audit log on disk.
/// </summary>
public static class AuditArgumentSanitizer
{
    // Argha - 2026-02-25 - case-insensitive set of key names considered sensitive
    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "password",
        "pwd",
        "pass",
        "secret",
        "token",
        "key",
        "apikey",
        "api_key",
        "connectionstring",
        "connection_string",
        "authorization",
        "auth",
    };

    /// <summary>
    /// Returns a new dictionary where values for sensitive keys are replaced with
    /// "[REDACTED]". Non-sensitive values are copied as-is.
    /// Returns <c>null</c> when <paramref name="arguments"/> is <c>null</c>.
    /// </summary>
    public static Dictionary<string, object>? Sanitize(Dictionary<string, object>? arguments)
    {
        if (arguments == null) return null;

        var result = new Dictionary<string, object>(arguments.Count, StringComparer.Ordinal);
        foreach (var kvp in arguments)
        {
            result[kvp.Key] = SensitiveKeys.Contains(kvp.Key)
                ? "[REDACTED]"
                : kvp.Value;
        }
        return result;
    }
}
