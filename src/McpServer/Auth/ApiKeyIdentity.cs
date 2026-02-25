// Argha - 2026-02-25 - Phase 7: session identity resolved from a valid API key
namespace McpServer.Auth;

public sealed record ApiKeyIdentity
{
    /// <summary>The raw API key value — never written to logs.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Friendly name shown in audit logs.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Allowed tool names for this key. "*" grants access to all tools.</summary>
    public List<string> AllowedTools { get; init; } = new();

    /// <summary>Per-tool action allowlists. If a tool has no entry, all its actions are permitted.</summary>
    public Dictionary<string, List<string>> AllowedActions { get; init; } = new();
}
