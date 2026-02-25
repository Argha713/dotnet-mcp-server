// Argha - 2026-02-24 - tests for prompts/list, prompts/get, BuiltInPromptProvider
using FluentAssertions;
using McpServer.Configuration;
using McpServer.Prompts;
using McpServer.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace McpServer.Tests;

// ============================================================
// BuiltInPromptProvider — unit tests
// ============================================================
public class BuiltInPromptProviderTests
{
    private readonly BuiltInPromptProvider _provider = new();

    // --- CanHandle ---

    [Fact]
    public void CanHandle_KnownPrompt_ReturnsTrue()
    {
        _provider.CanHandle("summarize_file").Should().BeTrue();
    }

    [Theory]
    [InlineData("sql_query_helper")]
    [InlineData("git_diff_review")]
    [InlineData("http_api_call")]
    [InlineData("explain_code")]
    public void CanHandle_AllKnownPrompts_ReturnTrue(string name)
    {
        _provider.CanHandle(name).Should().BeTrue();
    }

    [Fact]
    public void CanHandle_UnknownPrompt_ReturnsFalse()
    {
        _provider.CanHandle("nonexistent_prompt").Should().BeFalse();
    }

    [Fact]
    public void CanHandle_EmptyString_ReturnsFalse()
    {
        _provider.CanHandle("").Should().BeFalse();
    }

    // --- ListPromptsAsync ---

    [Fact]
    public async Task ListPromptsAsync_ReturnsAllFivePrompts()
    {
        var prompts = (await _provider.ListPromptsAsync(CancellationToken.None)).ToList();

        prompts.Should().HaveCount(5);
    }

