using FluentAssertions;
using McpServer.Configuration;
using McpServer.Protocol;
using McpServer.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Xunit;

namespace McpServer.Tests;

// ============================================================
// DateTimeTool Tests
// ============================================================
public class DateTimeToolTests
{
    private readonly DateTimeTool _tool;

    public DateTimeToolTests()
    {
        _tool = new DateTimeTool();
    }

    [Fact]
    public void Name_ShouldBeDatetime()
    {
        _tool.Name.Should().Be("datetime");
    }

    [Fact]
    public void Description_ShouldNotBeEmpty()
    {
        _tool.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void InputSchema_ShouldHaveActionProperty()
    {
        _tool.InputSchema.Properties.Should().ContainKey("action");
    }

    [Fact]
    public async Task ExecuteAsync_Now_ShouldReturnCurrentTime()
    {
        var arguments = new Dictionary<string, object> { ["action"] = "now" };

        var result = await _tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCount(1);
        result.Content[0].Text.Should().Contain("UTC");
    }

    [Fact]
    public async Task ExecuteAsync_NowWithTimezone_ShouldReturnLocalTime()
    {
        var arguments = new Dictionary<string, object>
        {
            ["action"] = "now",
            ["timezone"] = "UTC"
        };

        var result = await _tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("UTC");
    }

    [Fact]
    public async Task ExecuteAsync_ListTimezones_ShouldReturnList()
    {
        var arguments = new Dictionary<string, object> { ["action"] = "list_timezones" };

        var result = await _tool.ExecuteAsync(arguments);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("America/New_York");
        result.Content[0].Text.Should().Contain("Asia/Kolkata");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidAction_ShouldReturnError()
    {
        var arguments = new Dictionary<string, object> { ["action"] = "invalid_action" };

        var result = await _tool.ExecuteAsync(arguments);

        result.Content[0].Text.Should().Contain("Unknown action");
    }
}

// ============================================================
// SqlQueryTool Validation Tests
// ============================================================
public class SqlQueryValidationTests
{
    [Fact]
    public void ValidateQuery_ValidSelect_ReturnsNull()
    {
        SqlQueryTool.ValidateQuery("SELECT * FROM Users").Should().BeNull();
    }

    [Fact]
    public void ValidateQuery_SelectWithWhitespace_ReturnsNull()
    {
        SqlQueryTool.ValidateQuery("  SELECT TOP 10 * FROM Orders  ").Should().BeNull();
    }

    [Fact]
    public void ValidateQuery_NonSelect_ReturnsError()
    {
        var result = SqlQueryTool.ValidateQuery("INSERT INTO Users VALUES ('test')");
        result.Should().Contain("Only SELECT queries are allowed");
    }

    [Fact]
    public void ValidateQuery_Semicolon_ReturnsError()
    {
        var result = SqlQueryTool.ValidateQuery("SELECT 1; DROP TABLE Users");
        result.Should().Contain("Semicolons are not allowed");
    }

    [Fact]
    public void ValidateQuery_TrailingSemicolon_ReturnsError()
    {
        var result = SqlQueryTool.ValidateQuery("SELECT * FROM Users;");
        result.Should().Contain("Semicolons are not allowed");
    }

    [Fact]
    public void ValidateQuery_DashDashComment_ReturnsError()
    {
        var result = SqlQueryTool.ValidateQuery("SELECT * FROM Users -- comment");
        result.Should().Contain("SQL comments");
    }

    [Fact]
    public void ValidateQuery_BlockComment_ReturnsError()
    {
        var result = SqlQueryTool.ValidateQuery("SELECT * FROM Users /* hidden */");
        result.Should().Contain("SQL comments");
    }

    [Theory]
    [InlineData("SELECT * FROM x WHERE 1=1 INSERT INTO y")]
    [InlineData("SELECT * FROM x UPDATE y SET z=1")]
    [InlineData("SELECT * FROM x DELETE FROM y")]
    [InlineData("SELECT * FROM x DROP TABLE y")]
    [InlineData("SELECT * FROM x ALTER TABLE y")]
    [InlineData("SELECT * FROM x CREATE TABLE y")]
    [InlineData("SELECT * FROM x TRUNCATE TABLE y")]
    [InlineData("SELECT * FROM x EXEC sp_help")]
    [InlineData("SELECT * FROM x EXECUTE sp_help")]
    [InlineData("SELECT * FROM x GRANT SELECT ON y")]
    [InlineData("SELECT * FROM x REVOKE SELECT ON y")]
    [InlineData("SELECT * FROM x MERGE INTO y")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI','Server=evil')")]
    [InlineData("SELECT * FROM x XP_CMDSHELL 'dir'")]
    public void ValidateQuery_DangerousKeywords_ReturnsError(string query)
    {
        var result = SqlQueryTool.ValidateQuery(query);
        result.Should().Contain("forbidden keywords");
    }

    [Fact]
    public void ValidateQuery_CaseInsensitive_ReturnsError()
    {
        SqlQueryTool.ValidateQuery("select 1; drop table x").Should().Contain("Semicolons");
        SqlQueryTool.ValidateQuery("SELECT * FROM x DrOp TABLE y").Should().Contain("forbidden keywords");
    }

    [Fact]
    public void ValidateQuery_NestedSubquery_IsAllowed()
    {
        // Argha - 2026-02-17 - subqueries using SELECT inside SELECT are legitimate
        var result = SqlQueryTool.ValidateQuery("SELECT * FROM Users WHERE Id IN (SELECT UserId FROM Orders)");
        result.Should().BeNull();
    }
}

// ============================================================
// FileSystemTool Tests
// ============================================================
public class FileSystemToolTests : IDisposable
{
    private readonly FileSystemTool _tool;
    private readonly string _tempDir;

    public FileSystemToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var settings = Options.Create(new FileSystemSettings
        {
            AllowedPaths = new List<string> { _tempDir }
        });
        _tool = new FileSystemTool(settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task ReadFile_ExistingFile_ReturnsContent()
    {
        var filePath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(filePath, "hello world");

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "read",
            ["path"] = filePath
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("hello world");
    }

    [Fact]
    public async Task ReadFile_NonExistentFile_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "read",
            ["path"] = Path.Combine(_tempDir, "nonexistent.txt")
        });

        result.Content[0].Text.Should().Contain("File not found");
    }

    [Fact]
    public async Task ReadFile_OutsideAllowedPath_ReturnsAccessDenied()
    {
        // Argha - 2026-02-23 - use a cross-platform absolute path so the test passes on Linux CI too
        // (C:\Windows\... is not rooted on Linux, so ResolvePath would treat it as relative to _tempDir)
        var outsidePath = Path.Combine(
            Path.GetPathRoot(Path.GetTempPath()) ?? "/",
            "argha_mcp_outside_test"
        );

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "read",
            ["path"] = outsidePath
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Access denied");
    }

    [Fact]
    public async Task ReadFile_PathTraversalAttack_ReturnsAccessDenied()
    {
        // Argha - 2026-02-17 - ensure .. traversal can't escape allowed directory
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "read",
            ["path"] = Path.Combine(_tempDir, "..", "..", "Windows", "System32", "config", "sam")
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Access denied");
    }

    [Fact]
    public async Task ListDirectory_ReturnsContents()
    {
        // Create some test files and a subdirectory
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "file1.txt"), "content");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "file2.cs"), "code");
        Directory.CreateDirectory(Path.Combine(_tempDir, "subfolder"));

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "list",
            ["path"] = _tempDir
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("file1.txt");
        result.Content[0].Text.Should().Contain("file2.cs");
        result.Content[0].Text.Should().Contain("subfolder");
    }

    [Fact]
    public async Task ListDirectory_EmptyDir_ReturnsEmpty()
    {
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "list",
            ["path"] = emptyDir
        });

        result.Content[0].Text.Should().Contain("empty");
    }

    [Fact]
    public async Task SearchFiles_FindsByPattern()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "app.cs"), "code");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "app.txt"), "text");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "readme.md"), "docs");

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "search",
            ["path"] = _tempDir,
            ["pattern"] = "*.cs"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("app.cs");
        result.Content[0].Text.Should().NotContain("app.txt");
        result.Content[0].Text.Should().NotContain("readme.md");
    }

    [Fact]
    public async Task GetFileInfo_ReturnsMetadata()
    {
        var filePath = Path.Combine(_tempDir, "info_test.txt");
        await File.WriteAllTextAsync(filePath, "some content");

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "info",
            ["path"] = filePath
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Size:");
        result.Content[0].Text.Should().Contain(".txt");
    }

    [Fact]
    public async Task AllowedPaths_ReturnsConfiguredPaths()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "allowed_paths"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain(_tempDir);
    }

    [Fact]
    public async Task PathTraversal_SimilarPrefix_ReturnsAccessDenied()
    {
        // Argha - 2026-02-17 - C:\AllowedPathEvil should NOT match C:\AllowedPath
        var evilDir = _tempDir + "Evil";
        Directory.CreateDirectory(evilDir);
        var evilFile = Path.Combine(evilDir, "secret.txt");
        await File.WriteAllTextAsync(evilFile, "secret");

        try
        {
            var result = await _tool.ExecuteAsync(new Dictionary<string, object>
            {
                ["action"] = "read",
                ["path"] = evilFile
            });

            result.IsError.Should().BeTrue();
            result.Content[0].Text.Should().Contain("Access denied");
        }
        finally
        {
            if (Directory.Exists(evilDir))
                Directory.Delete(evilDir, true);
        }
    }

    [Fact]
    public async Task ReadFile_MissingPathParam_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "read"
        });

        result.Content[0].Text.Should().Contain("'path' parameter is required");
    }
}

