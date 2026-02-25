// Argha - 2026-02-25 - Phase 6.4: integration tests for McpServerHandler caching wiring
using FluentAssertions;
using McpServer.Audit;
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

namespace McpServer.Tests.Caching;

public class McpServerHandlerCacheTests
{
    // Argha - 2026-02-25 - tracks call count to verify caching bypasses execution
    private class CountingTool : McpServer.Tools.ITool
    {
        public int ExecuteCount { get; private set; }

        public string Name => "counting_tool";
        public string Description => "Counts executions";
        public JsonSchema InputSchema => new();

        public Task<ToolCallResult> ExecuteAsync(
            Dictionary<string, object>? arguments,
            McpServer.Progress.IProgressReporter? progress = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            return Task.FromResult(new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = $"execution #{ExecuteCount}" }
                }
            });
        }
    }

    // Argha - 2026-02-25 - always returns IsError=true to verify errors are not cached
    private class ErrorTool : McpServer.Tools.ITool
    {
        public string Name => "error_tool";
        public string Description => "Always returns an error result";
        public JsonSchema InputSchema => new();

        public Task<ToolCallResult> ExecuteAsync(
            Dictionary<string, object>? arguments,
            McpServer.Progress.IProgressReporter? progress = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = "tool error" }
                },
                IsError = true
            });
        }
    }

    // Argha - 2026-02-25 - build a handler with configurable cache and tool
    private static McpServerHandler BuildHandler(IResponseCache cache, McpServer.Tools.ITool tool)
    {
        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });
        return new McpServerHandler(
            tools: new McpServer.Tools.ITool[] { tool },
            resourceProviders: Array.Empty<McpServer.Resources.IResourceProvider>(),
            promptProviders: Array.Empty<McpServer.Prompts.IPromptProvider>(),
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            logSink: new McpLogSink(),
            auditLogger: NullAuditLogger.Instance,
            rateLimiter: NullRateLimiter.Instance,
            // Argha - 2026-02-25 - the cache under test
            responseCache: cache);
    }

    private static async Task InitializeAsync(McpServerHandler handler)
    {
        var msg = "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\"," +
                  "\"params\":{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}";
        await handler.ProcessMessageAsync(msg, CancellationToken.None);
    }

    private static string ToolCallMsg(string toolName, int id = 1, string action = "run") =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"tools/call\"," +
        $"\"params\":{{\"name\":\"{toolName}\",\"arguments\":{{\"action\":\"{action}\"}}}}}}";

    // Argha - 2026-02-25 - build a real MemoryResponseCache with short but non-zero TTL
    private static MemoryResponseCache RealCache(int defaultTtl = 300) =>
        new(Options.Create(new CacheSettings
        {
            Enabled = true,
            DefaultTtlSeconds = defaultTtl,
            MaxEntries = 1000,
        }));

    // -------------------------------------------------------
    // Cache miss — tool executes
    // -------------------------------------------------------

    [Fact]
    public async Task CacheMiss_ToolExecutes()
    {
        var tool = new CountingTool();
        var handler = BuildHandler(NullResponseCache.Instance, tool);
        await InitializeAsync(handler);

        await handler.ProcessMessageAsync(ToolCallMsg("counting_tool"), CancellationToken.None);

        tool.ExecuteCount.Should().Be(1, "tool must execute on a cache miss");
    }

    // -------------------------------------------------------
    // Cache hit — tool NOT executed, cached result returned
    // -------------------------------------------------------

    [Fact]
    public async Task CacheHit_ToolNotExecutedSecondTime()
    {
        var tool = new CountingTool();
        var handler = BuildHandler(RealCache(), tool);
        await InitializeAsync(handler);

        // First call — populates cache
        await handler.ProcessMessageAsync(ToolCallMsg("counting_tool", id: 1), CancellationToken.None);
        // Second call with same args — should hit cache
        await handler.ProcessMessageAsync(ToolCallMsg("counting_tool", id: 2), CancellationToken.None);

        tool.ExecuteCount.Should().Be(1, "second call with same args must be served from cache");
    }

    [Fact]
    public async Task CacheHit_ReturnsCachedResult()
    {
        var tool = new CountingTool();
        var handler = BuildHandler(RealCache(), tool);
        await InitializeAsync(handler);

        // First call
        var first = await handler.ProcessMessageAsync(ToolCallMsg("counting_tool", id: 1), CancellationToken.None);
        // Second call (cache hit)
        var second = await handler.ProcessMessageAsync(ToolCallMsg("counting_tool", id: 2), CancellationToken.None);

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        first!.Error.Should().BeNull();
        second!.Error.Should().BeNull();

        var firstJson = JsonSerializer.Serialize(first.Result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var secondJson = JsonSerializer.Serialize(second.Result,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        firstJson.Should().Be(secondJson, "cached result must match the original");
    }

    // -------------------------------------------------------
    // Error results NOT cached
    // -------------------------------------------------------

    [Fact]
    public async Task ErrorResult_NotCached_ToolAlwaysExecutes()
    {
        // Argha - 2026-02-25 - error results must not be cached; every call re-executes the tool
        var mockCache = new Mock<IResponseCache>();
        mockCache.Setup(c => c.TryGet(It.IsAny<string>(), It.IsAny<string>(), out It.Ref<ToolCallResult?>.IsAny))
                 .Returns(false); // always miss

        var tool = new ErrorTool();
        var handler = BuildHandler(mockCache.Object, tool);
        await InitializeAsync(handler);

        await handler.ProcessMessageAsync(ToolCallMsg("error_tool", id: 1), CancellationToken.None);

        // Set must NEVER be called for an error result
        mockCache.Verify(c => c.Set(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ToolCallResult>()), Times.Never,
            "error results must not be stored in the cache");
    }

    // -------------------------------------------------------
    // Cache hit audited with "CacheHit" outcome
    // -------------------------------------------------------

    [Fact]
    public async Task CacheHit_AuditedWithCacheHitOutcome()
    {
        var tool = new CountingTool();
        var cache = RealCache();

        AuditRecord? hitRecord = null;
        var mockAudit = new Mock<IAuditLogger>();
        mockAudit.Setup(a => a.LogCallAsync(It.IsAny<AuditRecord>()))
                 .Callback<AuditRecord>(r =>
                 {
                     if (r.Outcome == "CacheHit") hitRecord = r;
                 })
                 .Returns(Task.CompletedTask);

        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });
        var handler = new McpServerHandler(
            tools: new McpServer.Tools.ITool[] { tool },
            resourceProviders: Array.Empty<McpServer.Resources.IResourceProvider>(),
            promptProviders: Array.Empty<McpServer.Prompts.IPromptProvider>(),
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            logSink: new McpLogSink(),
            auditLogger: mockAudit.Object,
            rateLimiter: NullRateLimiter.Instance,
            responseCache: cache);

        await InitializeAsync(handler);

        // First call — populates cache
        await handler.ProcessMessageAsync(ToolCallMsg("counting_tool", id: 1), CancellationToken.None);
        // Second call — cache hit
        await handler.ProcessMessageAsync(ToolCallMsg("counting_tool", id: 2), CancellationToken.None);

        hitRecord.Should().NotBeNull("cache hits must be recorded in the audit log");
        hitRecord!.Outcome.Should().Be("CacheHit");
        hitRecord.ToolName.Should().Be("counting_tool");
    }

    // -------------------------------------------------------
    // NullCache — falls through to tool normally
    // -------------------------------------------------------

    [Fact]
    public async Task NullCache_AlwaysFallsThroughToTool()
    {
        var tool = new CountingTool();
        var handler = BuildHandler(NullResponseCache.Instance, tool);
        await InitializeAsync(handler);

        await handler.ProcessMessageAsync(ToolCallMsg("counting_tool", id: 1), CancellationToken.None);
        await handler.ProcessMessageAsync(ToolCallMsg("counting_tool", id: 2), CancellationToken.None);
        await handler.ProcessMessageAsync(ToolCallMsg("counting_tool", id: 3), CancellationToken.None);

        tool.ExecuteCount.Should().Be(3, "NullResponseCache never hits; tool must execute every time");
    }

    // -------------------------------------------------------
    // Different args → different cache keys → both execute
    // -------------------------------------------------------

    [Fact]
    public async Task DifferentArgs_BothExecute()
    {
        var tool = new CountingTool();
        var handler = BuildHandler(RealCache(), tool);
        await InitializeAsync(handler);

        var msg1 = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\"," +
                   "\"params\":{\"name\":\"counting_tool\",\"arguments\":{\"action\":\"alpha\"}}}";
        var msg2 = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\"," +
                   "\"params\":{\"name\":\"counting_tool\",\"arguments\":{\"action\":\"beta\"}}}";

        await handler.ProcessMessageAsync(msg1, CancellationToken.None);
        await handler.ProcessMessageAsync(msg2, CancellationToken.None);

        tool.ExecuteCount.Should().Be(2, "different args produce different cache keys; both must execute");
    }
}
