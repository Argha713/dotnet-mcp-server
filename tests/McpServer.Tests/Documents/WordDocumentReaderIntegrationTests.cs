// Argha - 2026-02-27 - Phase 8.2: integration tests for WordDocumentReader using real .docx files

using FluentAssertions;
using McpServer.Documents;
using Xunit;

namespace McpServer.Tests.Documents;

public class WordDocumentReaderIntegrationTests : IDisposable
{
    private readonly string _docxPath;
    private readonly WordDocumentReader _reader;

    public WordDocumentReaderIntegrationTests()
    {
        _docxPath = TestDocumentFactory.WriteTempDocx("Hello Word Document");
        _reader = new WordDocumentReader();
    }

    public void Dispose()
    {
        if (File.Exists(_docxPath)) File.Delete(_docxPath);
    }

    [Fact]
    public async Task ReadTextAsync_ValidDocx_ReturnsExtractedText()
    {
        var result = await _reader.ReadTextAsync(_docxPath, new DocumentReadOptions(), CancellationToken.None);

        result.Should().NotBeNull();
        result.Truncated.Should().BeFalse();
        result.Text.Should().Contain("Hello");
        result.PageCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ReadTextAsync_Truncation_ReturnsTruncatedContent()
    {
        // Argha - 2026-02-27 - set a tiny char limit to force truncation
        var options = new DocumentReadOptions(MaxCharsOutput: 20);

        var result = await _reader.ReadTextAsync(_docxPath, options, CancellationToken.None);

        result.Truncated.Should().BeTrue();
        result.TruncationMessage.Should().Contain("truncated");
        result.Text.Length.Should().BeLessThanOrEqualTo(20 + 200); // header + truncated body within buffer
    }

    [Fact]
    public async Task GetInfoAsync_ValidDocx_ReturnsMetadata()
    {
        var info = await _reader.GetInfoAsync(_docxPath, CancellationToken.None);

        info.Should().NotBeNull();
        info.Format.Should().Be("Word");
        info.FileSizeBytes.Should().BeGreaterThan(0);
        info.PageCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task SearchAsync_CaseSensitive_FindsMatch()
    {
        var matches = (await _reader.SearchAsync(_docxPath, "Hello", true, CancellationToken.None)).ToList();

        matches.Should().NotBeEmpty();
        matches[0].Page.Should().Be(1);
        matches[0].Context.Should().Contain("Hello");
    }

    [Fact]
    public async Task SearchAsync_CaseInsensitive_FindsMatch()
    {
        var matches = (await _reader.SearchAsync(_docxPath, "hello", false, CancellationToken.None)).ToList();

        matches.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmptyList()
    {
        var matches = (await _reader.SearchAsync(_docxPath, "ZZZnonexistentZZZ", false, CancellationToken.None)).ToList();

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractTablesAsync_ReturnsEmpty()
    {
        // Argha - 2026-02-27 - table extraction is deferred to Phase 8.3
        var tables = (await _reader.ExtractTablesAsync(_docxPath, CancellationToken.None)).ToList();

        tables.Should().BeEmpty();
    }
}