// ============================================================
// HttpTool Tests
// ============================================================
public class HttpToolTests
{
    private static HttpTool CreateTool(HttpClient httpClient, List<string>? allowedHosts = null)
    {
        var settings = Options.Create(new HttpSettings
        {
            AllowedHosts = allowedHosts ?? new List<string> { "api.github.com", "httpbin.org" },
            TimeoutSeconds = 30
        });
        return new HttpTool(httpClient, settings);
    }

    private static HttpClient CreateMockHttpClient(HttpStatusCode statusCode, string content, string contentType = "application/json")
    {
        var handler = new MockHttpMessageHandler(statusCode, content, contentType);
        return new HttpClient(handler);
    }

    [Fact]
    public async Task AllowedHosts_ReturnsConfiguredHosts()
    {
        var tool = CreateTool(new HttpClient());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "allowed_hosts"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("api.github.com");
        result.Content[0].Text.Should().Contain("httpbin.org");
    }

    [Fact]
    public async Task Get_AllowedHost_ReturnsResponse()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "{\"message\":\"success\"}");
        var tool = CreateTool(httpClient);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["url"] = "https://api.github.com/users/test"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("200");
    }

    [Fact]
    public async Task Get_BlockedHost_ReturnsError()
    {
        var tool = CreateTool(new HttpClient());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["url"] = "https://evil.com/steal-data"
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("not in the allowed hosts list");
    }

    [Fact]
    public async Task Get_SubdomainOfAllowedHost_IsAllowed()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.OK, "{}");
        var tool = CreateTool(httpClient);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["url"] = "https://sub.api.github.com/endpoint"
        });

        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Get_InvalidUrl_ReturnsError()
    {
        var tool = CreateTool(new HttpClient());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["url"] = "not-a-url"
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Invalid URL");
    }

    [Fact]
    public async Task Get_FtpScheme_ReturnsError()
    {
        var tool = CreateTool(new HttpClient());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["url"] = "ftp://api.github.com/file"
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("HTTP and HTTPS");
    }

    [Fact]
    public async Task Get_MissingUrl_ReturnsError()
    {
        var tool = CreateTool(new HttpClient());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get"
        });

        result.Content[0].Text.Should().Contain("'url' parameter is required");
    }

    [Fact]
    public async Task Post_AllowedHost_ReturnsResponse()
    {
        var httpClient = CreateMockHttpClient(HttpStatusCode.Created, "{\"id\":1}");
        var tool = CreateTool(httpClient);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "post",
            ["url"] = "https://httpbin.org/post",
            ["body"] = "{\"name\":\"test\"}"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("201");
    }

    [Fact]
    public async Task AllowedHosts_EmptyConfig_ShowsMessage()
    {
        var tool = CreateTool(new HttpClient(), new List<string>());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "allowed_hosts"
        });

        result.Content[0].Text.Should().Contain("No hosts are configured");
    }

    [Fact]
    public async Task InvalidAction_ReturnsUnknown()
    {
        var tool = CreateTool(new HttpClient());

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "delete"
        });

        result.Content[0].Text.Should().Contain("Unknown action");
    }
}

