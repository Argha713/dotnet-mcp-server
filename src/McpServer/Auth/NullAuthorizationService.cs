// Argha - 2026-02-25 - Phase 7: null-object singleton — always allows all calls (used when auth is disabled)
namespace McpServer.Auth;

public sealed class NullAuthorizationService : IAuthorizationService
{
    private NullAuthorizationService() { }

    public static readonly NullAuthorizationService Instance = new();

    // Argha - 2026-02-25 - anonymous identity: auth disabled, no key to resolve
    public ApiKeyIdentity? ResolveIdentity(string? apiKey) => null;

    public AuthorizationResult AuthorizeToolCall(ApiKeyIdentity? identity, string toolName, string? action)
        => AuthorizationResult.Allow;
}
