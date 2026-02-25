// Argha - 2026-02-25 - Phase 6.4: deterministic cache key builder
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace McpServer.Caching;

/// <summary>
/// Builds a stable, collision-resistant cache key for a tool invocation.
///
/// Key format: "{toolName}:{action}:{sha256Hex[..16]}"
///
/// The hash covers a canonical JSON representation of the arguments after removing
/// the reserved "action" and "_meta" keys. Keys are sorted for stable ordering.
/// </summary>
public static class CacheKeyBuilder
{
    // Argha - 2026-02-25 - keys to strip before hashing; they are already represented in the key prefix
    private static readonly HashSet<string> _excludedKeys =
        new(StringComparer.OrdinalIgnoreCase) { "action", "_meta" };

    /// <summary>
    /// Builds a cache key for the given tool invocation.
    /// </summary>
    public static string Build(string toolName, string? action, Dictionary<string, object>? args)
    {
        var filtered = FilterArgs(args);
        var canonical = CanonicalJson(filtered);
        var hash = HashPrefix(canonical);
        return $"{toolName.ToLowerInvariant()}:{action ?? ""}:{hash}";
    }

    // Argha - 2026-02-25 - remove excluded keys and sort the remainder for a stable representation
    private static Dictionary<string, object> FilterArgs(Dictionary<string, object>? args)
    {
        if (args == null) return new Dictionary<string, object>(StringComparer.Ordinal);

        return args
            .Where(kvp => !_excludedKeys.Contains(kvp.Key))
            .OrderBy(kvp => kvp.Key, StringComparer.Ordinal)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
    }

    // Argha - 2026-02-25 - produce a compact, deterministic JSON string
    private static string CanonicalJson(Dictionary<string, object> dict)
    {
        return JsonSerializer.Serialize(dict, new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = null,
        });
    }

    // Argha - 2026-02-25 - first 16 hex chars (8 bytes) of SHA256 — sufficient for this domain
    private static string HashPrefix(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