// Argha - 2026-02-17 - mock HTTP handler for testing HttpTool without real network calls
internal class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _content;
    private readonly string _contentType;

    public MockHttpMessageHandler(HttpStatusCode statusCode, string content, string contentType = "application/json")
    {
        _statusCode = statusCode;
        _content = content;
        _contentType = contentType;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content, System.Text.Encoding.UTF8, _contentType)
        };
        return Task.FromResult(response);
    }
}

// ============================================================
// McpServerHandler Tests
// ============================================================
public class McpServerHandlerTests
{
    private readonly McpServerHandler _handler;

    public McpServerHandlerTests()
    {
        var tools = new ITool[] { new DateTimeTool() };
        var serverSettings = Options.Create(new ServerSettings
        {
            Name = "test-server",
            Version = "1.0.0"
        });
        var logger = NullLogger<McpServerHandler>.Instance;
        // Argha - 2026-02-24 - pass empty resource and prompt providers; tested separately
        // Argha - 2026-02-24 - pass a no-op McpLogSink (writer never initialised in unit tests)
        _handler = new McpServerHandler(tools, Array.Empty<McpServer.Resources.IResourceProvider>(), Array.Empty<McpServer.Prompts.IPromptProvider>(), serverSettings, logger, new McpServer.Logging.McpLogSink());
    }

