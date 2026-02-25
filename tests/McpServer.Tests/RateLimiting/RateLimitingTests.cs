// Argha - 2026-02-25 - Phase 6.3: unit tests for the rate limiting subsystem
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

namespace McpServer.Tests.RateLimiting;

// ============================================================
// NullRateLimiter — unit tests
// ============================================================
public class NullRateLimiterTests
{
    [Fact]
    public void TryAcquire_AlwaysReturnsTrue()
    {
        var limiter = NullRateLimiter.Instance;

        limiter.TryAcquire("any_tool").Should().BeTrue();
    }

    [Fact]
    public void TryAcquire_RepeatedCalls_AlwaysReturnsTrue()
    {
        var limiter = NullRateLimiter.Instance;

        for (int i = 0; i < 1000; i++)
            limiter.TryAcquire("sql_query").Should().BeTrue();
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        NullRateLimiter.Instance.Should().BeSameAs(NullRateLimiter.Instance);
    }
}

// ============================================================
// SlidingWindowRateLimiter — unit tests
// ============================================================
public class SlidingWindowRateLimiterTests
{
    // Argha - 2026-02-25 - helper: build a limiter with a controllable fake clock
    private static (SlidingWindowRateLimiter limiter, Action<TimeSpan> advance) Build(
        int defaultLimit,
        Dictionary<string, int>? toolLimits = null)
    {
        var fakeNow = DateTimeOffset.UtcNow;
        Func<DateTimeOffset> clock = () => fakeNow;

        var settings = Options.Create(new RateLimitSettings
        {
            Enabled = true,
            DefaultLimitPerMinute = defaultLimit,
            ToolLimits = toolLimits ?? new Dictionary<string, int>(),
        });

        var limiter = new SlidingWindowRateLimiter(settings, clock);
        Action<TimeSpan> advance = delta => fakeNow = fakeNow.Add(delta);
        return (limiter, advance);
    }

    [Fact]
    public void FirstCall_AlwaysAllowed()
    {
        var (limiter, _) = Build(defaultLimit: 5);

        limiter.TryAcquire("datetime").Should().BeTrue();
    }

    [Fact]
    public void UnderLimit_AllCallsAllowed()
    {
        var (limiter, _) = Build(defaultLimit: 5);

        for (int i = 0; i < 5; i++)
            limiter.TryAcquire("datetime").Should().BeTrue($"call {i + 1} should be within limit");
    }

    [Fact]
    public void AtLimit_NextCallRejected()
    {
        var (limiter, _) = Build(defaultLimit: 3);

        limiter.TryAcquire("sql_query"); // 1
        limiter.TryAcquire("sql_query"); // 2
        limiter.TryAcquire("sql_query"); // 3 — at limit

        limiter.TryAcquire("sql_query").Should().BeFalse("limit of 3 has been reached");
    }

    [Fact]
    public void AfterWindowExpiry_AllowsAgain()
    {
        var (limiter, advance) = Build(defaultLimit: 2);

        limiter.TryAcquire("http_request"); // 1
        limiter.TryAcquire("http_request"); // 2 — at limit
        limiter.TryAcquire("http_request").Should().BeFalse();

        // Advance clock past the 1-minute window
        advance(TimeSpan.FromMinutes(2));

        limiter.TryAcquire("http_request").Should().BeTrue("window has expired, tokens refilled");
    }

    [Fact]
    public void PartialWindowExpiry_CountsOnlyRecentCalls()
    {
        var (limiter, advance) = Build(defaultLimit: 3);

        limiter.TryAcquire("git"); // t=0
        limiter.TryAcquire("git"); // t=0

        // Advance 90 seconds — those 2 calls are now outside the window
        advance(TimeSpan.FromSeconds(90));

        limiter.TryAcquire("git"); // t=90s — 1 in window
        limiter.TryAcquire("git"); // t=90s — 2 in window
        limiter.TryAcquire("git"); // t=90s — 3 in window (at limit)

        limiter.TryAcquire("git").Should().BeFalse("3 calls within window, limit reached");
    }

