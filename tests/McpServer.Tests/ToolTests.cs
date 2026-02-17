using FluentAssertions;
using McpServer.Configuration;
using McpServer.Protocol;
using McpServer.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
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
        var result = await _tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["action"] = "read",
            ["path"] = @"C:\Windows\System32\config\sam"
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
        _handler = new McpServerHandler(tools, serverSettings, logger);
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

        var message = MakeRequest("resources/list", id: 2);
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
        var tools = new ITool[]
        {
            new DateTimeTool()
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