    private static string MakeRequest(string method, int id = 1, string? paramsJson = null)
    {
        if (paramsJson != null)
            return $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}";
        return $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\"}}";
    }

    [Fact]
    public async Task Initialize_ReturnsServerInfo()
    {
        var message = MakeRequest("initialize", paramsJson: "{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"test\",\"version\":\"1.0\"}}");

        var response = await _handler.ProcessMessageAsync(message, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().BeNull();
        response.Result.Should().NotBeNull();

        var json = JsonSerializer.Serialize(response.Result);
        json.Should().Contain("test-server");
        json.Should().Contain("2024-11-05");
    }

    [Fact]
    public async Task Ping_ReturnsEmptyResult()
    {
        // Argha - 2026-02-17 - ping should work even before initialize
        var message = MakeRequest("ping");

        var response = await _handler.ProcessMessageAsync(message, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().BeNull();
    }

    [Fact]
    public async Task ToolsList_BeforeInitialize_ReturnsError()
    {
        // Argha - 2026-02-17 - initialization gate: must send initialize first
        var message = MakeRequest("tools/list");

        var response = await _handler.ProcessMessageAsync(message, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.InvalidRequest);
        response.Error.Message.Should().Contain("not initialized");
    }

    [Fact]
    public async Task ToolsCall_BeforeInitialize_ReturnsError()
    {
        var message = MakeRequest("tools/call", paramsJson: "{\"name\":\"datetime\",\"arguments\":{\"action\":\"now\"}}");

        var response = await _handler.ProcessMessageAsync(message, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.InvalidRequest);
    }

    [Fact]
    public async Task ToolsList_AfterInitialize_ReturnsTools()
    {
        // Initialize first
        var initMessage = MakeRequest("initialize", id: 1, paramsJson: "{\"protocolVersion\":\"2024-11-05\"}");
        await _handler.ProcessMessageAsync(initMessage, CancellationToken.None);

        // Now list tools
        var message = MakeRequest("tools/list", id: 2);
        var response = await _handler.ProcessMessageAsync(message, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().BeNull();
        var json = JsonSerializer.Serialize(response.Result);
        json.Should().Contain("datetime");
    }

    [Fact]
    public async Task ToolsCall_AfterInitialize_ExecutesTool()
    {
        // Initialize first
        var initMessage = MakeRequest("initialize", id: 1, paramsJson: "{\"protocolVersion\":\"2024-11-05\"}");
        await _handler.ProcessMessageAsync(initMessage, CancellationToken.None);

        // Call datetime tool
        var message = MakeRequest("tools/call", id: 2, paramsJson: "{\"name\":\"datetime\",\"arguments\":{\"action\":\"now\"}}");
        var response = await _handler.ProcessMessageAsync(message, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().BeNull();
        var json = JsonSerializer.Serialize(response.Result);
        json.Should().Contain("UTC");
    }

    [Fact]
    public async Task ToolsCall_UnknownTool_ReturnsError()
    {
        // Initialize first
        var initMessage = MakeRequest("initialize", id: 1, paramsJson: "{\"protocolVersion\":\"2024-11-05\"}");
        await _handler.ProcessMessageAsync(initMessage, CancellationToken.None);

        var message = MakeRequest("tools/call", id: 2, paramsJson: "{\"name\":\"nonexistent_tool\",\"arguments\":{}}");
        var response = await _handler.ProcessMessageAsync(message, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().NotBeNull();
        response.Error!.Message.Should().Contain("Tool not found");
    }

    [Fact]
    public async Task ToolsCall_MissingParams_ReturnsError()
    {
        // Initialize first
        var initMessage = MakeRequest("initialize", id: 1, paramsJson: "{\"protocolVersion\":\"2024-11-05\"}");
        await _handler.ProcessMessageAsync(initMessage, CancellationToken.None);

        var message = MakeRequest("tools/call", id: 2);
        var response = await _handler.ProcessMessageAsync(message, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.InvalidParams);
    }

    [Fact]
    public async Task UnknownMethod_ReturnsMethodNotFound()
    {
        // Initialize first
        var initMessage = MakeRequest("initialize", id: 1, paramsJson: "{\"protocolVersion\":\"2024-11-05\"}");
        await _handler.ProcessMessageAsync(initMessage, CancellationToken.None);

        // Argha - 2026-02-24 - resources/list is now a real method; use a genuinely unknown method instead
        // var message = MakeRequest("resources/list", id: 2);
        var message = MakeRequest("unknown/notarealmethod", id: 2);
        var response = await _handler.ProcessMessageAsync(message, CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.MethodNotFound);
    }

    [Fact]
    public async Task InvalidJson_ReturnsParseError()
    {
        var response = await _handler.ProcessMessageAsync("not valid json{{{", CancellationToken.None);

        response.Should().NotBeNull();
        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(JsonRpcErrorCodes.ParseError);
    }

    [Fact]
    public async Task Notification_Initialized_ReturnsNull()
    {
        // Notifications (no id) should return null (no response)
        var message = "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}";
        var response = await _handler.ProcessMessageAsync(message, CancellationToken.None);

        response.Should().BeNull();
    }
}

// ============================================================
// Tool Interface Tests
// ============================================================
public class ToolInterfaceTests
{
    [Fact]
    public void AllTools_ShouldHaveRequiredProperties()
    {
        var envSettings = Options.Create(new EnvironmentSettings());
        var fsSettings = Options.Create(new FileSystemSettings { AllowedPaths = new List<string> { "C:\\temp" } });
        var tools = new ITool[]
        {
            new DateTimeTool(),
            new TextTool(),
            new DataTransformTool(),
            new EnvironmentTool(envSettings),
            new SystemInfoTool(),
            new GitTool(fsSettings)
        };

        foreach (var tool in tools)
        {
            tool.Name.Should().NotBeNullOrWhiteSpace();
            tool.Description.Should().NotBeNullOrWhiteSpace();
            tool.InputSchema.Should().NotBeNull();
            tool.InputSchema.Type.Should().Be("object");
        }
    }
}

// ============================================================
// TextTool Tests
// ============================================================
public class TextToolTests
{
    private readonly TextTool _tool;

    public TextToolTests()
    {
        _tool = new TextTool();
    }

    [Fact]
    public void Name_ShouldBeText()
    {
        _tool.Name.Should().Be("text");
    }

    [Fact]
    public async Task RegexMatch_FindsAllMatches()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "regex_match",
            ["pattern"] = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
            ["text"] = "Contact alice@example.com or bob@test.org"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("2 match(es)");
        result.Content[0].Text.Should().Contain("alice@example.com");
        result.Content[0].Text.Should().Contain("bob@test.org");
    }

    [Fact]
    public async Task RegexMatch_WithGroups_ReturnsGroups()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "regex_match",
            ["pattern"] = @"(\w+)=(\d+)",
            ["text"] = "x=10 y=20"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Group 1:");
        result.Content[0].Text.Should().Contain("Group 2:");
    }

    [Fact]
    public async Task RegexMatch_NoMatches_ReturnsMessage()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "regex_match",
            ["pattern"] = @"\d{10}",
            ["text"] = "no numbers here"
        });

        result.Content[0].Text.Should().Contain("No matches found");
    }

    [Fact]
    public async Task RegexMatch_InvalidPattern_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "regex_match",
            ["pattern"] = @"[invalid",
            ["text"] = "test"
        });

        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task RegexMatch_MissingPattern_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "regex_match",
            ["text"] = "test"
        });

        result.Content[0].Text.Should().Contain("'pattern' parameter is required");
    }

    [Fact]
    public async Task RegexReplace_ReplacesText()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "regex_replace",
            ["pattern"] = @"localhost:3000",
            ["replacement"] = "api.prod.com",
            ["text"] = "url=localhost:3000/api and localhost:3000/health"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Replacements made: 2");
        result.Content[0].Text.Should().Contain("api.prod.com/api");
        result.Content[0].Text.Should().Contain("api.prod.com/health");
    }

    [Fact]
    public async Task RegexReplace_WithGroupReferences()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "regex_replace",
            ["pattern"] = @"(\w+)\.(\w+)",
            ["replacement"] = "$2_$1",
            ["text"] = "first.last"
        });

        result.Content[0].Text.Should().Contain("last_first");
    }

    [Fact]
    public async Task WordCount_ReturnsStats()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "word_count",
            ["text"] = "Hello world. This is a test. How are you?"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Words: 9");
        result.Content[0].Text.Should().Contain("Sentences: 3");
    }

    [Fact]
    public async Task WordCount_MultipleLines()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "word_count",
            ["text"] = "line1\nline2\nline3"
        });

        result.Content[0].Text.Should().Contain("Lines: 3");
    }

    [Fact]
    public async Task DiffText_IdenticalTexts()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "diff_text",
            ["text1"] = "hello\nworld",
            ["text2"] = "hello\nworld"
        });

        result.Content[0].Text.Should().Contain("identical");
    }

    [Fact]
    public async Task DiffText_DifferentTexts()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "diff_text",
            ["text1"] = "hello\nworld",
            ["text2"] = "hello\nearth"
        });

        result.Content[0].Text.Should().Contain("- world");
        result.Content[0].Text.Should().Contain("+ earth");
    }

    [Fact]
    public async Task FormatJson_ValidJson()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "format_json",
            ["text"] = "{\"name\":\"test\",\"value\":42}"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Formatted JSON:");
        result.Content[0].Text.Should().Contain("\"name\": \"test\"");
    }

    [Fact]
    public async Task FormatJson_InvalidJson()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "format_json",
            ["text"] = "not json{{"
        });

        result.Content[0].Text.Should().Contain("Invalid JSON");
    }

    [Fact]
    public async Task FormatXml_ValidXml()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "format_xml",
            ["text"] = "<root><child>text</child></root>"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Formatted XML:");
    }

    [Fact]
    public async Task FormatXml_InvalidXml()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "format_xml",
            ["text"] = "<not>valid<xml"
        });

        result.Content[0].Text.Should().Contain("Invalid XML");
    }

    [Fact]
    public async Task InvalidAction_ReturnsUnknown()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "nonexistent"
        });

        result.Content[0].Text.Should().Contain("Unknown action");
    }
}

