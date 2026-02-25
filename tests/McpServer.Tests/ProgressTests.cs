// Argha - 2026-02-24 - unit tests for IProgressReporter implementations + handler progress wiring
using FluentAssertions;
using McpServer.Configuration;
using McpServer.Logging;
using McpServer.Progress;
using McpServer.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace McpServer.Tests;

// ============================================================
// NullProgressReporter — unit tests
// ============================================================
public class NullProgressReporterTests
{
    [Fact]
    public void Report_DoesNotThrow()
    {
        var reporter = NullProgressReporter.Instance;

        var act = () => reporter.Report(42, 100);

        act.Should().NotThrow();
    }

    [Fact]
    public void Report_WithoutTotal_DoesNotThrow()
    {
        var reporter = NullProgressReporter.Instance;

        var act = () => reporter.Report(10);

        act.Should().NotThrow();
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        NullProgressReporter.Instance.Should().BeSameAs(NullProgressReporter.Instance);
    }
}

// ============================================================
// ProgressReporter — unit tests
// ============================================================
public class ProgressReporterTests
{
    private static (McpLogSink sink, MemoryStream ms) CreateInitializedSink()
    {
        var sink = new McpLogSink();
        // Argha - 2026-02-24 - set level to debug so WriteLog calls don't interfere
        sink.SetLevel(McpLogLevel.Debug);
        var ms = new MemoryStream();
        sink.Initialize(new StreamWriter(ms) { AutoFlush = true });
        return (sink, ms);
    }

    [Fact]
    public void Report_EmitsProgressNotificationJson()
    {
        var (sink, ms) = CreateInitializedSink();
        var reporter = new ProgressReporter("token-abc", sink);

        reporter.Report(25, 100);

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd().Trim();
        json.Should().Contain("notifications/progress");
    }

    [Fact]
    public void Report_ContainsCorrectProgressToken()
    {
        var (sink, ms) = CreateInitializedSink();
        var reporter = new ProgressReporter("my-token", sink);

        reporter.Report(50);

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd();
        json.Should().Contain("my-token");
    }

    [Fact]
    public void Report_ContainsProgressValue()
    {
        var (sink, ms) = CreateInitializedSink();
        var reporter = new ProgressReporter("tok", sink);

        reporter.Report(75, 100);

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd();
        json.Should().Contain("75");
    }

    [Fact]
    public void Report_ContainsTotalWhenProvided()
    {
        var (sink, ms) = CreateInitializedSink();
        var reporter = new ProgressReporter("tok", sink);

        reporter.Report(10, 200);

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd();
        json.Should().Contain("200");
    }

    [Fact]
    public void Report_WithoutTotal_OmitsTotalField()
    {
        var (sink, ms) = CreateInitializedSink();
        var reporter = new ProgressReporter("tok", sink);

        reporter.Report(10);

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd().Trim();
        // total is WhenWritingNull so it should not appear
        json.Should().NotContain("\"total\"");
    }

    [Fact]
    public void Report_OutputIsValidJson()
    {
        var (sink, ms) = CreateInitializedSink();
        var reporter = new ProgressReporter("tok", sink);

        reporter.Report(33, 100);

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd().Trim();
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public void Report_BeforeSinkInitialized_DoesNotThrow()
    {
        // Argha - 2026-02-24 - sink with no writer initialized — reporter must not throw
        var sink = new McpLogSink();
        var reporter = new ProgressReporter("tok", sink);

        var act = () => reporter.Report(5, 10);

        act.Should().NotThrow();
    }

    [Fact]
    public void Report_MultipleReports_AllWrittenToStream()
    {
        var (sink, ms) = CreateInitializedSink();
        var reporter = new ProgressReporter("tok", sink);

        reporter.Report(0, 100);
        reporter.Report(50, 100);
        reporter.Report(100, 100);

        ms.Position = 0;
        var text = new StreamReader(ms).ReadToEnd();
        // Three separate JSON lines
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3);
    }
}

// ============================================================
// McpServerHandler — progress wiring integration tests
// ============================================================
public class McpServerHandlerProgressTests
{
    // Argha - 2026-02-24 - fake tool that calls progress.Report() so we can verify the handler wires it correctly
    private class FakeProgressTool : McpServer.Tools.ITool
    {
        public string Name => "fake_progress";
        public string Description => "Fake tool for progress testing";
        public McpServer.Protocol.JsonSchema InputSchema => new();