    [Fact]
    public async Task ListPromptsAsync_AllHaveNameAndDescription()
    {
        var prompts = (await _provider.ListPromptsAsync(CancellationToken.None)).ToList();

        prompts.Should().AllSatisfy(p =>
        {
            p.Name.Should().NotBeNullOrEmpty();
            p.Description.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task ListPromptsAsync_ContainsExpectedNames()
    {
        var prompts = (await _provider.ListPromptsAsync(CancellationToken.None)).ToList();
        var names = prompts.Select(p => p.Name).ToList();

        names.Should().Contain("summarize_file")
             .And.Contain("sql_query_helper")
             .And.Contain("git_diff_review")
             .And.Contain("http_api_call")
             .And.Contain("explain_code");
    }

    [Fact]
    public async Task ListPromptsAsync_RequiredArgumentsMarkedCorrectly()
    {
        var prompts = (await _provider.ListPromptsAsync(CancellationToken.None)).ToList();

        var summarize = prompts.First(p => p.Name == "summarize_file");
        summarize.Arguments.Should().ContainSingle(a => a.Name == "path" && a.Required);

        var httpCall = prompts.First(p => p.Name == "http_api_call");
        httpCall.Arguments.First(a => a.Name == "url").Required.Should().BeTrue();
        httpCall.Arguments.First(a => a.Name == "goal").Required.Should().BeFalse();
    }

    // --- GetPromptAsync ---

    [Fact]
    public async Task GetPromptAsync_SummarizeFile_RendersPathArgument()
    {
        var args = new Dictionary<string, string> { ["path"] = "/repo/src/Program.cs" };

        var result = await _provider.GetPromptAsync("summarize_file", args, CancellationToken.None);

        result.Messages.Should().ContainSingle();
        result.Messages[0].Role.Should().Be("user");
        result.Messages[0].Content.Type.Should().Be("text");
        result.Messages[0].Content.Text.Should().Contain("/repo/src/Program.cs");
    }

    [Fact]
    public async Task GetPromptAsync_HttpApiCall_UsesDefaultGoalWhenOmitted()
    {
        var args = new Dictionary<string, string> { ["url"] = "https://api.example.com/data" };

        var result = await _provider.GetPromptAsync("http_api_call", args, CancellationToken.None);

        result.Messages.Should().ContainSingle();
        result.Messages[0].Content.Text.Should().Contain("https://api.example.com/data");
        // default goal clause should be present when goal arg is omitted
        result.Messages[0].Content.Text.Should().Contain("Summarize");
    }

    [Fact]
    public async Task GetPromptAsync_HttpApiCall_UsesProvidedGoal()
    {
        var args = new Dictionary<string, string>
        {
            ["url"] = "https://api.example.com/data",
            ["goal"] = "Extract the list of user IDs"
        };

        var result = await _provider.GetPromptAsync("http_api_call", args, CancellationToken.None);

        result.Messages[0].Content.Text.Should().Contain("Extract the list of user IDs");
    }

    [Fact]
    public async Task GetPromptAsync_ExplainCode_IncludesLanguageWhenProvided()
    {
        var args = new Dictionary<string, string>
        {
            ["path"] = "/repo/src/Tool.cs",
            ["language"] = "C#"
        };

        var result = await _provider.GetPromptAsync("explain_code", args, CancellationToken.None);

        result.Messages[0].Content.Text.Should().Contain("C#");
        result.Messages[0].Content.Text.Should().Contain("/repo/src/Tool.cs");
    }

    [Fact]
    public async Task GetPromptAsync_SqlQueryHelper_RendersDbAndQuestion()
    {
        var args = new Dictionary<string, string>
        {
            ["database"] = "production",
            ["question"] = "How many users signed up last month?"
        };

        var result = await _provider.GetPromptAsync("sql_query_helper", args, CancellationToken.None);

        result.Messages[0].Content.Text.Should().Contain("production");
        result.Messages[0].Content.Text.Should().Contain("How many users signed up last month?");
    }

    [Fact]
    public async Task GetPromptAsync_GitDiffReview_RendersRepository()
    {
        var args = new Dictionary<string, string> { ["repository"] = "/home/user/myproject" };

        var result = await _provider.GetPromptAsync("git_diff_review", args, CancellationToken.None);

        result.Messages[0].Content.Text.Should().Contain("/home/user/myproject");
    }

    [Fact]
    public async Task GetPromptAsync_UnknownPrompt_ThrowsArgumentException()
    {
        var args = new Dictionary<string, string> { ["path"] = "/some/path" };

        await _provider.Invoking(p => p.GetPromptAsync("nonexistent", args, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPromptAsync_MissingRequiredArg_ThrowsArgumentException()
    {
        // summarize_file requires "path"
        var args = new Dictionary<string, string>(); // empty

        await _provider.Invoking(p => p.GetPromptAsync("summarize_file", args, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>().WithMessage("*path*");
    }

    [Fact]
    public async Task GetPromptAsync_NullArgs_ThrowsArgumentExceptionForRequiredArg()
    {
        await _provider.Invoking(p => p.GetPromptAsync("summarize_file", null, CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPromptAsync_ResultHasDescription()
    {
        var args = new Dictionary<string, string> { ["path"] = "/repo/README.md" };

        var result = await _provider.GetPromptAsync("summarize_file", args, CancellationToken.None);

        result.Description.Should().NotBeNullOrEmpty();
    }
}

// ============================================================
// McpServerHandler — prompts/list and prompts/get routing
// ============================================================
public class PromptHandlerTests
{
    private readonly McpServerHandler _handler;

    public PromptHandlerTests()
    {
        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });
        var promptProvider = new BuiltInPromptProvider();

        _handler = new McpServerHandler(
            tools: Array.Empty<McpServer.Tools.ITool>(),
            resourceProviders: Array.Empty<McpServer.Resources.IResourceProvider>(),
            promptProviders: new McpServer.Prompts.IPromptProvider[] { promptProvider },
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            // Argha - 2026-02-24 - no-op sink; writer never initialised in unit tests
            logSink: new McpServer.Logging.McpLogSink(),
            // Argha - 2026-02-25 - Phase 6.2: no-op audit logger for unit tests
            auditLogger: McpServer.Audit.NullAuditLogger.Instance);
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

    // --- Initialize advertises Prompts capability ---

    [Fact]
    public async Task Initialize_AdvertisesPromptsCapability()
    {
        var msg = MakeRequest("initialize", paramsJson: "{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        var json = JsonSerializer.Serialize(response!.Result);
        json.Should().Contain("\"prompts\"");
    }

    // --- prompts/list ---

    [Fact]
    public async Task PromptsList_BeforeInitialize_ReturnsError()
    {
        var msg = MakeRequest("prompts/list");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Message.Should().Contain("not initialized");
    }

    [Fact]
    public async Task PromptsList_AfterInitialize_ReturnsPromptArray()
    {
        await InitializeAsync();
        var msg = MakeRequest("prompts/list");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().BeNull();
        var json = JsonSerializer.Serialize(response.Result);
        json.Should().Contain("\"prompts\"");
    }

    [Fact]
    public async Task PromptsList_ResponseContainsPromptName()
    {
        await InitializeAsync();
        var msg = MakeRequest("prompts/list");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        var json = JsonSerializer.Serialize(response!.Result);
        json.Should().Contain("summarize_file");
    }

    [Fact]
    public async Task PromptsList_ResponseContainsAllFivePrompts()
    {
        await InitializeAsync();
        var msg = MakeRequest("prompts/list");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        var json = JsonSerializer.Serialize(response!.Result);
        json.Should().Contain("sql_query_helper")
            .And.Contain("git_diff_review")
            .And.Contain("http_api_call")
            .And.Contain("explain_code");
    }

    // --- prompts/get ---

    [Fact]
    public async Task PromptsGet_ValidPrompt_ReturnsMessages()
    {
        await InitializeAsync();
        var msg = MakeRequest("prompts/get", paramsJson:
            "{\"name\":\"summarize_file\",\"arguments\":{\"path\":\"/repo/README.md\"}}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().BeNull();
        var json = JsonSerializer.Serialize(response.Result);
        json.Should().Contain("\"messages\"");
        json.Should().Contain("/repo/README.md");
    }

    [Fact]
    public async Task PromptsGet_MissingParams_ReturnsInvalidParamsError()
    {
        await InitializeAsync();
        var msg = MakeRequest("prompts/get");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32602);
    }

    [Fact]
    public async Task PromptsGet_EmptyName_ReturnsInvalidParamsError()
    {
        await InitializeAsync();
        var msg = MakeRequest("prompts/get", paramsJson: "{\"name\":\"\"}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32602);
    }

    [Fact]
    public async Task PromptsGet_UnknownPrompt_ReturnsMethodNotFoundError()
    {
        await InitializeAsync();
        var msg = MakeRequest("prompts/get", paramsJson: "{\"name\":\"nonexistent_prompt\"}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32601);
        response.Error.Message.Should().Contain("nonexistent_prompt");
    }

    [Fact]
    public async Task PromptsGet_MissingRequiredArg_ReturnsInvalidParamsError()
    {
        await InitializeAsync();
        // summarize_file requires "path" — pass no arguments
        var msg = MakeRequest("prompts/get", paramsJson: "{\"name\":\"summarize_file\",\"arguments\":{}}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32602);
    }

    [Fact]
    public async Task PromptsGet_BeforeInitialize_ReturnsError()
    {
        var msg = MakeRequest("prompts/get", paramsJson:
            "{\"name\":\"summarize_file\",\"arguments\":{\"path\":\"/file.txt\"}}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Message.Should().Contain("not initialized");
    }
}