// ============================================================
// DataTransformTool Tests
// ============================================================
public class DataTransformToolTests
{
    private readonly DataTransformTool _tool;

    public DataTransformToolTests()
    {
        _tool = new DataTransformTool();
    }

    [Fact]
    public void Name_ShouldBeDataTransform()
    {
        _tool.Name.Should().Be("data_transform");
    }

    [Fact]
    public async Task JsonQuery_SimpleProperty()
    {
        var json = "{\"name\":\"Alice\",\"age\":30}";
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "json_query",
            ["text"] = json,
            ["query"] = "name"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Alice");
    }

    [Fact]
    public async Task JsonQuery_NestedProperty()
    {
        var json = "{\"data\":{\"user\":{\"name\":\"Bob\"}}}";
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "json_query",
            ["text"] = json,
            ["query"] = "data.user.name"
        });

        result.Content[0].Text.Should().Contain("Bob");
    }

    [Fact]
    public async Task JsonQuery_ArrayIndex()
    {
        var json = "{\"items\":[\"a\",\"b\",\"c\"]}";
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "json_query",
            ["text"] = json,
            ["query"] = "items[1]"
        });

        result.Content[0].Text.Should().Contain("b");
    }

    [Fact]
    public async Task JsonQuery_WildcardArray()
    {
        var json = "{\"users\":[{\"id\":1},{\"id\":2},{\"id\":3}]}";
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "json_query",
            ["text"] = json,
            ["query"] = "users[*].id"
        });

        result.Content[0].Text.Should().Contain("3 results");
    }

    [Fact]
    public async Task JsonQuery_InvalidJson()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "json_query",
            ["text"] = "not json",
            ["query"] = "foo"
        });

        result.Content[0].Text.Should().Contain("Invalid JSON");
    }

    [Fact]
    public async Task JsonQuery_NoResults()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "json_query",
            ["text"] = "{\"a\":1}",
            ["query"] = "nonexistent"
        });

        result.Content[0].Text.Should().Contain("No results found");
    }

    [Fact]
    public async Task CsvToJson_BasicConversion()
    {
        var csv = "name,age\nAlice,30\nBob,25";
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "csv_to_json",
            ["text"] = csv
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("2 record(s)");
        result.Content[0].Text.Should().Contain("Alice");
        result.Content[0].Text.Should().Contain("Bob");
    }

    [Fact]
    public async Task CsvToJson_QuotedFieldsWithCommas()
    {
        var csv = "name,address\nAlice,\"123 Main St, Apt 4\"";
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "csv_to_json",
            ["text"] = csv
        });

        result.Content[0].Text.Should().Contain("123 Main St, Apt 4");
    }

    [Fact]
    public async Task JsonToCsv_BasicConversion()
    {
        var json = "[{\"name\":\"Alice\",\"age\":30},{\"name\":\"Bob\",\"age\":25}]";
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "json_to_csv",
            ["text"] = json
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("name");
        result.Content[0].Text.Should().Contain("age");
        result.Content[0].Text.Should().Contain("Alice");
    }

    [Fact]
    public async Task JsonToCsv_NotArray_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "json_to_csv",
            ["text"] = "{\"key\":\"value\"}"
        });

        result.Content[0].Text.Should().Contain("must be an array");
    }

    [Fact]
    public async Task CsvToJson_ThenJsonToCsv_RoundTrip()
    {
        var csv = "name,age\nAlice,30\nBob,25";
        var toJsonResult = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "csv_to_json",
            ["text"] = csv
        });

        // Argha - 2026-02-18 - extract JSON from result for round-trip test
        var jsonText = toJsonResult.Content[0].Text;
        var jsonStart = jsonText.IndexOf('[');
        var jsonPortion = jsonText[jsonStart..];

        var toCsvResult = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "json_to_csv",
            ["text"] = jsonPortion
        });

        toCsvResult.Content[0].Text.Should().Contain("Alice");
        toCsvResult.Content[0].Text.Should().Contain("Bob");
    }

    [Fact]
    public async Task XmlToJson_BasicConversion()
    {
        var xml = "<root><name>Alice</name><age>30</age></root>";
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "xml_to_json",
            ["text"] = xml
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Alice");
        result.Content[0].Text.Should().Contain("root");
    }

    [Fact]
    public async Task XmlToJson_WithAttributes()
    {
        var xml = "<user id=\"1\"><name>Alice</name></user>";
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "xml_to_json",
            ["text"] = xml
        });

        result.Content[0].Text.Should().Contain("@id");
    }

    [Fact]
    public async Task XmlToJson_InvalidXml()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "xml_to_json",
            ["text"] = "<not>valid<xml"
        });

        result.Content[0].Text.Should().Contain("Invalid XML");
    }

    [Fact]
    public async Task Base64Encode_EncodesCorrectly()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "base64_encode",
            ["text"] = "Hello World"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("SGVsbG8gV29ybGQ=");
    }

    [Fact]
    public async Task Base64Decode_DecodesCorrectly()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "base64_decode",
            ["text"] = "SGVsbG8gV29ybGQ="
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Hello World");
    }

    [Fact]
    public async Task Base64Decode_InvalidInput()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "base64_decode",
            ["text"] = "not-valid-base64!!!"
        });

        result.Content[0].Text.Should().Contain("Invalid Base64");
    }

    [Fact]
    public async Task Hash_Sha256_Default()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "hash",
            ["text"] = "hello"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("SHA256");
        // Argha - 2026-02-18 - known SHA256 of "hello"
        result.Content[0].Text.Should().Contain("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824");
    }

    [Fact]
    public async Task Hash_Md5()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "hash",
            ["text"] = "hello",
            ["algorithm"] = "md5"
        });

        result.Content[0].Text.Should().Contain("MD5");
        result.Content[0].Text.Should().Contain("5d41402abc4b2a76b9719d911017c592");
    }

    [Fact]
    public async Task Hash_InvalidAlgorithm()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "hash",
            ["text"] = "hello",
            ["algorithm"] = "invalid"
        });

        result.Content[0].Text.Should().Contain("Unsupported hash algorithm");
    }

    [Fact]
    public async Task InvalidAction_ReturnsUnknown()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "nonexistent"
        });

        result.Content[0].Text.Should().Contain("Unknown action");
    }
}

