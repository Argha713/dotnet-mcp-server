// Argha - 2026-02-24 - tests for the MCP logging protocol: McpLogSink unit tests + handler routing
using FluentAssertions;
using McpServer.Configuration;
using McpServer.Logging;
using McpServer.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace McpServer.Tests;

// ============================================================
// McpLogSink — unit tests
// ============================================================
public class McpLogSinkTests
{
    // --- IsEnabled ---

    [Fact]
    public void IsEnabled_DefaultThreshold_WarningIsEnabled()
    {
        var sink = new McpLogSink();

        sink.IsEnabled(LogLevel.Warning).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_DefaultThreshold_InformationIsDisabled()
    {
        var sink = new McpLogSink();

        sink.IsEnabled(LogLevel.Information).Should().BeFalse();
    }

    [Fact]
    public void IsEnabled_DefaultThreshold_ErrorIsEnabled()
    {
        var sink = new McpLogSink();

        sink.IsEnabled(LogLevel.Error).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_AfterSetLevelDebug_DebugIsEnabled()
    {
        var sink = new McpLogSink();
        sink.SetLevel(McpLogLevel.Debug);

        sink.IsEnabled(LogLevel.Debug).Should().BeTrue();
    }

    [Fact]
    public void IsEnabled_AfterSetLevelError_WarningIsDisabled()
    {
        var sink = new McpLogSink();
        sink.SetLevel(McpLogLevel.Error);

        sink.IsEnabled(LogLevel.Warning).Should().BeFalse();
    }

    // --- WriteLog before Initialize ---

    [Fact]
    public void WriteLog_BeforeInitialize_DoesNotThrow()
    {
        var sink = new McpLogSink();

        var act = () => sink.WriteLog(LogLevel.Warning, "TestCategory", "test message");

        act.Should().NotThrow();
    }

    // --- WriteLog after Initialize ---

    [Fact]
    public void WriteLog_LevelMeetsThreshold_WritesNotificationJson()
    {
        var sink = new McpLogSink();
        // Argha - 2026-02-24 - use a MemoryStream-backed writer so we can read what was written
        var ms = new MemoryStream();
        sink.Initialize(new StreamWriter(ms) { AutoFlush = true });

        sink.WriteLog(LogLevel.Warning, "TestLogger", "something went wrong");

        ms.Position = 0;
        var written = new StreamReader(ms).ReadToEnd();
        written.Should().Contain("notifications/message");
    }

    [Fact]
    public void WriteLog_LevelMeetsThreshold_ContainsCorrectMcpLevel()
    {
        var sink = new McpLogSink();
        var ms = new MemoryStream();
        sink.Initialize(new StreamWriter(ms) { AutoFlush = true });

        sink.WriteLog(LogLevel.Warning, "Cat", "msg");

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd();
        json.Should().Contain("\"warning\"");
    }

    [Fact]
    public void WriteLog_LevelMeetsThreshold_ContainsCategoryAsLogger()
    {
        var sink = new McpLogSink();
        var ms = new MemoryStream();
        sink.Initialize(new StreamWriter(ms) { AutoFlush = true });

        sink.WriteLog(LogLevel.Error, "McpServer.Tools.GitTool", "repo not found");

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd();
        json.Should().Contain("McpServer.Tools.GitTool");
    }

    [Fact]
    public void WriteLog_LevelBelowThreshold_WritesNothing()
    {
        var sink = new McpLogSink(); // default: warning
        var ms = new MemoryStream();
        sink.Initialize(new StreamWriter(ms) { AutoFlush = true });

        sink.WriteLog(LogLevel.Information, "Cat", "info msg");

        ms.Position = 0;
        var written = new StreamReader(ms).ReadToEnd();
        written.Should().BeEmpty();
    }

    [Fact]
    public void WriteLog_InfoLevelMapping_WritesInfoString()
    {
        var sink = new McpLogSink();
        sink.SetLevel(McpLogLevel.Info);
        var ms = new MemoryStream();
        sink.Initialize(new StreamWriter(ms) { AutoFlush = true });

        sink.WriteLog(LogLevel.Information, "Cat", "server started");

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd();
        json.Should().Contain("\"info\"");
    }

    [Fact]
    public void WriteLog_CriticalLevelMapping_WritesCriticalString()
    {
        var sink = new McpLogSink();
        sink.SetLevel(McpLogLevel.Debug);
        var ms = new MemoryStream();
        sink.Initialize(new StreamWriter(ms) { AutoFlush = true });

        sink.WriteLog(LogLevel.Critical, "Cat", "fatal");

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd();
        json.Should().Contain("\"critical\"");
    }

    [Fact]
    public void WriteLog_OutputIsValidJson()
    {
        var sink = new McpLogSink();
        var ms = new MemoryStream();
        sink.Initialize(new StreamWriter(ms) { AutoFlush = true });

        sink.WriteLog(LogLevel.Warning, "Cat", "test");

        ms.Position = 0;
        var json = new StreamReader(ms).ReadToEnd().Trim();
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }
}

// ============================================================
// Logging handler routing — integration tests via ProcessMessageAsync
// ============================================================
public class LoggingHandlerTests
{
    private readonly McpServerHandler _handler;

    public LoggingHandlerTests()
    {
        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });

        _handler = new McpServerHandler(
            tools: Array.Empty<McpServer.Tools.ITool>(),
            resourceProviders: Array.Empty<McpServer.Resources.IResourceProvider>(),
            promptProviders: Array.Empty<McpServer.Prompts.IPromptProvider>(),
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            logSink: new McpLogSink(),
            // Argha - 2026-02-25 - Phase 6.2: no-op audit logger for unit tests
            auditLogger: McpServer.Audit.NullAuditLogger.Instance,
            // Argha - 2026-02-25 - Phase 6.3: no-op rate limiter for unit tests
            rateLimiter: McpServer.RateLimiting.NullRateLimiter.Instance,
            // Argha - 2026-02-25 - Phase 6.4: no-op cache for unit tests
            responseCache: McpServer.Caching.NullResponseCache.Instance,
            // Argha - 2026-02-25 - Phase 7: no-op auth for unit tests
            authorizationService: McpServer.Auth.NullAuthorizationService.Instance);
    }