    [Fact]
    public void PerTool_LimitsAreIndependent()
    {
        var (limiter, _) = Build(defaultLimit: 2);

        // Use up tool_a's limit
        limiter.TryAcquire("tool_a");
        limiter.TryAcquire("tool_a");
        limiter.TryAcquire("tool_a").Should().BeFalse();

        // tool_b should still be unaffected
        limiter.TryAcquire("tool_b").Should().BeTrue();
        limiter.TryAcquire("tool_b").Should().BeTrue();
    }

    [Fact]
    public void ToolSpecificLimit_OverridesDefault()
    {
        var (limiter, _) = Build(
            defaultLimit: 60,
            toolLimits: new Dictionary<string, int> { ["sql_query"] = 2 });

        limiter.TryAcquire("sql_query"); // 1
        limiter.TryAcquire("sql_query"); // 2

        // sql_query hits its tool-specific limit of 2, not the default 60
        limiter.TryAcquire("sql_query").Should().BeFalse("tool-specific limit of 2 applies");
    }

    [Fact]
    public void ZeroToolLimit_MeansUnlimited()
    {
        var (limiter, _) = Build(
            defaultLimit: 3,
            toolLimits: new Dictionary<string, int> { ["datetime"] = 0 });

        for (int i = 0; i < 1000; i++)
            limiter.TryAcquire("datetime").Should().BeTrue("limit of 0 means unlimited");
    }

    [Fact]
    public void ZeroDefaultLimit_MeansUnlimited()
    {
        var (limiter, _) = Build(defaultLimit: 0);

        for (int i = 0; i < 1000; i++)
            limiter.TryAcquire("any_tool").Should().BeTrue("default limit of 0 means unlimited");
    }

    [Fact]
    public void LimitOf1_SecondCallRejected()
    {
        var (limiter, _) = Build(defaultLimit: 1);

        limiter.TryAcquire("filesystem").Should().BeTrue();
        limiter.TryAcquire("filesystem").Should().BeFalse();
    }

    [Fact]
    public void CaseInsensitiveToolName_SameBucket()
    {
        // Argha - 2026-02-25 - tool names should be treated case-insensitively to prevent trivial bypass
        var (limiter, _) = Build(defaultLimit: 1);

        limiter.TryAcquire("SQL_QUERY").Should().BeTrue();
        // Same tool, different casing — must hit the same bucket
        limiter.TryAcquire("sql_query").Should().BeFalse();
    }
}

// ============================================================
// McpServerHandler — rate limiting wiring integration tests
// ============================================================
public class McpServerHandlerRateLimitTests
{
    // Argha - 2026-02-25 - fake tool that always succeeds, used to test rate-limit rejection
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