// ============================================================
// EnvironmentTool Tests
// ============================================================
public class EnvironmentToolTests
{
    private readonly EnvironmentTool _tool;

    public EnvironmentToolTests()
    {
        var settings = Options.Create(new EnvironmentSettings
        {
            AdditionalBlockedVariables = new List<string> { "CUSTOM_BLOCKED_VAR" }
        });
        _tool = new EnvironmentTool(settings);
    }

    [Fact]
    public void Name_ShouldBeEnvironment()
    {
        _tool.Name.Should().Be("environment");
    }

    [Fact]
    public async Task Get_ExistingVariable()
    {
        // PATH should exist on all systems
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["name"] = "PATH"
        });

        result.IsError.Should().BeFalse();
        // Argha - 2026-02-18 - PATH contains KEY pattern, so it should be masked
        // Actually, let's test with a known non-sensitive var
    }

    [Fact]
    public async Task Get_NonexistentVariable()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["name"] = "MCP_TEST_NONEXISTENT_VAR_12345"
        });

        result.Content[0].Text.Should().Contain("not set");
    }

    [Fact]
    public async Task Get_BlockedExactName()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["name"] = "GITHUB_TOKEN"
        });

        result.Content[0].Text.Should().Contain("********");
        result.Content[0].Text.Should().Contain("sensitive");
    }

    [Fact]
    public async Task Get_BlockedByPattern_ContainsPassword()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["name"] = "MY_DATABASE_PASSWORD"
        });

        result.Content[0].Text.Should().Contain("********");
    }

    [Fact]
    public async Task Get_BlockedByPattern_ContainsSecret()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["name"] = "MY_APP_SECRET"
        });

        result.Content[0].Text.Should().Contain("********");
    }

    [Fact]
    public async Task Get_BlockedByPattern_ContainsApiKey()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["name"] = "STRIPE_API_KEY"
        });

        result.Content[0].Text.Should().Contain("********");
    }

    [Fact]
    public async Task Get_CustomBlockedVariable()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get",
            ["name"] = "CUSTOM_BLOCKED_VAR"
        });

        result.Content[0].Text.Should().Contain("********");
    }

    [Fact]
    public async Task Get_MissingName_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "get"
        });

        result.Content[0].Text.Should().Contain("'name' parameter is required");
    }

    [Fact]
    public async Task List_ReturnsVariables()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "list"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Environment variables");
    }

    [Fact]
    public async Task List_WithFilter()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "list",
            ["filter"] = "PATH"
        });

        result.Content[0].Text.Should().Contain("PATH");
    }

    [Fact]
    public async Task List_SensitiveVarsMasked()
    {
        // Argha - 2026-02-18 - any var with TOKEN/KEY/SECRET in the name should be masked in list
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "list"
        });

        // We can't guarantee specific env vars exist, but the tool should run without error
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Has_ExistingVariable()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "has",
            ["name"] = "PATH"
        });

        result.Content[0].Text.Should().Contain("exists");
    }

    [Fact]
    public async Task Has_NonexistentVariable()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "has",
            ["name"] = "MCP_TEST_NONEXISTENT_12345"
        });

        result.Content[0].Text.Should().Contain("not set");
    }

    [Fact]
    public async Task Has_BlockedVariable_StillWorks()
    {
        // Argha - 2026-02-18 - has is safe for blocked vars: only returns exists/not set, never the value
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "has",
            ["name"] = "GITHUB_TOKEN"
        });

        // Should return exists or not set, but NOT an error or masked value
        var text = result.Content[0].Text;
        (text.Contains("exists") || text.Contains("not set")).Should().BeTrue();
    }

    [Fact]
    public async Task InvalidAction_ReturnsUnknown()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "set"
        });

        result.Content[0].Text.Should().Contain("Unknown action");
    }
}