    private static string MakeRequest(string method, int id = 1, string? paramsJson = null)
    {
        if (paramsJson != null)
            return $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}";
        return $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\"}}";
    }

    private async Task InitializeAsync()
    {
        var msg = MakeRequest("initialize", paramsJson: "{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}");
        await _handler.ProcessMessageAsync(msg, CancellationToken.None);
    }

    // --- Initialize advertises Logging capability ---

    [Fact]
    public async Task Initialize_AdvertisesLoggingCapability()
    {
        var msg = MakeRequest("initialize", paramsJson: "{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        var json = JsonSerializer.Serialize(response!.Result);
        json.Should().Contain("\"logging\"");
    }

    // --- logging/setLevel before initialize ---

    [Fact]
    public async Task SetLevel_BeforeInitialize_ReturnsNotInitializedError()
    {
        var msg = MakeRequest("logging/setLevel", paramsJson: "{\"level\":\"warning\"}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Message.Should().Contain("not initialized");
    }

    // --- logging/setLevel after initialize: valid levels ---

    [Theory]
    [InlineData("debug")]
    [InlineData("info")]
    [InlineData("notice")]
    [InlineData("warning")]
    [InlineData("error")]
    [InlineData("critical")]
    [InlineData("alert")]
    [InlineData("emergency")]
    public async Task SetLevel_ValidLevel_ReturnsEmptyResult(string level)
    {
        await InitializeAsync();
        var msg = MakeRequest("logging/setLevel", paramsJson: $"{{\"level\":\"{level}\"}}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().BeNull();
        response.Result.Should().NotBeNull();
    }

    // --- Case insensitivity ---

    [Fact]
    public async Task SetLevel_UpperCaseLevel_IsAccepted()
    {
        await InitializeAsync();
        var msg = MakeRequest("logging/setLevel", paramsJson: "{\"level\":\"WARNING\"}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().BeNull();
    }

    // --- Missing / invalid params ---

    [Fact]
    public async Task SetLevel_MissingParams_ReturnsInvalidParamsError()
    {
        await InitializeAsync();
        var msg = MakeRequest("logging/setLevel"); // no params

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.InvalidParams);
    }

    [Fact]
    public async Task SetLevel_UnknownLevel_ReturnsInvalidParamsError()
    {
        await InitializeAsync();
        var msg = MakeRequest("logging/setLevel", paramsJson: "{\"level\":\"verbose\"}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.InvalidParams);
        response.Error.Message.Should().Contain("verbose");
    }

    [Fact]
    public async Task SetLevel_EmptyLevel_ReturnsInvalidParamsError()
    {
        await InitializeAsync();
        var msg = MakeRequest("logging/setLevel", paramsJson: "{\"level\":\"\"}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.InvalidParams);
    }
}
