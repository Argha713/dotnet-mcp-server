// Argha - 2026-02-25 - Phase 7: immutable result record for authorization checks
namespace McpServer.Auth;

public sealed record AuthorizationResult
{
    public bool IsAuthorized { get; init; }
    public string? DenialReason { get; init; }

    public static readonly AuthorizationResult Allow = new() { IsAuthorized = true };

    public static AuthorizationResult Deny(string reason) =>
        new() { IsAuthorized = false, DenialReason = reason };
}
