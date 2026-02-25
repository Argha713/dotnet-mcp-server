// Argha - 2026-02-24 - tests for resources/list, resources/read, FileSystemResourceProvider
using FluentAssertions;
using McpServer.Configuration;
using McpServer.Protocol;
using McpServer.Resources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Xunit;

namespace McpServer.Tests;

// ============================================================
// FileSystemResourceProvider — unit tests
// ============================================================
public class FileSystemResourceProviderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemResourceProvider _provider;

    public FileSystemResourceProviderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp-resource-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var settings = Options.Create(new FileSystemSettings
        {
            AllowedPaths = new List<string> { _tempDir }
        });
        _provider = new FileSystemResourceProvider(settings);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // --- CanHandle ---

    [Fact]
    public void CanHandle_FileUri_ReturnsTrue()
    {
        _provider.CanHandle("file:///C:/some/path.txt").Should().BeTrue();
    }

    [Fact]
    public void CanHandle_HttpUri_ReturnsFalse()
    {
        _provider.CanHandle("https://example.com/file.txt").Should().BeFalse();
    }

    [Fact]
    public void CanHandle_EmptyString_ReturnsFalse()
    {
        _provider.CanHandle("").Should().BeFalse();
    }

    // --- ListResourcesAsync ---

    [Fact]
    public async Task ListResources_WithFiles_ReturnsEachFileAsResource()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "hello.txt"), "hello");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "data.json"), "{}");

        var resources = (await _provider.ListResourcesAsync(CancellationToken.None)).ToList();

        resources.Should().HaveCount(2);
        resources.Select(r => r.Name).Should().Contain("hello.txt").And.Contain("data.json");
        resources.All(r => r.Uri.StartsWith("file://")).Should().BeTrue();
    }

    [Fact]
    public async Task ListResources_EmptyDirectory_ReturnsEmptyList()
    {
        var resources = await _provider.ListResourcesAsync(CancellationToken.None);

        resources.Should().BeEmpty();
    }

    [Fact]
    public async Task ListResources_NonExistentAllowedPath_IsSkipped()
    {
        var settings = Options.Create(new FileSystemSettings
        {
            AllowedPaths = new List<string> { "/nonexistent/path/xyz" }
        });
        var provider = new FileSystemResourceProvider(settings);

        var resources = await provider.ListResourcesAsync(CancellationToken.None);

        resources.Should().BeEmpty();
    }

    [Fact]
    public async Task ListResources_NoAllowedPaths_ReturnsEmptyList()
    {
        var settings = Options.Create(new FileSystemSettings { AllowedPaths = new List<string>() });
        var provider = new FileSystemResourceProvider(settings);

        var resources = await provider.ListResourcesAsync(CancellationToken.None);

        resources.Should().BeEmpty();
    }

    [Fact]
    public async Task ListResources_SetsCorrectMimeType()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "notes.md"), "# Hello");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "config.json"), "{}");

        var resources = (await _provider.ListResourcesAsync(CancellationToken.None)).ToList();

        resources.First(r => r.Name == "notes.md").MimeType.Should().Be("text/markdown");
        resources.First(r => r.Name == "config.json").MimeType.Should().Be("application/json");
    }

    [Fact]
    public async Task ListResources_IncludesFilesInSubdirectories()
    {
        var subDir = Directory.CreateDirectory(Path.Combine(_tempDir, "sub"));
        await File.WriteAllTextAsync(Path.Combine(subDir.FullName, "nested.txt"), "nested");

        var resources = (await _provider.ListResourcesAsync(CancellationToken.None)).ToList();

        resources.Should().ContainSingle(r => r.Name == "nested.txt");
    }

    // --- ReadResourceAsync ---

    [Fact]
    public async Task ReadResource_TextFile_ReturnsTextContent()
    {
        var filePath = Path.Combine(_tempDir, "readme.txt");
        await File.WriteAllTextAsync(filePath, "Hello MCP");
        var uri = FileSystemResourceProvider.PathToFileUri(filePath);

        var contents = await _provider.ReadResourceAsync(uri, CancellationToken.None);

        contents.Text.Should().Be("Hello MCP");
        contents.Blob.Should().BeNull();
        contents.MimeType.Should().Be("text/plain");
        contents.Uri.Should().Be(uri);
    }

    [Fact]
    public async Task ReadResource_JsonFile_ReturnsTextContent()
    {
        var filePath = Path.Combine(_tempDir, "data.json");
        await File.WriteAllTextAsync(filePath, "{\"key\":\"value\"}");
        var uri = FileSystemResourceProvider.PathToFileUri(filePath);

        var contents = await _provider.ReadResourceAsync(uri, CancellationToken.None);

        contents.Text.Should().Be("{\"key\":\"value\"}");
        contents.MimeType.Should().Be("application/json");
    }

    [Fact]
    public async Task ReadResource_BinaryFile_ReturnsBlobNotText()
    {
        var filePath = Path.Combine(_tempDir, "image.png");
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A }; // PNG magic bytes
        await File.WriteAllBytesAsync(filePath, bytes);
        var uri = FileSystemResourceProvider.PathToFileUri(filePath);

        var contents = await _provider.ReadResourceAsync(uri, CancellationToken.None);

        contents.Blob.Should().NotBeNullOrEmpty();
        contents.Text.Should().BeNull();
        contents.MimeType.Should().Be("image/png");
        Convert.FromBase64String(contents.Blob!).Should().Equal(bytes);
    }

    [Fact]
    public async Task ReadResource_FileNotFound_ThrowsFileNotFoundException()
    {
        var uri = FileSystemResourceProvider.PathToFileUri(Path.Combine(_tempDir, "nonexistent.txt"));

        await _provider.Invoking(p => p.ReadResourceAsync(uri, CancellationToken.None))
            .Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task ReadResource_OutsideAllowedPath_ThrowsUnauthorizedAccessException()
    {
        var outsidePath = Path.GetTempPath();
        var outsideFile = Path.Combine(outsidePath, $"outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outsideFile, "secret");

        try
        {
            var uri = FileSystemResourceProvider.PathToFileUri(outsideFile);

            await _provider.Invoking(p => p.ReadResourceAsync(uri, CancellationToken.None))
                .Should().ThrowAsync<UnauthorizedAccessException>();
        }
        finally
        {
            File.Delete(outsideFile);
        }
    }

    [Fact]
    public async Task ReadResource_InvalidScheme_ThrowsArgumentException()
    {
        await _provider.Invoking(p => p.ReadResourceAsync("https://example.com/file.txt", CancellationToken.None))
            .Should().ThrowAsync<ArgumentException>();
    }

    // --- URI helpers ---

    [Fact]
    public void PathToFileUri_WindowsAbsolutePath_ProducesThreeSlashUri()
    {
        // This test verifies the round-trip on any OS using a temp path
        var path = Path.GetFullPath(Path.Combine(_tempDir, "test.txt"));
        var uri = FileSystemResourceProvider.PathToFileUri(path);

        uri.Should().StartWith("file://");
        var roundTripped = FileSystemResourceProvider.FileUriToPath(uri);
        roundTripped.Should().Be(path, because: "URI round-trip should reproduce the original path");
    }
}

// ============================================================
// McpServerHandler — resources/list and resources/read routing
// ============================================================
public class ResourceHandlerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly McpServerHandler _handler;

    public ResourceHandlerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp-handler-resource-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var fsSettings = Options.Create(new FileSystemSettings
        {
            AllowedPaths = new List<string> { _tempDir }
        });
        var serverSettings = Options.Create(new ServerSettings { Name = "test", Version = "1.0.0" });
        var provider = new FileSystemResourceProvider(fsSettings);

        _handler = new McpServerHandler(
            tools: Array.Empty<McpServer.Tools.ITool>(),
            resourceProviders: new McpServer.Resources.IResourceProvider[] { provider },
            promptProviders: Array.Empty<McpServer.Prompts.IPromptProvider>(),
            serverSettings: serverSettings,
            logger: NullLogger<McpServerHandler>.Instance,
            // Argha - 2026-02-24 - no-op sink; writer never initialised in unit tests
            logSink: new McpServer.Logging.McpLogSink(),
            // Argha - 2026-02-25 - Phase 6.2: no-op audit logger for unit tests
            auditLogger: McpServer.Audit.NullAuditLogger.Instance,
            // Argha - 2026-02-25 - Phase 6.3: no-op rate limiter for unit tests
            rateLimiter: McpServer.RateLimiting.NullRateLimiter.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
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

    // --- Initialize advertises Resources capability ---

    [Fact]
    public async Task Initialize_AdvertisesResourcesCapability()
    {
        var msg = MakeRequest("initialize", paramsJson: "{\"protocolVersion\":\"2024-11-05\",\"clientInfo\":{\"name\":\"test\",\"version\":\"1\"}}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        var json = JsonSerializer.Serialize(response!.Result);
        json.Should().Contain("\"resources\"");
    }

    // --- resources/list ---

    [Fact]
    public async Task ResourcesList_BeforeInitialize_ReturnsError()
    {
        var msg = MakeRequest("resources/list");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Message.Should().Contain("not initialized");
    }

    [Fact]
    public async Task ResourcesList_WithFiles_ReturnsResourceArray()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "a.txt"), "aaa");
        await InitializeAsync();
        var msg = MakeRequest("resources/list");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().BeNull();
        var json = JsonSerializer.Serialize(response.Result);
        json.Should().Contain("\"resources\"");
        json.Should().Contain("a.txt");
        json.Should().Contain("file://");
    }

    [Fact]
    public async Task ResourcesList_EmptyDirectory_ReturnsEmptyArray()
    {
        await InitializeAsync();
        var msg = MakeRequest("resources/list");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().BeNull();
        var json = JsonSerializer.Serialize(response.Result);
        json.Should().Contain("\"resources\":[]");
    }

    // --- resources/read ---

    [Fact]
    public async Task ResourcesRead_ValidTextFile_ReturnsTextContents()
    {
        var filePath = Path.Combine(_tempDir, "hello.txt");
        await File.WriteAllTextAsync(filePath, "hello world");
        var uri = FileSystemResourceProvider.PathToFileUri(filePath);
        await InitializeAsync();
        var msg = MakeRequest("resources/read", paramsJson: $"{{\"uri\":\"{uri.Replace("\\", "\\\\")}\"}}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().BeNull();
        var json = JsonSerializer.Serialize(response.Result);
        json.Should().Contain("hello world");
        json.Should().Contain("\"text\"");
    }

    [Fact]
    public async Task ResourcesRead_MissingParams_ReturnsInvalidParamsError()
    {
        await InitializeAsync();
        var msg = MakeRequest("resources/read");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32602);
    }

    [Fact]
    public async Task ResourcesRead_EmptyUri_ReturnsInvalidParamsError()
    {
        await InitializeAsync();
        var msg = MakeRequest("resources/read", paramsJson: "{\"uri\":\"\"}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32602);
    }

    [Fact]
    public async Task ResourcesRead_UnsupportedScheme_ReturnsMethodNotFoundError()
    {
        await InitializeAsync();
        var msg = MakeRequest("resources/read", paramsJson: "{\"uri\":\"https://example.com/file.txt\"}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32601);
    }

    [Fact]
    public async Task ResourcesRead_FileNotFound_ReturnsInvalidParamsError()
    {
        var uri = FileSystemResourceProvider.PathToFileUri(Path.Combine(_tempDir, "missing.txt"));
        await InitializeAsync();
        var msg = MakeRequest("resources/read", paramsJson: $"{{\"uri\":\"{uri.Replace("\\", "\\\\")}\"}}");

        var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

        response!.Error.Should().NotBeNull();
        response.Error!.Code.Should().Be(-32602);
    }

    [Fact]
    public async Task ResourcesRead_PathOutsideAllowed_ReturnsAccessDeniedError()
    {
        // Point the URI to a temp file outside the configured allowed path
        var outsideFile = Path.Combine(Path.GetTempPath(), $"outside-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outsideFile, "secret");

        try
        {
            var uri = FileSystemResourceProvider.PathToFileUri(outsideFile);
            await InitializeAsync();
            var msg = MakeRequest("resources/read", paramsJson: $"{{\"uri\":\"{uri.Replace("\\", "\\\\")}\"}}");

            var response = await _handler.ProcessMessageAsync(msg, CancellationToken.None);

            response!.Error.Should().NotBeNull();
            response.Error!.Message.Should().Contain("Access denied");
        }
        finally
        {
            File.Delete(outsideFile);
        }
    }
}
