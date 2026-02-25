// Argha - 2026-02-25 - Phase 7: real authorization service; validates API keys and enforces per-key permissions
using McpServer.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpServer.Auth;

public sealed class ApiKeyAuthorizationService : IAuthorizationService
{
    private readonly AuthSettings _settings;
    private readonly ILogger<ApiKeyAuthorizationService> _logger;

    // Argha - 2026-02-25 - sentinel returned when key is missing or unrecognized; empty AllowedTools = deny all
    private static readonly ApiKeyIdentity DeniedIdentity = new()
    {
        Key = string.Empty,
        Name = "__denied__",
        AllowedTools = new List<string>(),
        AllowedActions = new Dictionary<string, List<string>>(),
    };

    public ApiKeyAuthorizationService(
        IOptions<AuthSettings> settings,
        ILogger<ApiKeyAuthorizationService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public ApiKeyIdentity? ResolveIdentity(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (!_settings.RequireAuthentication)
                return null; // anonymous session is permitted when RequireAuthentication=false

            // Argha - 2026-02-25 - key required but not supplied; return denied sentinel (never log the key)
            _logger.LogWarning("Auth: no API key provided but RequireAuthentication is true.");
            return DeniedIdentity;
        }

        // Argha - 2026-02-25 - key is case-sensitive (opaque token)
        if (_settings.ApiKeys.TryGetValue(apiKey, out var config))
        {
            return new ApiKeyIdentity
            {
                Key = apiKey,
                Name = config.Name,
                AllowedTools = config.AllowedTools,
                AllowedActions = config.AllowedActions,
            };
        }

        // Argha - 2026-02-25 - unrecognized key; return denied sentinel (never log the raw key value)
        _logger.LogWarning("Auth: unrecognized API key presented.");
        return DeniedIdentity;
    }

    public AuthorizationResult AuthorizeToolCall(ApiKeyIdentity? identity, string toolName, string? action)
    {
        // Argha - 2026-02-25 - null = anonymous session (RequireAuthentication=false) — always allow
        if (identity == null)
            return AuthorizationResult.Allow;

        // Argha - 2026-02-25 - denied sentinel: key was missing or unrecognized during ResolveIdentity
        if (ReferenceEquals(identity, DeniedIdentity))
            return AuthorizationResult.Deny("Authentication required. No valid API key was provided.");

        // Argha - 2026-02-25 - check tool-level permission; tool names are case-insensitive
        if (!IsToolAllowed(identity.AllowedTools, toolName))
            return AuthorizationResult.Deny(
                $"Unauthorized: API key '{identity.Name}' does not have access to tool '{toolName}'.");

        // Argha - 2026-02-25 - check action-level permission when action is present and restrictions exist
        if (action != null && !IsActionAllowed(identity.AllowedActions, toolName, action))
            return AuthorizationResult.Deny(
                $"Unauthorized: API key '{identity.Name}' does not have access to action '{action}' on tool '{toolName}'.");

        return AuthorizationResult.Allow;
    }

    private static bool IsToolAllowed(List<string> allowedTools, string toolName)
    {
        foreach (var entry in allowedTools)
        {
            if (entry == "*")
                return true;
            if (string.Equals(entry, toolName, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsActionAllowed(
        Dictionary<string, List<string>> allowedActions,
        string toolName,
        string action)
    {
        // Argha - 2026-02-25 - case-insensitive lookup of the tool key in the restrictions dictionary
        var toolKey = allowedActions.Keys.FirstOrDefault(k =>
            string.Equals(k, toolName, StringComparison.OrdinalIgnoreCase));

        if (toolKey == null)
            return true; // no action restriction for this tool — all actions permitted

        var actions = allowedActions[toolKey];
        return actions.Any(a => string.Equals(a, action, StringComparison.OrdinalIgnoreCase));
    }
}
