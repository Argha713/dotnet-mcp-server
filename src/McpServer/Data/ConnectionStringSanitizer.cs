// Argha - 2026-02-25 - Phase 6.1: strips passwords from connection strings before they reach any response
using System.Data.Common;

namespace McpServer.Data;

/// <summary>
/// Removes sensitive fields from a connection string so it is safe to include in error messages
/// returned to the AI. The AI never sees passwords even on connection failures.
/// </summary>
public static class ConnectionStringSanitizer
{
    private static readonly string[] SensitiveKeys =
    [
        "password", "pwd", "user id", "uid", "user password", "userpassword"
    ];

    /// <summary>
    /// Returns a copy of the connection string with password-like fields replaced by ***.
    /// If parsing fails for any reason, returns "[connection string redacted]" to ensure
    /// nothing leaks regardless of the driver format.
    /// </summary>
    public static string Sanitize(string connectionString)
    {
        try
        {
            var builder = new DbConnectionStringBuilder
            {
                ConnectionString = connectionString
            };

            foreach (var key in SensitiveKeys)
            {
                if (builder.ContainsKey(key))
                    builder[key] = "***";
            }

            return builder.ConnectionString;
        }
        catch
        {
            // Argha - 2026-02-25 - if we cannot parse the string at all, redact everything
            return "[connection string redacted]";
        }
    }
}
