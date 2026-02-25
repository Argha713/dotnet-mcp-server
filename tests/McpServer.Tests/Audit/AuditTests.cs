// Argha - 2026-02-25 - Phase 6.2: unit tests for the audit logging subsystem
using FluentAssertions;
using McpServer.Audit;
using McpServer.Configuration;
using McpServer.Logging;
using McpServer.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using Xunit;

namespace McpServer.Tests.Audit;

// ============================================================
// AuditArgumentSanitizer — unit tests
// ============================================================
public class AuditArgumentSanitizerTests
{
    [Fact]
    public void Sanitize_NullArgs_ReturnsNull()
    {
        AuditArgumentSanitizer.Sanitize(null).Should().BeNull();
    }

    [Fact]
    public void Sanitize_EmptyDict_ReturnsEmpty()
    {
        var result = AuditArgumentSanitizer.Sanitize(new Dictionary<string, object>());

        result.Should().NotBeNull();
        result!.Should().BeEmpty();
    }

    [Fact]
    public void Sanitize_PasswordKey_Redacted()
    {
        var args = new Dictionary<string, object> { ["password"] = "s3cret" };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["password"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_PwdKey_Redacted()
    {
        var args = new Dictionary<string, object> { ["pwd"] = "hunter2" };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["pwd"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_TokenKey_Redacted()
    {
        var args = new Dictionary<string, object> { ["token"] = "tok_abc123" };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["token"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_ApiKeyKey_Redacted()
    {
        var args = new Dictionary<string, object> { ["api_key"] = "key_xyz" };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["api_key"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_ApiKeyNoUnderscore_Redacted()
    {
        var args = new Dictionary<string, object> { ["apikey"] = "key_abc" };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["apikey"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_CaseInsensitive_Redacted()
    {
        var args = new Dictionary<string, object> { ["PASSWORD"] = "uppercase_secret" };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["PASSWORD"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_MixedCaseSensitive_Redacted()
    {
        var args = new Dictionary<string, object> { ["Password"] = "mixed" };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["Password"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_NonSensitiveKey_Unchanged()
    {
        var args = new Dictionary<string, object> { ["query"] = "SELECT * FROM Orders" };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["query"].Should().Be("SELECT * FROM Orders");
    }

    [Fact]
    public void Sanitize_MixedKeys_OnlySensitiveRedacted()
    {
        var args = new Dictionary<string, object>
        {
            ["action"] = "execute_query",
            ["connection"] = "MyDB",
            ["password"] = "s3cret",
        };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["action"].Should().Be("execute_query");
        result["connection"].Should().Be("MyDB");
        result["password"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_SecretKey_Redacted()
    {
        var args = new Dictionary<string, object> { ["secret"] = "my-secret-value" };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["secret"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_AuthorizationKey_Redacted()
    {
        var args = new Dictionary<string, object> { ["authorization"] = "Bearer abc123" };

        var result = AuditArgumentSanitizer.Sanitize(args);

        result!["authorization"].Should().Be("[REDACTED]");
    }

    [Fact]
    public void Sanitize_DoesNotMutateOriginal()
    {
        var args = new Dictionary<string, object> { ["password"] = "original" };

        AuditArgumentSanitizer.Sanitize(args);

        // Original must be untouched
        args["password"].Should().Be("original");
    }
}

// ============================================================
// NullAuditLogger — unit tests
// ============================================================
public class NullAuditLoggerTests
{
    [Fact]
    public async Task LogCallAsync_DoesNotThrow()
    {
        var logger = NullAuditLogger.Instance;
        var record = MakeRecord("datetime", "Success");

        var act = async () => await logger.LogCallAsync(record);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void Instance_IsSingleton()
    {
        NullAuditLogger.Instance.Should().BeSameAs(NullAuditLogger.Instance);
    }

    private static AuditRecord MakeRecord(string tool, string outcome) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        CorrelationId = Guid.NewGuid().ToString("N"),
        ToolName = tool,
        Outcome = outcome,
        DurationMs = 1,
    };
}

// ============================================================
// FileAuditLogger — unit tests
// ============================================================
public class FileAuditLoggerTests : IDisposable
{
    private readonly string _tempDir;

    public FileAuditLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "audit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private FileAuditLogger BuildLogger(bool enabled = true, int retentionDays = 30)
    {
        var settings = Options.Create(new AuditSettings
        {
            Enabled = enabled,
            LogDirectory = _tempDir,
            RetentionDays = retentionDays,
            SanitizeArguments = true,
        });
        return new FileAuditLogger(settings);
    }

    private static AuditRecord MakeRecord(string tool = "sql_query", string action = "execute_query",
        string outcome = "Success", string? error = null, long durationMs = 42) => new()
    {
        Timestamp = DateTimeOffset.UtcNow,
        CorrelationId = Guid.NewGuid().ToString("N"),
        ToolName = tool,
        Action = action,
        Arguments = new Dictionary<string, object> { ["connection"] = "MyDB", ["query"] = "SELECT 1" },
        Outcome = outcome,
        ErrorMessage = error,
        DurationMs = durationMs,
    };

    [Fact]
    public async Task LogCallAsync_WritesOneJsonlLine()
    {
        using var logger = BuildLogger();
        var record = MakeRecord();

        await logger.LogCallAsync(record);

        var files = Directory.GetFiles(_tempDir, "audit-*.jsonl");
        files.Should().HaveCount(1);
        var lines = await File.ReadAllLinesAsync(files[0]);
        lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task LogCallAsync_LineIsValidJson()
    {
        using var logger = BuildLogger();
        await logger.LogCallAsync(MakeRecord());

        var files = Directory.GetFiles(_tempDir, "audit-*.jsonl");
        var line = (await File.ReadAllLinesAsync(files[0]))[0];

        var act = () => JsonDocument.Parse(line);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task LogCallAsync_ContainsExpectedFields()
    {
        using var logger = BuildLogger();
        var record = MakeRecord(tool: "filesystem", action: "read_file", outcome: "Success", durationMs: 7);

        await logger.LogCallAsync(record);

        var files = Directory.GetFiles(_tempDir, "audit-*.jsonl");
        var json = (await File.ReadAllLinesAsync(files[0]))[0];

        json.Should().Contain("filesystem");
        json.Should().Contain("read_file");
        json.Should().Contain("Success");
        json.Should().Contain("7");
    }

    [Fact]
    public async Task LogCallAsync_TwoCalls_TwoLines()
    {
        using var logger = BuildLogger();

        await logger.LogCallAsync(MakeRecord());
        await logger.LogCallAsync(MakeRecord());

        var files = Directory.GetFiles(_tempDir, "audit-*.jsonl");
        var lines = await File.ReadAllLinesAsync(files[0]);
        lines.Where(l => !string.IsNullOrWhiteSpace(l)).Should().HaveCount(2);
    }

    [Fact]
    public async Task LogCallAsync_FileName_ContainsToday()
    {
        using var logger = BuildLogger();
        await logger.LogCallAsync(MakeRecord());

        var files = Directory.GetFiles(_tempDir, "audit-*.jsonl");
        files[0].Should().Contain(DateTime.UtcNow.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task LogCallAsync_WhenDisabled_CreatesNoFile()
    {
        using var logger = BuildLogger(enabled: false);

        await logger.LogCallAsync(MakeRecord());

        Directory.GetFiles(_tempDir).Should().BeEmpty();
    }

    [Fact]
    public async Task LogCallAsync_DirectoryCreated_IfNotExists()
    {
        // Argha - 2026-02-25 - logger must create the directory itself on first write
        var subDir = Path.Combine(_tempDir, "nested", "audit");
        var settings = Options.Create(new AuditSettings
        {
            Enabled = true,
            LogDirectory = subDir,
            RetentionDays = 30,
        });
        using var logger = new FileAuditLogger(settings);

        await logger.LogCallAsync(MakeRecord());

        Directory.Exists(subDir).Should().BeTrue();
        Directory.GetFiles(subDir, "audit-*.jsonl").Should().HaveCount(1);
    }

    [Fact]
    public async Task LogCallAsync_SensitiveArguments_Redacted()
    {
        using var logger = BuildLogger();
        var record = MakeRecord() with
        {
            Arguments = new Dictionary<string, object>
            {
                ["connection"] = "MyDB",
                ["password"] = "s3cret!",
            }
        };

        await logger.LogCallAsync(record);

        var files = Directory.GetFiles(_tempDir, "audit-*.jsonl");
        var json = (await File.ReadAllLinesAsync(files[0]))[0];

        json.Should().Contain("[REDACTED]");
        json.Should().NotContain("s3cret!");
    }

    [Fact]
    public async Task LogCallAsync_FailureRecord_ContainsErrorMessage()
    {
        using var logger = BuildLogger();
        var record = MakeRecord(outcome: "Failure", error: "Connection refused");

        await logger.LogCallAsync(record);

        var files = Directory.GetFiles(_tempDir, "audit-*.jsonl");
        var json = (await File.ReadAllLinesAsync(files[0]))[0];

        json.Should().Contain("Failure");
        json.Should().Contain("Connection refused");
    }

    [Fact]
    public async Task RetentionCleanup_DeletesOldFiles_OnFirstWrite()
    {
        // Argha - 2026-02-25 - seed an old file that should be deleted by the retention policy
        var oldFile = Path.Combine(_tempDir, "audit-2020-01-01.jsonl");
        await File.WriteAllTextAsync(oldFile, "{\"old\":true}\n");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-60));

        using var logger = BuildLogger(retentionDays: 30);
        await logger.LogCallAsync(MakeRecord());

        File.Exists(oldFile).Should().BeFalse("file older than RetentionDays should be deleted");
    }

    [Fact]
    public async Task RetentionCleanup_KeepsRecentFiles()
    {
        // Argha - 2026-02-25 - a file written yesterday must survive the retention pass
        var recentFile = Path.Combine(_tempDir, "audit-recent.jsonl");
        await File.WriteAllTextAsync(recentFile, "{\"recent\":true}\n");
        File.SetLastWriteTimeUtc(recentFile, DateTime.UtcNow.AddDays(-1));

        using var logger = BuildLogger(retentionDays: 30);
        await logger.LogCallAsync(MakeRecord());

        File.Exists(recentFile).Should().BeTrue("file within retention window must be kept");
    }

    [Fact]
    public async Task RetentionCleanup_RunsOnlyOnce()
    {
        // Argha - 2026-02-25 - seed an old file, write twice, verify it was deleted only once
        var oldFile = Path.Combine(_tempDir, "audit-2019-06-01.jsonl");
        await File.WriteAllTextAsync(oldFile, "{\"old\":true}\n");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-999));

        using var logger = BuildLogger(retentionDays: 30);
        await logger.LogCallAsync(MakeRecord());

        // Re-create the old file after first write to confirm cleanup doesn't run again
        await File.WriteAllTextAsync(oldFile, "{\"old2\":true}\n");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-999));

        await logger.LogCallAsync(MakeRecord());

        // File should still exist because cleanup only runs once per process lifetime
        File.Exists(oldFile).Should().BeTrue("retention cleanup ran on first write only");
    }
}

// ============================================================
// McpServerHandler — audit wiring integration tests
// ============================================================
public class McpServerHandlerAuditTests
{
    // Argha - 2026-02-25 - fake tool that always succeeds, used to verify audit on happy path
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

    // Argha - 2026-02-25 - fake tool that always throws, used to verify audit on failure path
    private class FailingTool : McpServer.Tools.ITool
    {
        public string Name => "failing_tool";
        public string Description => "Always fails";
        public McpServer.Protocol.JsonSchema InputSchema => new();

        public Task<McpServer.Protocol.ToolCallResult> ExecuteAsync(
            Dictionary<string, object>? arguments,
            McpServer.Progress.IProgressReporter? progress = null,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("intentional failure");
        }
    }

    private static McpServerHandler BuildHandler(IAuditLogger auditLogger, McpServer.Tools.ITool? tool = null)
    {
        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });
        var tools = tool != null
            ? new McpServer.Tools.ITool[] { tool }
            : Array.Empty<McpServer.Tools.ITool>();

        return new McpServerHandler(
            tools: tools,
            resourceProviders: Array.Empty<McpServer.Resources.IResourceProvider>(),
            promptProviders: Array.Empty<McpServer.Prompts.IPromptProvider>(),
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            logSink: new McpLogSink(),
            // Argha - 2026-02-25 - inject mock or spy so we can assert calls
            auditLogger: auditLogger);
    }

    private static async Task InitializeAsync(McpServerHandler handler)
    {
        var msg = "{\"jsonrpc\":\"2.0\",\"id\":0,\"method\":\"initialize\"," +
                  "\"params\":{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"t\",\"version\":\"1\"}}}";
        await handler.ProcessMessageAsync(msg, CancellationToken.None);
    }

    [Fact]
    public async Task ToolCall_Success_AuditLoggerCalledWithSuccessOutcome()
    {
        var mockAudit = new Mock<IAuditLogger>();
        var handler = BuildHandler(mockAudit.Object, new SuccessTool());
        await InitializeAsync(handler);

        var msg = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"success_tool\",\"arguments\":{\"action\":\"run\"}}}";

        await handler.ProcessMessageAsync(msg, CancellationToken.None);

        mockAudit.Verify(a => a.LogCallAsync(It.Is<AuditRecord>(r =>
            r.ToolName == "success_tool" &&
            r.Outcome == "Success" &&
            r.ErrorMessage == null &&
            r.DurationMs >= 0
        )), Times.Once);
    }

    [Fact]
    public async Task ToolCall_Failure_AuditLoggerCalledWithFailureOutcome()
    {
        var mockAudit = new Mock<IAuditLogger>();
        var handler = BuildHandler(mockAudit.Object, new FailingTool());
        await InitializeAsync(handler);

        var msg = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"failing_tool\",\"arguments\":{}}}";

        await handler.ProcessMessageAsync(msg, CancellationToken.None);

        mockAudit.Verify(a => a.LogCallAsync(It.Is<AuditRecord>(r =>
            r.ToolName == "failing_tool" &&
            r.Outcome == "Failure" &&
            r.ErrorMessage == "intentional failure"
        )), Times.Once);
    }

    [Fact]
    public async Task ToolCall_ToolSuccess_CorrelationIdIsSet()
    {
        AuditRecord? captured = null;
        var mockAudit = new Mock<IAuditLogger>();
        mockAudit.Setup(a => a.LogCallAsync(It.IsAny<AuditRecord>()))
                 .Callback<AuditRecord>(r => captured = r)
                 .Returns(Task.CompletedTask);

        var handler = BuildHandler(mockAudit.Object, new SuccessTool());
        await InitializeAsync(handler);

        var msg = "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"success_tool\",\"arguments\":{}}}";

        await handler.ProcessMessageAsync(msg, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.CorrelationId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ToolCall_ActionArgument_CapturedInAuditRecord()
    {
        AuditRecord? captured = null;
        var mockAudit = new Mock<IAuditLogger>();
        mockAudit.Setup(a => a.LogCallAsync(It.IsAny<AuditRecord>()))
                 .Callback<AuditRecord>(r => captured = r)
                 .Returns(Task.CompletedTask);

        var handler = BuildHandler(mockAudit.Object, new SuccessTool());
        await InitializeAsync(handler);

        var msg = "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"success_tool\",\"arguments\":{\"action\":\"my_action\"}}}";

        await handler.ProcessMessageAsync(msg, CancellationToken.None);

        captured!.Action.Should().Be("my_action");
    }

    [Fact]
    public async Task ToolCall_AuditLoggerThrows_ResponseStillReturned()
    {
        // Argha - 2026-02-25 - if the audit logger itself throws, the tool response must still be returned
        var mockAudit = new Mock<IAuditLogger>();
        mockAudit.Setup(a => a.LogCallAsync(It.IsAny<AuditRecord>()))
                 .ThrowsAsync(new IOException("disk full"));

        var handler = BuildHandler(mockAudit.Object, new SuccessTool());
        await InitializeAsync(handler);

        var msg = "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"success_tool\",\"arguments\":{}}}";

        var response = await handler.ProcessMessageAsync(msg, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().BeNull("audit failure must not surface as a JSON-RPC error");
        response.Result.Should().NotBeNull();
    }

    [Fact]
    public async Task ToolCall_TimestampIsRecent()
    {
        AuditRecord? captured = null;
        var mockAudit = new Mock<IAuditLogger>();
        mockAudit.Setup(a => a.LogCallAsync(It.IsAny<AuditRecord>()))
                 .Callback<AuditRecord>(r => captured = r)
                 .Returns(Task.CompletedTask);

        var handler = BuildHandler(mockAudit.Object, new SuccessTool());
        await InitializeAsync(handler);

        var before = DateTimeOffset.UtcNow;
        var msg = "{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"tools/call\"," +
                  "\"params\":{\"name\":\"success_tool\",\"arguments\":{}}}";
        await handler.ProcessMessageAsync(msg, CancellationToken.None);
        var after = DateTimeOffset.UtcNow;

        captured!.Timestamp.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }
}