        public Task<McpServer.Protocol.ToolCallResult> ExecuteAsync(
            Dictionary<string, object>? arguments,
            McpServer.Progress.IProgressReporter? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Report two progress steps so tests can verify notifications were emitted
            progress?.Report(0, 100);
            progress?.Report(100, 100);

            return Task.FromResult(new McpServer.Protocol.ToolCallResult
            {
                Content = new List<McpServer.Protocol.ContentBlock>
                {
                    new() { Type = "text", Text = "done" }
                }
            });
        }
    }

    private static McpServerHandler BuildHandler(McpLogSink sink, MemoryStream ms)
    {
        sink.Initialize(new StreamWriter(ms) { AutoFlush = true });
        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });
        return new McpServerHandler(
            tools: new McpServer.Tools.ITool[] { new FakeProgressTool() },
            resourceProviders: Array.Empty<McpServer.Resources.IResourceProvider>(),
            promptProviders: Array.Empty<McpServer.Prompts.IPromptProvider>(),
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            logSink: sink,
            // Argha - 2026-02-25 - Phase 6.2: no-op audit logger for unit tests
            auditLogger: McpServer.Audit.NullAuditLogger.Instance,
            // Argha - 2026-02-25 - Phase 6.3: no-op rate limiter for unit tests
            rateLimiter: McpServer.RateLimiting.NullRateLimiter.Instance);
    }

    private static async Task InitializeHandlerAsync(McpServerHandler handler)
    {
        var msg = "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\"," +
                  "\"params\":{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}}";
        await handler.ProcessMessageAsync(msg, CancellationToken.None);
    }

    [Fact]
    public async Task ToolCall_WithProgressToken_EmitsProgressNotifications()
    {
        var sink = new McpLogSink();
        var ms = new MemoryStream();
        var handler = BuildHandler(sink, ms);
        await InitializeHandlerAsync(handler);

        var msg = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"fake_progress\",\"arguments\":{}," +
                  "\"_meta\":{\"progressToken\":\"test-token-1\"}}}";

        await handler.ProcessMessageAsync(msg, CancellationToken.None);

        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();
        // Should contain at least one notifications/progress line
        output.Should().Contain("notifications/progress");
        output.Should().Contain("test-token-1");
    }

    [Fact]
    public async Task ToolCall_WithoutProgressToken_EmitsNoProgressNotifications()
    {
        var sink = new McpLogSink();
        var ms = new MemoryStream();
        var handler = BuildHandler(sink, ms);
        await InitializeHandlerAsync(handler);

        // No _meta block
        var msg = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"fake_progress\",\"arguments\":{}}}";

        await handler.ProcessMessageAsync(msg, CancellationToken.None);

        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();
        output.Should().NotContain("notifications/progress");
    }

    [Fact]
    public async Task ToolCall_WithEmptyProgressToken_EmitsNoProgressNotifications()
    {
        var sink = new McpLogSink();
        var ms = new MemoryStream();
        var handler = BuildHandler(sink, ms);
        await InitializeHandlerAsync(handler);

        // _meta present but progressToken is empty string
        var msg = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"fake_progress\",\"arguments\":{}," +
                  "\"_meta\":{\"progressToken\":\"\"}}}";

        await handler.ProcessMessageAsync(msg, CancellationToken.None);

        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();
        output.Should().NotContain("notifications/progress");
    }

    [Fact]
    public async Task ToolCall_WithProgressToken_ToolResponseStillReturned()
    {
        var sink = new McpLogSink();
        var ms = new MemoryStream();
        var handler = BuildHandler(sink, ms);
        await InitializeHandlerAsync(handler);

        var msg = "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"fake_progress\",\"arguments\":{}," +
                  "\"_meta\":{\"progressToken\":\"token-4\"}}}";

        var response = await handler.ProcessMessageAsync(msg, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().BeNull();
        response.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task ToolCall_WithNullProgressToken_EmitsNoProgressNotifications()
    {
        var sink = new McpLogSink();
        var ms = new MemoryStream();
        var handler = BuildHandler(sink, ms);
        await InitializeHandlerAsync(handler);

        // _meta present but progressToken is JSON null
        var msg = "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"fake_progress\",\"arguments\":{}," +
                  "\"_meta\":{\"progressToken\":null}}}";

        await handler.ProcessMessageAsync(msg, CancellationToken.None);

        ms.Position = 0;
        var output = new StreamReader(ms).ReadToEnd();
        output.Should().NotContain("notifications/progress");
    }
}