    private static McpServerHandler BuildHandler(IRateLimiter rateLimiter)
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
            // Argha - 2026-02-25 - inject the rate limiter under test
            rateLimiter: rateLimiter,
            // Argha - 2026-02-25 - Phase 6.4: no-op cache; rate limiting tests don't test caching
            responseCache: NullResponseCache.Instance,
            // Argha - 2026-02-25 - Phase 7: no-op auth; rate limiting tests don't test authorization
            authorizationService: NullAuthorizationService.Instance);
    }

    private static async Task InitializeAsync(McpServerHandler handler)
    {
        var msg = "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\"," +
                  "\"params\":{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}";
        await handler.ProcessMessageAsync(msg, CancellationToken.None);
    }

    private static string ToolCallMsg(int id = 1) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"tools/call\"," +
        $"\"params\":{{\"name\":\"success_tool\",\"arguments\":{{\"action\":\"run\"}}}}}}";

    [Fact]
    public async Task UnderLimit_ToolExecutes_ReturnsSuccess()
    {
        var handler = BuildHandler(NullRateLimiter.Instance);
        await InitializeAsync(handler);

        var response = await handler.ProcessMessageAsync(ToolCallMsg(1), CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().BeNull();
        var result = response.Result as ToolCallResult;
        result?.IsError.Should().NotBe(true);
    }

    [Fact]
    public async Task RateLimitExceeded_ReturnsIsErrorResult()
    {
        // Argha - 2026-02-25 - mock that always rejects, simulating a saturated limiter
        var mockLimiter = new Mock<IRateLimiter>();
        mockLimiter.Setup(l => l.TryAcquire(It.IsAny<string>())).Returns(false);

        var handler = BuildHandler(mockLimiter.Object);
        await InitializeAsync(handler);

        var response = await handler.ProcessMessageAsync(ToolCallMsg(2), CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().BeNull("rate limit returns a result, not a JSON-RPC error");
        var result = JsonSerializer.Deserialize<ToolCallResult>(
            JsonSerializer.Serialize(response.Result),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        result?.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task RateLimitExceeded_MessageContainsToolName()
    {
        var mockLimiter = new Mock<IRateLimiter>();
        mockLimiter.Setup(l => l.TryAcquire(It.IsAny<string>())).Returns(false);

        var handler = BuildHandler(mockLimiter.Object);
        await InitializeAsync(handler);

        var response = await handler.ProcessMessageAsync(ToolCallMsg(3), CancellationToken.None);

        var json = JsonSerializer.Serialize(response!.Result);
        json.Should().Contain("success_tool");
        json.Should().Contain("Rate limit");
    }

    [Fact]
    public async Task RateLimitExceeded_TryAcquireCalledWithCorrectToolName()
    {
        var mockLimiter = new Mock<IRateLimiter>();
        mockLimiter.Setup(l => l.TryAcquire(It.IsAny<string>())).Returns(true);

        var handler = BuildHandler(mockLimiter.Object);
        await InitializeAsync(handler);

        await handler.ProcessMessageAsync(ToolCallMsg(4), CancellationToken.None);

        mockLimiter.Verify(l => l.TryAcquire("success_tool"), Times.Once);
    }

    [Fact]
    public async Task RateLimitExceeded_AuditRecordWritten()
    {
        // Argha - 2026-02-25 - rate-limited calls must still be recorded in the audit log
        var mockLimiter = new Mock<IRateLimiter>();
        mockLimiter.Setup(l => l.TryAcquire(It.IsAny<string>())).Returns(false);

        var mockAudit = new Mock<IAuditLogger>();
        AuditRecord? captured = null;
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
            rateLimiter: mockLimiter.Object,
            // Argha - 2026-02-25 - Phase 6.4: no-op cache; rate limiting tests don't test caching
            responseCache: NullResponseCache.Instance,
            // Argha - 2026-02-25 - Phase 7: no-op auth; rate limiting tests don't test authorization
            authorizationService: NullAuthorizationService.Instance);

        await InitializeAsync(handler);
        await handler.ProcessMessageAsync(ToolCallMsg(5), CancellationToken.None);

        captured.Should().NotBeNull("rate-limited calls must be audited");
        captured!.Outcome.Should().Be("RateLimited");
        captured.ToolName.Should().Be("success_tool");
    }

    [Fact]
    public async Task RealLimiter_LimitOf1_SecondCallRejected()
    {
        // Argha - 2026-02-25 - end-to-end test with a real SlidingWindowRateLimiter (limit=1)
        var settings = Options.Create(new RateLimitSettings
        {
            Enabled = true,
            DefaultLimitPerMinute = 1,
        });
        var limiter = new SlidingWindowRateLimiter(settings);
        var handler = BuildHandler(limiter);
        await InitializeAsync(handler);

        var first = await handler.ProcessMessageAsync(ToolCallMsg(6), CancellationToken.None);
        var second = await handler.ProcessMessageAsync(ToolCallMsg(7), CancellationToken.None);

        // First call should succeed
        var firstJson = JsonSerializer.Serialize(first!.Result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        firstJson.Should().NotContain("Rate limit");

        // Second call should be rate-limited
        var secondJson = JsonSerializer.Serialize(second!.Result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        secondJson.Should().Contain("Rate limit");
    }
}