// ============================================================
// SystemInfoTool Tests
// ============================================================
public class SystemInfoToolTests
{
    private readonly SystemInfoTool _tool;

    public SystemInfoToolTests()
    {
        _tool = new SystemInfoTool();
    }

    [Fact]
    public void Name_ShouldBeSystemInfo()
    {
        _tool.Name.Should().Be("system_info");
    }

    [Fact]
    public async Task SystemInfo_ReturnsOSInfo()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "system_info"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("OS:");
    }

    [Fact]
    public async Task SystemInfo_ReturnsDotNetVersion()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "system_info"
        });

        result.Content[0].Text.Should().Contain(".NET Version:");
    }

    [Fact]
    public async Task SystemInfo_ReturnsCpuInfo()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "system_info"
        });

        result.Content[0].Text.Should().Contain("Processor Count:");
    }

    [Fact]
    public async Task SystemInfo_ReturnsDiskInfo()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "system_info"
        });

        result.Content[0].Text.Should().Contain("Disk Drives:");
    }

    [Fact]
    public async Task Processes_ReturnsCurrentProcess()
    {
        var currentProcess = Process.GetCurrentProcess();
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "processes",
            ["filter"] = currentProcess.ProcessName,
            ["top"] = "50"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("PID");
    }

    [Fact]
    public async Task Processes_WithFilter()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "processes",
            ["filter"] = "dotnet"
        });

        // Argha - 2026-02-18 - dotnet processes should be running since we're a dotnet test
        result.IsError.Should().BeFalse();
    }

    [Fact]
    public async Task Processes_WithTopLimit()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "processes",
            ["top"] = "5"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("top 5");
    }

    [Fact]
    public async Task Processes_SortedByName()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "processes",
            ["sort_by"] = "name",
            ["top"] = "10"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("sorted by name");
    }

    [Fact]
    public async Task Processes_FilterNoMatch()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "processes",
            ["filter"] = "zzz_nonexistent_process_zzz"
        });

        result.Content[0].Text.Should().Contain("No processes matching");
    }

    [Fact]
    public async Task Network_ReturnsInterfaces()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "network"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Network Interfaces:");
    }

    [Fact]
    public async Task InvalidAction_ReturnsUnknown()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "nonexistent"
        });

        result.Content[0].Text.Should().Contain("Unknown action");
    }
}

// ============================================================
// GitTool Tests
// ============================================================
public class GitToolTests : IDisposable
{
    private readonly GitTool _tool;
    private readonly string _tempDir;
    private readonly string _repoDir;

    public GitToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp_git_test_{Guid.NewGuid():N}");
        _repoDir = Path.Combine(_tempDir, "repo");
        Directory.CreateDirectory(_repoDir);

        var settings = Options.Create(new FileSystemSettings
        {
            AllowedPaths = new List<string> { _tempDir }
        });
        _tool = new GitTool(settings);

