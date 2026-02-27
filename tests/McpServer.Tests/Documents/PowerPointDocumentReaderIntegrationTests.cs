// Argha - 2026-02-27 - Phase 8.2: integration tests for PowerPointDocumentReader using real .pptx files

using FluentAssertions;
using McpServer.Documents;
using Xunit;

namespace McpServer.Tests.Documents;

public class PowerPointDocumentReaderIntegrationTests : IDisposable
{
    private readonly string _pptxPath;
    private readonly PowerPointDocumentReader _reader;

    public PowerPointDocumentReaderIntegrationTests()
    {
        _pptxPath = TestDocumentFactory.WriteTempPptx("Hello PowerPoint");
        _reader = new PowerPointDocumentReader();
    }

    public void Dispose()
    {
        if (File.Exists(_pptxPath)) File.Delete(_pptxPath);
    }

    [Fact]
    public async Task ReadTextAsync_ValidPptx_ReturnsExtractedText()
    {
        var result = await _reader.ReadTextAsync(_pptxPath, new DocumentReadOptions(), CancellationToken.None);

        result.Should().NotBeNull();
        result.Truncated.Should().BeFalse();
        result.Text.Should().Contain("Hello");
        result.PageCount.Should().Be(1);
    }

    [Fact]
    public async Task ReadTextAsync_PageRange_ReadsSubsetOfSlides()
    {
        // Argha - 2026-02-27 - create a 3-slide presentation, read only slides 2-3
        var path = TestDocumentFactory.WriteTempPptx("Slide One", "Slide Two", "Slide Three");

        try
        {
            var options = new DocumentReadOptions(PageRange: "2-3");
            var result = await _reader.ReadTextAsync(path, options, CancellationToken.None);

            result.PageCount.Should().Be(3);
            result.Text.Should().Contain("Slide Two");
            result.Text.Should().Contain("Slide Three");
            result.Text.Should().NotContain("Slide One");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task GetInfoAsync_ValidPptx_ReturnsMetadata()
    {
        var info = await _reader.GetInfoAsync(_pptxPath, CancellationToken.None);

        info.Should().NotBeNull();
        info.Format.Should().Be("PowerPoint");
        info.SlideCount.Should().Be(1);
        info.FileSizeBytes.Should().BeGreaterThan(0);
        info.PageCount.Should().BeNull();
        info.SheetCount.Should().BeNull();
    }

    [Fact]
    public async Task SearchAsync_FindsTextInSlide()
    {
        var matches = (await _reader.SearchAsync(_pptxPath, "Hello", false, CancellationToken.None)).ToList();

        matches.Should().NotBeEmpty();
        matches[0].Page.Should().Be(1);
        matches[0].Context.Should().Contain("Hello");
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        var matches = (await _reader.SearchAsync(_pptxPath, "ZZZnonexistentZZZ", false, CancellationToken.None)).ToList();

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSheetsAsync_ReturnsEmpty()
    {
        // Argha - 2026-02-27 - ListSheetsAsync is not applicable for PowerPoint
        var sheets = (await _reader.ListSheetsAsync(_pptxPath, CancellationToken.None)).ToList();

        sheets.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractTablesAsync_ReturnsEmpty()
    {
        // Argha - 2026-02-27 - table extraction deferred to Phase 8.3
        var tables = (await _reader.ExtractTablesAsync(_pptxPath, CancellationToken.None)).ToList();

        tables.Should().BeEmpty();
    }
}
