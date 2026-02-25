// Argha - 2026-02-25 - Phase 7: unit and integration tests for the authentication and authorization subsystem
using FluentAssertions;
using McpServer.Audit;
using McpServer.Auth;
using McpServer.Caching;
using McpServer.Configuration;
using McpServer.Logging;
using McpServer.Protocol;
using McpServer.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using Xunit;

namespace McpServer.Tests.Auth;

// ============================================================
// NullAuthorizationService — unit tests
// ============================================================
public class NullAuthorizationServiceTests
{
    [Fact]
    public void ResolveIdentity_AlwaysReturnsNull()
    {
        var svc = NullAuthorizationService.Instance;

        svc.ResolveIdentity("any-key").Should().BeNull();
        svc.ResolveIdentity(null).Should().BeNull();
        svc.ResolveIdentity(string.Empty).Should().BeNull();
    }

    [Fact]
    public void AuthorizeToolCall_WithNullIdentity_ReturnsAllow()
    {
        var svc = NullAuthorizationService.Instance;

        var result = svc.AuthorizeToolCall(null, "sql_query", "execute_query");

        result.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeToolCall_WithAnyToolName_ReturnsAllow()
    {
        var svc = NullAuthorizationService.Instance;
        var identity = new ApiKeyIdentity { Key = "k", Name = "test", AllowedTools = new() };

        svc.AuthorizeToolCall(identity, "filesystem", "read_file").IsAuthorized.Should().BeTrue();
        svc.AuthorizeToolCall(identity, "http_request", "get").IsAuthorized.Should().BeTrue();
        svc.AuthorizeToolCall(identity, "datetime", "now").IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeToolCall_ReturnsAllow_WithNoDenialReason()
    {
        var svc = NullAuthorizationService.Instance;

        var result = svc.AuthorizeToolCall(null, "any_tool", null);

        result.IsAuthorized.Should().BeTrue();
        result.DenialReason.Should().BeNull();
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        NullAuthorizationService.Instance.Should().BeSameAs(NullAuthorizationService.Instance);
    }
}

// ============================================================
// ApiKeyAuthorizationService — unit tests
// ============================================================
public class ApiKeyAuthorizationServiceTests
{
    private static ApiKeyAuthorizationService Build(AuthSettings settings) =>
        new(Options.Create(settings), NullLogger<ApiKeyAuthorizationService>.Instance);

    private static AuthSettings SettingsWithKey(
        string key = "secret-key",
        string name = "TestClient",
        List<string>? allowedTools = null,
        Dictionary<string, List<string>>? allowedActions = null) =>
        new()
        {
            Enabled = true,
            RequireAuthentication = true,
            ApiKeys = new Dictionary<string, ApiKeyConfig>
            {
                [key] = new ApiKeyConfig
                {
                    Name = name,
                    AllowedTools = allowedTools ?? new List<string> { "*" },
                    AllowedActions = allowedActions ?? new Dictionary<string, List<string>>(),
                }
            }
        };

    // -------------------------------------------------------
    // ResolveIdentity
    // -------------------------------------------------------

    [Fact]
    public void ResolveIdentity_ValidKey_ReturnsIdentityWithName()
    {
        var svc = Build(SettingsWithKey(key: "my-key", name: "Claude Desktop"));

        var identity = svc.ResolveIdentity("my-key");

        identity.Should().NotBeNull();
        identity!.Name.Should().Be("Claude Desktop");
    }

    [Fact]
    public void ResolveIdentity_ValidKey_ReturnsAllowedTools()
    {
        var svc = Build(SettingsWithKey(key: "my-key", allowedTools: new List<string> { "sql_query", "filesystem" }));

        var identity = svc.ResolveIdentity("my-key");

        identity.Should().NotBeNull();
        identity!.AllowedTools.Should().BeEquivalentTo(new[] { "sql_query", "filesystem" });
    }

    [Fact]
    public void ResolveIdentity_InvalidKey_ToolCallReturnsDeny()
    {
        // Argha - 2026-02-25 - invalid key produces a denied sentinel; verify via AuthorizeToolCall
        var svc = Build(SettingsWithKey(key: "valid-key"));

        var identity = svc.ResolveIdentity("wrong-key");
        var result = svc.AuthorizeToolCall(identity, "sql_query", null);

        result.IsAuthorized.Should().BeFalse();
        result.DenialReason.Should().Contain("Authentication required");
    }

    [Fact]
    public void ResolveIdentity_NullKey_RequireAuth_ToolCallReturnsDeny()
    {
        var settings = SettingsWithKey();
        settings.RequireAuthentication = true;
        var svc = Build(settings);

        var identity = svc.ResolveIdentity(null);
        var result = svc.AuthorizeToolCall(identity, "sql_query", null);

        result.IsAuthorized.Should().BeFalse();
        result.DenialReason.Should().Contain("Authentication required");
    }

    [Fact]
    public void ResolveIdentity_NullKey_NoRequireAuth_ReturnsNull()
    {
        var settings = SettingsWithKey();
        settings.RequireAuthentication = false;
        var svc = Build(settings);

        var identity = svc.ResolveIdentity(null);

        identity.Should().BeNull("anonymous session allowed when RequireAuthentication=false");
    }

    // -------------------------------------------------------
    // AuthorizeToolCall — identity scenarios
    // -------------------------------------------------------

    [Fact]
    public void AuthorizeToolCall_NullIdentity_ReturnsAllow()
    {
        // Argha - 2026-02-25 - null = anonymous session (RequireAuthentication=false)
        var svc = Build(SettingsWithKey());

        var result = svc.AuthorizeToolCall(null, "sql_query", "execute_query");

        result.IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeToolCall_DeniedSentinel_ReturnsDeny()
    {
        var settings = SettingsWithKey();
        settings.RequireAuthentication = true;
        var svc = Build(settings);

        // ResolveIdentity with null key + RequireAuthentication=true returns denied sentinel
        var identity = svc.ResolveIdentity(null);
        var result = svc.AuthorizeToolCall(identity, "filesystem", "read_file");

        result.IsAuthorized.Should().BeFalse();
    }

    // -------------------------------------------------------
    // AuthorizeToolCall — tool allowlist
    // -------------------------------------------------------

    [Fact]
    public void AuthorizeToolCall_WildcardAllowedTools_ReturnsAllow()
    {
        var svc = Build(SettingsWithKey(allowedTools: new List<string> { "*" }));
        var identity = svc.ResolveIdentity("secret-key");

        svc.AuthorizeToolCall(identity, "sql_query", null).IsAuthorized.Should().BeTrue();
        svc.AuthorizeToolCall(identity, "filesystem", null).IsAuthorized.Should().BeTrue();
        svc.AuthorizeToolCall(identity, "http_request", null).IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeToolCall_AllowedTool_ReturnsAllow()
    {
        var svc = Build(SettingsWithKey(allowedTools: new List<string> { "datetime", "sql_query" }));
        var identity = svc.ResolveIdentity("secret-key");

        svc.AuthorizeToolCall(identity, "datetime", null).IsAuthorized.Should().BeTrue();
        svc.AuthorizeToolCall(identity, "sql_query", null).IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeToolCall_DisallowedTool_ReturnsDeny()
    {
        var svc = Build(SettingsWithKey(allowedTools: new List<string> { "datetime" }));
        var identity = svc.ResolveIdentity("secret-key");

        var result = svc.AuthorizeToolCall(identity, "filesystem", null);

        result.IsAuthorized.Should().BeFalse();
    }

    [Fact]
    public void AuthorizeToolCall_DenyResult_ContainsToolName()
    {
        var svc = Build(SettingsWithKey(allowedTools: new List<string> { "datetime" }));
        var identity = svc.ResolveIdentity("secret-key");

        var result = svc.AuthorizeToolCall(identity, "sql_query", null);

        result.DenialReason.Should().Contain("sql_query");
    }

    [Fact]
    public void AuthorizeToolCall_DenyResult_ContainsIdentityName()
    {
        var svc = Build(SettingsWithKey(name: "ReadonlyClient", allowedTools: new List<string> { "datetime" }));
        var identity = svc.ResolveIdentity("secret-key");

        var result = svc.AuthorizeToolCall(identity, "filesystem", null);

        result.DenialReason.Should().Contain("ReadonlyClient");
    }

    [Fact]
    public void AuthorizeToolCall_CaseInsensitiveToolName_Matches()
    {
        var svc = Build(SettingsWithKey(allowedTools: new List<string> { "SQL_QUERY" }));
        var identity = svc.ResolveIdentity("secret-key");

        // Tool name in different case should still match
        svc.AuthorizeToolCall(identity, "sql_query", null).IsAuthorized.Should().BeTrue();
        svc.AuthorizeToolCall(identity, "Sql_Query", null).IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeToolCall_EmptyAllowedTools_DeniesAllTools()
    {
        var svc = Build(SettingsWithKey(allowedTools: new List<string>()));
        var identity = svc.ResolveIdentity("secret-key");

        svc.AuthorizeToolCall(identity, "datetime", null).IsAuthorized.Should().BeFalse();
        svc.AuthorizeToolCall(identity, "sql_query", null).IsAuthorized.Should().BeFalse();
    }

    // -------------------------------------------------------
    // AuthorizeToolCall — action allowlist
    // -------------------------------------------------------

    [Fact]
    public void AuthorizeToolCall_AllowedAction_ReturnsAllow()
    {
        var svc = Build(SettingsWithKey(
            allowedTools: new List<string> { "sql_query" },
            allowedActions: new Dictionary<string, List<string>>
            {
                ["sql_query"] = new List<string> { "execute_query", "list_tables" }
            }));
        var identity = svc.ResolveIdentity("secret-key");

        svc.AuthorizeToolCall(identity, "sql_query", "execute_query").IsAuthorized.Should().BeTrue();
        svc.AuthorizeToolCall(identity, "sql_query", "list_tables").IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeToolCall_DisallowedAction_ReturnsDeny()
    {
        var svc = Build(SettingsWithKey(
            allowedTools: new List<string> { "sql_query" },
            allowedActions: new Dictionary<string, List<string>>
            {
                ["sql_query"] = new List<string> { "list_tables" }
            }));
        var identity = svc.ResolveIdentity("secret-key");

        var result = svc.AuthorizeToolCall(identity, "sql_query", "execute_query");

        result.IsAuthorized.Should().BeFalse();
        result.DenialReason.Should().Contain("execute_query");
        result.DenialReason.Should().Contain("sql_query");
    }

    [Fact]
    public void AuthorizeToolCall_NoActionRestrictionForTool_AllActionsAllowed()
    {
        // Argha - 2026-02-25 - no entry for the tool in AllowedActions = unrestricted
        var svc = Build(SettingsWithKey(
            allowedTools: new List<string> { "filesystem" },
            allowedActions: new Dictionary<string, List<string>>())); // empty — no restrictions
        var identity = svc.ResolveIdentity("secret-key");

        svc.AuthorizeToolCall(identity, "filesystem", "read_file").IsAuthorized.Should().BeTrue();
        svc.AuthorizeToolCall(identity, "filesystem", "list_directory").IsAuthorized.Should().BeTrue();
        svc.AuthorizeToolCall(identity, "filesystem", "write_file").IsAuthorized.Should().BeTrue();
    }

    [Fact]
    public void AuthorizeToolCall_CaseInsensitiveAction_Matches()
    {
        var svc = Build(SettingsWithKey(
            allowedTools: new List<string> { "sql_query" },
            allowedActions: new Dictionary<string, List<string>>
            {
                ["sql_query"] = new List<string> { "EXECUTE_QUERY" }
            }));
        var identity = svc.ResolveIdentity("secret-key");

        // Action name in different case should still match
        svc.AuthorizeToolCall(identity, "sql_query", "execute_query").IsAuthorized.Should().BeTrue();
        svc.AuthorizeToolCall(identity, "sql_query", "Execute_Query").IsAuthorized.Should().BeTrue();
    }
}

// ============================================================
// McpServerHandler — auth wiring integration tests
// ============================================================
public class McpServerHandlerAuthTests
{
    // Argha - 2026-02-25 - fake tool that always succeeds, used to verify auth gating
    private class SuccessTool : McpServer.Tools.ITool
    {
        public string Name => "success_tool";
        public string Description => "Always succeeds";
        public McpServer.Protocol.JsonSchema InputSchema => new();

        public Task<McpServer.Protocol.ToolCallResult> ExecuteAsync(
            Dictionary<string, object>? arguments,
            McpServer.Progress.IProgressReporter? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new McpServer.Protocol.ToolCallResult
            {
                Content = new List<McpServer.Protocol.ContentBlock>
                {
                    new() { Type = "text", Text = "ok" }
                }
            });
        }
    }

    private static McpServerHandler BuildHandler(IAuthorizationService? authService = null)
    {
        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });
        return new McpServerHandler(
            tools: new McpServer.Tools.ITool[] { new SuccessTool() },
            resourceProviders: Array.Empty<McpServer.Resources.IResourceProvider>(),
            promptProviders: Array.Empty<McpServer.Prompts.IPromptProvider>(),
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            logSink: new McpLogSink(),
            auditLogger: NullAuditLogger.Instance,
            rateLimiter: NullRateLimiter.Instance,
            responseCache: NullResponseCache.Instance,
            // Argha - 2026-02-25 - inject auth service under test; default to null-object for unrelated tests
            authorizationService: authService ?? NullAuthorizationService.Instance);
    }

    private static async Task InitializeAsync(McpServerHandler handler)
    {
        var msg = "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\"," +
                  "\"params\":{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}";
        await handler.ProcessMessageAsync(msg, CancellationToken.None);
    }

    private static string ToolCallMsg(int id = 1, string action = "run") =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"tools/call\"," +
        $"\"params\":{{\"name\":\"success_tool\",\"arguments\":{{\"action\":\"{action}\"}}}}}}";

    // -------------------------------------------------------
    // Auth disabled — NullAuthorizationService passes everything
    // -------------------------------------------------------

    [Fact]
    public async Task AuthDisabled_NullService_AnyToolCallSucceeds()
    {
        var handler = BuildHandler(NullAuthorizationService.Instance);
        await InitializeAsync(handler);

        var response = await handler.ProcessMessageAsync(ToolCallMsg(1), CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().BeNull();
        var result = response.Result as ToolCallResult;
        result?.IsError.Should().NotBe(true);
    }

    // -------------------------------------------------------
    // Auth enabled — valid key in env var
    // -------------------------------------------------------

    [Fact]
    public async Task AuthEnabled_ValidKey_InEnvVar_ToolCallSucceeds()
    {
        const string testKey = "test-valid-key-auth-handler";
        var settings = new AuthSettings
        {
            Enabled = true,
            RequireAuthentication = true,
            ApiKeys = new Dictionary<string, ApiKeyConfig>
            {
                [testKey] = new ApiKeyConfig
                {
                    Name = "TestClient",
                    AllowedTools = new List<string> { "*" },
                    AllowedActions = new Dictionary<string, List<string>>(),
                }
            }
        };
        var authSvc = new ApiKeyAuthorizationService(
            Options.Create(settings),
            NullLogger<ApiKeyAuthorizationService>.Instance);

        var prevKey = Environment.GetEnvironmentVariable("MCP_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("MCP_API_KEY", testKey);
            var handler = BuildHandler(authSvc);
            await InitializeAsync(handler);

            var response = await handler.ProcessMessageAsync(ToolCallMsg(1), CancellationToken.None);

            response.Should().NotBeNull();
            response!.Error.Should().BeNull();
            var result = JsonSerializer.Deserialize<ToolCallResult>(
                JsonSerializer.Serialize(response.Result),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            result?.IsError.Should().NotBe(true);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_API_KEY", prevKey);
        }
    }

    // -------------------------------------------------------
    // Auth enabled — invalid key
    // -------------------------------------------------------

    [Fact]
    public async Task AuthEnabled_InvalidKey_ToolCallReturnsIsError()
    {
        const string wrongKey = "wrong-key-xyz";
        var settings = new AuthSettings
        {
            Enabled = true,
            RequireAuthentication = true,
            ApiKeys = new Dictionary<string, ApiKeyConfig>
            {
                ["correct-key"] = new ApiKeyConfig { Name = "Good", AllowedTools = new List<string> { "*" } }
            }
        };
        var authSvc = new ApiKeyAuthorizationService(
            Options.Create(settings),
            NullLogger<ApiKeyAuthorizationService>.Instance);

        var prevKey = Environment.GetEnvironmentVariable("MCP_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("MCP_API_KEY", wrongKey);
            var handler = BuildHandler(authSvc);
            await InitializeAsync(handler);

            var response = await handler.ProcessMessageAsync(ToolCallMsg(2), CancellationToken.None);

            response.Should().NotBeNull();
            var result = JsonSerializer.Deserialize<ToolCallResult>(
                JsonSerializer.Serialize(response.Result),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            result?.IsError.Should().BeTrue("unrecognized key must yield IsError=true");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_API_KEY", prevKey);
        }
    }

    // -------------------------------------------------------
    // Auth enabled — no key, RequireAuthentication=true
    // -------------------------------------------------------

    [Fact]
    public async Task AuthEnabled_NoKey_RequireAuth_ToolCallReturnsIsError()
    {
        var settings = new AuthSettings
        {
            Enabled = true,
            RequireAuthentication = true,
            ApiKeys = new Dictionary<string, ApiKeyConfig>
            {
                ["some-key"] = new ApiKeyConfig { Name = "SomeClient", AllowedTools = new List<string> { "*" } }
            }
        };
        var authSvc = new ApiKeyAuthorizationService(
            Options.Create(settings),
            NullLogger<ApiKeyAuthorizationService>.Instance);

        var prevKey = Environment.GetEnvironmentVariable("MCP_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("MCP_API_KEY", null);
            var handler = BuildHandler(authSvc);
            await InitializeAsync(handler);

            var response = await handler.ProcessMessageAsync(ToolCallMsg(3), CancellationToken.None);

            var result = JsonSerializer.Deserialize<ToolCallResult>(
                JsonSerializer.Serialize(response!.Result),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            result?.IsError.Should().BeTrue("missing key must yield IsError=true when RequireAuthentication=true");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MCP_API_KEY", prevKey);
        }
    }

    // -------------------------------------------------------
    // Auth enabled — valid key but tool not in allowlist
    // -------------------------------------------------------

    [Fact]
    public async Task AuthEnabled_ValidKey_DisallowedTool_ReturnsIsError()
    {
        // Argha - 2026-02-25 - mock returns Allow on ResolveIdentity but Deny on AuthorizeToolCall
        var mockAuth = new Mock<IAuthorizationService>();
        var identity = new ApiKeyIdentity { Key = "k", Name = "Limited", AllowedTools = new List<string> { "datetime" } };
        mockAuth.Setup(a => a.ResolveIdentity(It.IsAny<string?>())).Returns(identity);
        mockAuth.Setup(a => a.AuthorizeToolCall(identity, "success_tool", It.IsAny<string?>()))
                .Returns(AuthorizationResult.Deny("Unauthorized: API key 'Limited' does not have access to tool 'success_tool'."));

        var handler = BuildHandler(mockAuth.Object);
        await InitializeAsync(handler);

        var response = await handler.ProcessMessageAsync(ToolCallMsg(4), CancellationToken.None);

        var result = JsonSerializer.Deserialize<ToolCallResult>(
            JsonSerializer.Serialize(response!.Result),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        result?.IsError.Should().BeTrue();
    }

    // -------------------------------------------------------
    // Auth enabled — valid key, tool allowed, action not allowed
    // -------------------------------------------------------

    [Fact]
    public async Task AuthEnabled_ValidKey_DisallowedAction_ReturnsIsError()
    {
        var mockAuth = new Mock<IAuthorizationService>();
        var identity = new ApiKeyIdentity { Key = "k", Name = "Restricted", AllowedTools = new List<string> { "success_tool" } };
        mockAuth.Setup(a => a.ResolveIdentity(It.IsAny<string?>())).Returns(identity);
        mockAuth.Setup(a => a.AuthorizeToolCall(identity, "success_tool", "run"))
                .Returns(AuthorizationResult.Deny("Unauthorized: API key 'Restricted' does not have access to action 'run' on tool 'success_tool'."));

        var handler = BuildHandler(mockAuth.Object);
        await InitializeAsync(handler);

        var response = await handler.ProcessMessageAsync(ToolCallMsg(5, action: "run"), CancellationToken.None);

        var result = JsonSerializer.Deserialize<ToolCallResult>(
            JsonSerializer.Serialize(response!.Result),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        result?.IsError.Should().BeTrue();
    }

    // -------------------------------------------------------
    // Auth enabled — valid key, tool and action both allowed
    // -------------------------------------------------------

    [Fact]
    public async Task AuthEnabled_ValidKey_AllowedToolAndAction_Succeeds()
    {
        var mockAuth = new Mock<IAuthorizationService>();
        var identity = new ApiKeyIdentity { Key = "k", Name = "Full", AllowedTools = new List<string> { "*" } };
        mockAuth.Setup(a => a.ResolveIdentity(It.IsAny<string?>())).Returns(identity);
        mockAuth.Setup(a => a.AuthorizeToolCall(identity, It.IsAny<string>(), It.IsAny<string?>()))
                .Returns(AuthorizationResult.Allow);

        var handler = BuildHandler(mockAuth.Object);
        await InitializeAsync(handler);

        var response = await handler.ProcessMessageAsync(ToolCallMsg(6), CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().BeNull();
        var result = JsonSerializer.Deserialize<ToolCallResult>(
            JsonSerializer.Serialize(response.Result),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        result?.IsError.Should().NotBe(true);
    }

    // -------------------------------------------------------
    // Auth check happens BEFORE rate limit — denied calls don't consume tokens
    // -------------------------------------------------------

    [Fact]
    public async Task AuthCheck_BeforeRateLimit_RateLimitNotConsumedOnDeny()
    {
        var mockAuth = new Mock<IAuthorizationService>();
        mockAuth.Setup(a => a.ResolveIdentity(It.IsAny<string?>())).Returns((ApiKeyIdentity?)null);
        mockAuth.Setup(a => a.AuthorizeToolCall(It.IsAny<ApiKeyIdentity?>(), It.IsAny<string>(), It.IsAny<string?>()))
                .Returns(AuthorizationResult.Deny("Authentication required. No valid API key was provided."));

        var mockRateLimiter = new Mock<IRateLimiter>();

        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });
        var handler = new McpServerHandler(
            tools: new McpServer.Tools.ITool[] { new SuccessTool() },
            resourceProviders: Array.Empty<McpServer.Resources.IResourceProvider>(),
            promptProviders: Array.Empty<McpServer.Prompts.IPromptProvider>(),
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            logSink: new McpLogSink(),
            auditLogger: NullAuditLogger.Instance,
            rateLimiter: mockRateLimiter.Object,
            responseCache: NullResponseCache.Instance,
            authorizationService: mockAuth.Object);

        await InitializeAsync(handler);
        await handler.ProcessMessageAsync(ToolCallMsg(7), CancellationToken.None);

        // TryAcquire must NOT be called — auth check short-circuits before rate limiting
        mockRateLimiter.Verify(l => l.TryAcquire(It.IsAny<string>()), Times.Never,
            "unauthorized calls must not consume rate-limit tokens");
    }

    // -------------------------------------------------------
    // Unauthorized calls are audited
    // -------------------------------------------------------

    [Fact]
    public async Task UnauthorizedCall_AuditedWithUnauthorizedOutcome()
    {
        var mockAuth = new Mock<IAuthorizationService>();
        mockAuth.Setup(a => a.ResolveIdentity(It.IsAny<string?>())).Returns((ApiKeyIdentity?)null);
        mockAuth.Setup(a => a.AuthorizeToolCall(It.IsAny<ApiKeyIdentity?>(), It.IsAny<string>(), It.IsAny<string?>()))
                .Returns(AuthorizationResult.Deny("Authentication required. No valid API key was provided."));

        AuditRecord? captured = null;
        var mockAudit = new Mock<IAuditLogger>();
        mockAudit.Setup(a => a.LogCallAsync(It.IsAny<AuditRecord>()))
                 .Callback<AuditRecord>(r => captured = r)
                 .Returns(Task.CompletedTask);

        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });
        var handler = new McpServerHandler(
            tools: new McpServer.Tools.ITool[] { new SuccessTool() },
            resourceProviders: Array.Empty<McpServer.Resources.IResourceProvider>(),
            promptProviders: Array.Empty<McpServer.Prompts.IPromptProvider>(),
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            logSink: new McpLogSink(),
            auditLogger: mockAudit.Object,
            rateLimiter: NullRateLimiter.Instance,
            responseCache: NullResponseCache.Instance,
            authorizationService: mockAuth.Object);

        await InitializeAsync(handler);
        await handler.ProcessMessageAsync(ToolCallMsg(8), CancellationToken.None);

        captured.Should().NotBeNull("unauthorized calls must be recorded in the audit log");
        captured!.Outcome.Should().Be("Unauthorized");
    }

    [Fact]
    public async Task UnauthorizedCall_AuditRecordContainsToolName()
    {
        var mockAuth = new Mock<IAuthorizationService>();
        mockAuth.Setup(a => a.ResolveIdentity(It.IsAny<string?>())).Returns((ApiKeyIdentity?)null);
        mockAuth.Setup(a => a.AuthorizeToolCall(It.IsAny<ApiKeyIdentity?>(), It.IsAny<string>(), It.IsAny<string?>()))
                .Returns(AuthorizationResult.Deny("Authentication required. No valid API key was provided."));

        AuditRecord? captured = null;
        var mockAudit = new Mock<IAuditLogger>();
        mockAudit.Setup(a => a.LogCallAsync(It.IsAny<AuditRecord>()))
                 .Callback<AuditRecord>(r => captured = r)
                 .Returns(Task.CompletedTask);

        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });
        var handler = new McpServerHandler(
            tools: new McpServer.Tools.ITool[] { new SuccessTool() },
            resourceProviders: Array.Empty<McpServer.Resources.IResourceProvider>(),
            promptProviders: Array.Empty<McpServer.Prompts.IPromptProvider>(),
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            logSink: new McpLogSink(),
            auditLogger: mockAudit.Object,
            rateLimiter: NullRateLimiter.Instance,
            responseCache: NullResponseCache.Instance,
            authorizationService: mockAuth.Object);

        await InitializeAsync(handler);
        await handler.ProcessMessageAsync(ToolCallMsg(9), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.ToolName.Should().Be("success_tool");
    }
}
