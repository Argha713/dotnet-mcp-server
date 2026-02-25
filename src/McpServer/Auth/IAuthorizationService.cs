// Argha - 2026-02-25 - Phase 7: contract for API key resolution and per-tool authorization
namespace McpServer.Auth;

public interface IAuthorizationService
{
    /// <summary>
    /// Resolves the session identity from the provided API key.
    /// Returns null for anonymous (unauthenticated) sessions when RequireAuthentication is false.
    /// Returns a DeniedIdentity sentinel when the key is missing or unrecognized.
    /// </summary>
    ApiKeyIdentity? ResolveIdentity(string? apiKey);

    /// <summary>
    /// Checks whether the given identity may invoke the specified tool and action.
    /// </summary>
    AuthorizationResult AuthorizeToolCall(ApiKeyIdentity? identity, string toolName, string? action);
}
