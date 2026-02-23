// Argha - 2026-02-23 - tests for the --init first-run configuration wizard
using FluentAssertions;
using System.Text.Json;
using Xunit;

namespace McpServer.Tests;

public class InitWizardTests : IDisposable
{
    private readonly string _tempDir;

    public InitWizardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"mcp_init_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string OutputPath => Path.Combine(_tempDir, "appsettings.json");

    // Argha - 2026-02-23 - helper: run the wizard with scripted stdin
    private static async Task<int> Run(string outputPath, string stdin)
    {
        using var reader = new StringReader(stdin);
        using var writer = new StringWriter();
        return await InitWizard.RunAsync(outputPath, reader, writer);
    }

    [Fact]
    public async Task AllEmpty_WritesValidJson_ReturnsZero()
    {
        // 3 empty lines: skip paths, skip SQL name, skip hosts
        var result = await Run(OutputPath, "\n\n\n");

        result.Should().Be(0);
        File.Exists(OutputPath).Should().BeTrue();

        var json = await File.ReadAllTextAsync(OutputPath);
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("FileSystem").GetProperty("AllowedPaths").GetArrayLength().Should().Be(0);
        doc.RootElement.GetProperty("Sql").GetProperty("Connections").EnumerateObject().Should().BeEmpty();
        doc.RootElement.GetProperty("Http").GetProperty("AllowedHosts").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task WithFilesystemPath_WritesPathToJson()
    {
        // one path, then blank to finish; blank SQL name; blank host
        var result = await Run(OutputPath, "C:\\Users\\Argha\\Documents\n\n\n\n");

        result.Should().Be(0);
        var json = await File.ReadAllTextAsync(OutputPath);
        var doc = JsonDocument.Parse(json);
        var paths = doc.RootElement.GetProperty("FileSystem").GetProperty("AllowedPaths");
        paths.GetArrayLength().Should().Be(1);
        paths[0].GetString().Should().Be("C:\\Users\\Argha\\Documents");
    }

    [Fact]
    public async Task WithSqlConnection_WritesConnectionToJson()
    {
        // blank path; SQL name=MyDB, connStr, desc, blank to finish; blank host
        var result = await Run(OutputPath, "\nMyDB\nServer=localhost;Database=MyDB;\nLocal DB\n\n\n");

        result.Should().Be(0);
        var json = await File.ReadAllTextAsync(OutputPath);
        var doc = JsonDocument.Parse(json);
        var conn = doc.RootElement.GetProperty("Sql").GetProperty("Connections").GetProperty("MyDB");
        conn.GetProperty("ConnectionString").GetString().Should().Be("Server=localhost;Database=MyDB;");
        conn.GetProperty("Description").GetString().Should().Be("Local DB");
    }

    [Fact]
    public async Task WithHttpHost_WritesHostToJson()
    {
        // blank path; blank SQL; one host, blank to finish
        var result = await Run(OutputPath, "\n\napi.github.com\n\n");

        result.Should().Be(0);
        var json = await File.ReadAllTextAsync(OutputPath);
        var doc = JsonDocument.Parse(json);
        var hosts = doc.RootElement.GetProperty("Http").GetProperty("AllowedHosts");
        hosts.GetArrayLength().Should().Be(1);
        hosts[0].GetString().Should().Be("api.github.com");
    }

    [Fact]
    public async Task DefaultSections_AlwaysPresent()
    {
        await Run(OutputPath, "\n\n\n");
        var json = await File.ReadAllTextAsync(OutputPath);
        var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("Server", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("Logging", out _).Should().BeTrue();
        doc.RootElement.TryGetProperty("Environment", out _).Should().BeTrue();
        doc.RootElement.GetProperty("Http").GetProperty("TimeoutSeconds").GetInt32().Should().Be(30);
    }

    [Fact]
    public async Task ExistingFile_UserSaysNo_DoesNotOverwrite()
    {
        await File.WriteAllTextAsync(OutputPath, "{\"original\":true}");

        // first line is the overwrite prompt response 'n'; then blanks for each section
        var result = await Run(OutputPath, "n\n\n\n");

        result.Should().Be(0);
        var json = await File.ReadAllTextAsync(OutputPath);
        json.Should().Contain("original");
    }

    [Fact]
    public async Task ExistingFile_UserSaysYes_Overwrites()
    {
        await File.WriteAllTextAsync(OutputPath, "{\"original\":true}");

        // 'y' to overwrite, then blanks for each section
        var result = await Run(OutputPath, "y\n\n\n\n");

        result.Should().Be(0);
        var json = await File.ReadAllTextAsync(OutputPath);
        json.Should().NotContain("original");
        JsonDocument.Parse(json).RootElement.TryGetProperty("FileSystem", out _).Should().BeTrue();
    }
}