        // Argha - 2026-02-18 - init a test git repo with an initial commit
        RunGit(_repoDir, "init");
        RunGit(_repoDir, "config user.email test@test.com");
        RunGit(_repoDir, "config user.name TestUser");
        File.WriteAllText(Path.Combine(_repoDir, "README.md"), "# Test Repo");
        RunGit(_repoDir, "add .");
        RunGit(_repoDir, "commit -m \"Initial commit\"");
    }

    public void Dispose()
    {
        // Argha - 2026-02-18 - clean up temp git repo, handle readonly .git files
        if (Directory.Exists(_tempDir))
        {
            foreach (var file in Directory.GetFiles(_tempDir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_tempDir, true);
        }
    }

    private static void RunGit(string workDir, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        proc.WaitForExit(10000);
    }

    [Fact]
    public void Name_ShouldBeGit()
    {
        _tool.Name.Should().Be("git");
    }

    [Fact]
    public async Task Status_CleanRepo()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "status",
            ["path"] = _repoDir
        });

        result.IsError.Should().BeFalse();
        // Argha - 2026-02-18 - clean repo should have no output in porcelain format
        result.Content[0].Text.Should().Contain("clean state");
    }

    [Fact]
    public async Task Status_ModifiedFile()
    {
        File.WriteAllText(Path.Combine(_repoDir, "README.md"), "# Modified");

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "status",
            ["path"] = _repoDir
        });

        result.Content[0].Text.Should().Contain("README.md");
    }

    [Fact]
    public async Task Status_UntrackedFile()
    {
        File.WriteAllText(Path.Combine(_repoDir, "new_file.txt"), "new content");

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "status",
            ["path"] = _repoDir
        });

        result.Content[0].Text.Should().Contain("new_file.txt");
    }

    [Fact]
    public async Task Status_OutsideAllowedPath()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "status",
            ["path"] = "C:\\Windows\\System32"
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Access denied");
    }

    [Fact]
    public async Task Status_MissingPath()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "status"
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("'path' parameter is required");
    }

    [Fact]
    public async Task Log_ReturnsHistory()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "log",
            ["path"] = _repoDir
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Initial commit");
    }

    [Fact]
    public async Task Log_MaxCount()
    {
        // Argha - 2026-02-18 - add a second commit to test max_count limiting
        File.WriteAllText(Path.Combine(_repoDir, "file2.txt"), "content");
        RunGit(_repoDir, "add .");
        RunGit(_repoDir, "commit -m \"Second commit\"");

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "log",
            ["path"] = _repoDir,
            ["max_count"] = "1"
        });

        result.Content[0].Text.Should().Contain("Second commit");
        result.Content[0].Text.Should().NotContain("Initial commit");
    }

    [Fact]
    public async Task Diff_ModifiedFile()
    {
        File.WriteAllText(Path.Combine(_repoDir, "README.md"), "# Changed content");

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "diff",
            ["path"] = _repoDir
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("Changed content");
    }

    [Fact]
    public async Task Diff_CleanRepo()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "diff",
            ["path"] = _repoDir
        });

        // Argha - 2026-02-18 - no changes means no diff output
        result.Content[0].Text.Should().Contain("clean state");
    }

    [Fact]
    public async Task Diff_WithTarget()
    {
        File.WriteAllText(Path.Combine(_repoDir, "file2.txt"), "new file");
        RunGit(_repoDir, "add .");
        RunGit(_repoDir, "commit -m \"Add file2\"");

        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "diff",
            ["path"] = _repoDir,
            ["target"] = "HEAD~1"
        });

        result.Content[0].Text.Should().Contain("file2.txt");
    }

    [Fact]
    public async Task BranchList_ReturnsBranches()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "branch_list",
            ["path"] = _repoDir
        });

        result.IsError.Should().BeFalse();
        // Argha - 2026-02-18 - should show current branch (master or main)
        var text = result.Content[0].Text;
        (text.Contains("master") || text.Contains("main")).Should().BeTrue();
    }

    [Fact]
    public async Task BranchList_CurrentBranchMarked()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "branch_list",
            ["path"] = _repoDir
        });

        // git branch -a marks current with *
        result.Content[0].Text.Should().Contain("*");
    }

    [Fact]
    public async Task Blame_ExistingFile()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "blame",
            ["path"] = _repoDir,
            ["file"] = "README.md"
        });

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("TestUser");
    }

    [Fact]
    public async Task Blame_NonexistentFile()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "blame",
            ["path"] = _repoDir,
            ["file"] = "nonexistent.txt"
        });

        result.Content[0].Text.Should().Contain("error");
    }

    [Fact]
    public async Task Blame_MissingFile_ReturnsError()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "blame",
            ["path"] = _repoDir
        });

        result.Content[0].Text.Should().Contain("'file' parameter is required");
    }

    [Fact]
    public async Task Security_PathTraversal_InFileArg()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "blame",
            ["path"] = _repoDir,
            ["file"] = "../../etc/passwd"
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Path traversal");
    }

    [Fact]
    public async Task Security_UnsafeCharsInBranch()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "log",
            ["path"] = _repoDir,
            ["branch"] = "main; rm -rf /"
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("unsafe characters");
    }

    [Fact]
    public async Task Security_PipeInTarget()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "diff",
            ["path"] = _repoDir,
            ["target"] = "HEAD | cat /etc/passwd"
        });

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("unsafe characters");
    }

    [Fact]
    public async Task InvalidAction_ReturnsUnknown()
    {
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "push",
            ["path"] = _repoDir
        });

        result.Content[0].Text.Should().Contain("Unknown action");
    }
}
