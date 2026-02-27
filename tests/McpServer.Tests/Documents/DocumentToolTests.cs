// Argha - 2026-02-26 - Phase 8.1: unit tests for DocumentTool and PdfDocumentReader

using FluentAssertions;
using McpServer.Configuration;
using McpServer.Documents;
using McpServer.Protocol;
using McpServer.Tools;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace McpServer.Tests.Documents;

// ============================================================
// DocumentTool unit tests (mock IDocumentReader)
// ============================================================
public class DocumentToolTests
{
    private readonly Mock<IDocumentReader> _mockReader;
    private readonly DocumentTool _tool;

    public DocumentToolTests()
    {
        _mockReader = new Mock<IDocumentReader>();
        _mockReader.Setup(r => r.SupportedExtensions).Returns(new[] { ".pdf" });

        var fsOptions = Options.Create(new FileSystemSettings
        {
            AllowedPaths = new List<string> { Path.GetTempPath() }
        });
        var docOptions = Options.Create(new DocumentSettings());

        _tool = new DocumentTool(fsOptions, docOptions, new[] { _mockReader.Object });
    }

    [Fact]
    public void Name_ShouldBeDocument()
    {
        _tool.Name.Should().Be("document");
    }

    [Fact]
    public void Description_ShouldNotBeEmpty()
    {
        _tool.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void InputSchema_ShouldHaveRequiredProperties()
    {
        _tool.InputSchema.Properties.Should().ContainKey("action");
        _tool.InputSchema.Properties.Should().ContainKey("path");
        _tool.InputSchema.Required.Should().Contain("action");
        _tool.InputSchema.Required.Should().Contain("path");
    }

    [Fact]
    public async Task ExecuteAsync_NullPath_ReturnsError()
    {
        var args = new Dictionary<string, object> { ["action"] = "read" };

        var result = await _tool.ExecuteAsync(args);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("'path' parameter is required");
    }

    [Fact]
    public async Task ExecuteAsync_PathOutsideAllowed_ReturnsAccessDenied()
    {
        // Use a path outside the configured AllowedPaths (temp dir)
        var args = new Dictionary<string, object>
        {
            ["action"] = "read",
            ["path"] = @"C:\Windows\System32\something.pdf"
        };

        var result = await _tool.ExecuteAsync(args);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("Access denied");
    }

    [Fact]
    public async Task ExecuteAsync_FileNotFound_ReturnsError()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), "nonexistent_test_file.pdf");
        var args = new Dictionary<string, object>
        {
            ["action"] = "read",
            ["path"] = fakePath
        };

        var result = await _tool.ExecuteAsync(args);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("File not found");
    }

    [Fact]
    public async Task ExecuteAsync_UnsupportedExtension_ReturnsError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xyz");
        await File.WriteAllTextAsync(path, "dummy");

        try
        {
            var args = new Dictionary<string, object> { ["action"] = "read", ["path"] = path };
            var result = await _tool.ExecuteAsync(args);

            result.IsError.Should().BeTrue();
            result.Content[0].Text.Should().Contain("Unsupported file format");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Read_CallsReaderAndReturnsText()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "dummy");

        _mockReader
            .Setup(r => r.ReadTextAsync(path, It.IsAny<DocumentReadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentContent("Extracted text content", 1, false));

        try
        {
            var args = new Dictionary<string, object> { ["action"] = "read", ["path"] = path };
            var result = await _tool.ExecuteAsync(args);

            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("Extracted text content");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Read_PassesPageRangeToReader()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "dummy");

        DocumentReadOptions? capturedOptions = null;
        _mockReader
            .Setup(r => r.ReadTextAsync(path, It.IsAny<DocumentReadOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, DocumentReadOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new DocumentContent("text", 5, false));

        try
        {
            var args = new Dictionary<string, object>
            {
                ["action"] = "read",
                ["path"] = path,
                ["pages"] = "2-4"
            };
            await _tool.ExecuteAsync(args);

            capturedOptions.Should().NotBeNull();
            capturedOptions!.PageRange.Should().Be("2-4");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Read_TruncatedContent_AppendsTruncationMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "dummy");

        _mockReader
            .Setup(r => r.ReadTextAsync(path, It.IsAny<DocumentReadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentContent("partial text", 10, Truncated: true, TruncationMessage: "Truncated at 50000 chars."));

        try
        {
            var args = new Dictionary<string, object> { ["action"] = "read", ["path"] = path };
            var result = await _tool.ExecuteAsync(args);

            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("Truncated at 50000 chars.");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Info_CallsReaderAndReturnsMetadata()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "dummy");

        _mockReader
            .Setup(r => r.GetInfoAsync(path, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentInfo(
                Format: "PDF",
                Title: "Annual Report 2025",
                Author: "Finance Team",
                Created: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Modified: null,
                PageCount: 42,
                WordCount: null,
                SheetCount: null,
                SlideCount: null,
                FileSizeBytes: 1024 * 100));

        try
        {
            var args = new Dictionary<string, object> { ["action"] = "info", ["path"] = path };
            var result = await _tool.ExecuteAsync(args);

            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("Annual Report 2025");
            result.Content[0].Text.Should().Contain("Finance Team");
            result.Content[0].Text.Should().Contain("42");
            result.Content[0].Text.Should().Contain("PDF");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Search_MissingQuery_ReturnsError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "dummy");

        try
        {
            var args = new Dictionary<string, object> { ["action"] = "search", ["path"] = path };
            var result = await _tool.ExecuteAsync(args);

            result.IsError.Should().BeFalse(); // not IsError, but text contains error message
            result.Content[0].Text.Should().Contain("'query' parameter is required");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Search_CallsReaderAndReturnsMatches()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "dummy");

        _mockReader
            .Setup(r => r.SearchAsync(path, "revenue", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new DocumentSearchMatch(1, "revenue", "...total revenue was $5M..."),
                new DocumentSearchMatch(3, "revenue", "...revenue increased by 10%...")
            });

        try
        {
            var args = new Dictionary<string, object>
            {
                ["action"] = "search",
                ["path"] = path,
                ["query"] = "revenue"
            };
            var result = await _tool.ExecuteAsync(args);

            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("2 match");
            result.Content[0].Text.Should().Contain("total revenue was $5M");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Search_NoMatches_ReturnsNoMatchesMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "dummy");

        _mockReader
            .Setup(r => r.SearchAsync(path, "unicorn", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DocumentSearchMatch>());

        try
        {
            var args = new Dictionary<string, object>
            {
                ["action"] = "search",
                ["path"] = path,
                ["query"] = "unicorn"
            };
            var result = await _tool.ExecuteAsync(args);

            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("No matches found");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_UnknownAction_ReturnsErrorMessage()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "dummy");

        try
        {
            var args = new Dictionary<string, object> { ["action"] = "fly", ["path"] = path };
            var result = await _tool.ExecuteAsync(args);

            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("Unknown action");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

// ============================================================
// Phase 8.2: DocumentTool unit tests — sheet param, list_sheets action, extension routing
// ============================================================
public class DocumentToolPhase82Tests
{
    private static DocumentTool CreateToolWithReaders(params IDocumentReader[] readers)
    {
        var fsOptions = Options.Create(new FileSystemSettings
        {
            AllowedPaths = new List<string> { Path.GetTempPath() }
        });
        var docOptions = Options.Create(new DocumentSettings());
        return new DocumentTool(fsOptions, docOptions, readers);
    }

    [Fact]
    public async Task ExecuteAsync_ListSheets_CallsReaderAndReturnsSheets()
    {
        // Argha - 2026-02-27 - mock reader returns two worksheets; verify table output
        var mockReader = new Mock<IDocumentReader>();
        mockReader.Setup(r => r.SupportedExtensions).Returns(new[] { ".xlsx" });
        mockReader
            .Setup(r => r.ListSheetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new WorksheetSummary("Sales", 100, 5),
                new WorksheetSummary("Config", 10, 3)
            });

        var tool = CreateToolWithReaders(mockReader.Object);
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(path, "dummy");

        try
        {
            var args = new Dictionary<string, object> { ["action"] = "list_sheets", ["path"] = path };
            var result = await tool.ExecuteAsync(args);

            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("Sales");
            result.Content[0].Text.Should().Contain("Config");
            result.Content[0].Text.Should().Contain("100");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Read_PassesSheetParameterToReader()
    {
        // Argha - 2026-02-27 - sheet arg should reach the reader via DocumentReadOptions.Sheet
        var mockReader = new Mock<IDocumentReader>();
        mockReader.Setup(r => r.SupportedExtensions).Returns(new[] { ".xlsx" });

        DocumentReadOptions? capturedOptions = null;
        mockReader
            .Setup(r => r.ReadTextAsync(It.IsAny<string>(), It.IsAny<DocumentReadOptions>(), It.IsAny<CancellationToken>()))
            .Callback<string, DocumentReadOptions, CancellationToken>((_, opts, _) => capturedOptions = opts)
            .ReturnsAsync(new DocumentContent("cell data", 2, false));

        var tool = CreateToolWithReaders(mockReader.Object);
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(path, "dummy");

        try
        {
            var args = new Dictionary<string, object>
            {
                ["action"] = "read",
                ["path"] = path,
                ["sheet"] = "MySheet"
            };
            await tool.ExecuteAsync(args);

            capturedOptions.Should().NotBeNull();
            capturedOptions!.Sheet.Should().Be("MySheet");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Docx_RoutesToWordReader()
    {
        // Argha - 2026-02-27 - .docx extension should route to the docx-capable reader
        var docxReader = new Mock<IDocumentReader>();
        docxReader.Setup(r => r.SupportedExtensions).Returns(new[] { ".docx" });
        docxReader
            .Setup(r => r.ReadTextAsync(It.IsAny<string>(), It.IsAny<DocumentReadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentContent("Word content", 1, false));

        var tool = CreateToolWithReaders(docxReader.Object);
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.docx");
        await File.WriteAllTextAsync(path, "dummy");

        try
        {
            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["action"] = "read", ["path"] = path });

            result.IsError.Should().BeFalse();
            docxReader.Verify(r => r.ReadTextAsync(It.IsAny<string>(), It.IsAny<DocumentReadOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Xlsx_RoutesToExcelReader()
    {
        // Argha - 2026-02-27 - .xlsx extension should route to the xlsx-capable reader
        var xlsxReader = new Mock<IDocumentReader>();
        xlsxReader.Setup(r => r.SupportedExtensions).Returns(new[] { ".xlsx" });
        xlsxReader
            .Setup(r => r.ReadTextAsync(It.IsAny<string>(), It.IsAny<DocumentReadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentContent("Excel content", 1, false));

        var tool = CreateToolWithReaders(xlsxReader.Object);
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        await File.WriteAllTextAsync(path, "dummy");

        try
        {
            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["action"] = "read", ["path"] = path });

            result.IsError.Should().BeFalse();
            xlsxReader.Verify(r => r.ReadTextAsync(It.IsAny<string>(), It.IsAny<DocumentReadOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Pptx_RoutesToPowerPointReader()
    {
        // Argha - 2026-02-27 - .pptx extension should route to the pptx-capable reader
        var pptxReader = new Mock<IDocumentReader>();
        pptxReader.Setup(r => r.SupportedExtensions).Returns(new[] { ".pptx" });
        pptxReader
            .Setup(r => r.ReadTextAsync(It.IsAny<string>(), It.IsAny<DocumentReadOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentContent("PowerPoint content", 1, false));

        var tool = CreateToolWithReaders(pptxReader.Object);
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pptx");
        await File.WriteAllTextAsync(path, "dummy");

        try
        {
            var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["action"] = "read", ["path"] = path });

            result.IsError.Should().BeFalse();
            pptxReader.Verify(r => r.ReadTextAsync(It.IsAny<string>(), It.IsAny<DocumentReadOptions>(), It.IsAny<CancellationToken>()), Times.Once);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ListSheets_UnsupportedFormat_ReturnsError()
    {
        // Argha - 2026-02-27 - list_sheets on a PDF (no reader supports list_sheets) → "No sheets found"
        // Actually the tool routes to the reader and calls ListSheetsAsync which defaults to empty
        var pdfReader = new Mock<IDocumentReader>();
        pdfReader.Setup(r => r.SupportedExtensions).Returns(new[] { ".pdf" });
        pdfReader
            .Setup(r => r.ListSheetsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WorksheetSummary>());

        var tool = CreateToolWithReaders(pdfReader.Object);
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "dummy");

        try
        {
            var args = new Dictionary<string, object> { ["action"] = "list_sheets", ["path"] = path };
            var result = await tool.ExecuteAsync(args);

            result.IsError.Should().BeFalse();
            result.Content[0].Text.Should().Contain("No sheets found");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

// ============================================================
// PdfDocumentReader unit tests (no file I/O)
// ============================================================
public class PdfDocumentReaderParseTests
{
    [Fact]
    public void SupportedExtensions_ContainsPdf()
    {
        var reader = new PdfDocumentReader();
        reader.SupportedExtensions.Should().Contain(".pdf");
    }

    [Fact]
    public void ParsePageRange_Null_ReturnsAllPages()
    {
        var (start, end) = PdfDocumentReader.ParsePageRange(null, 10);
        start.Should().Be(1);
        end.Should().Be(10);
    }

    [Fact]
    public void ParsePageRange_SinglePage_ReturnsCorrectPage()
    {
        var (start, end) = PdfDocumentReader.ParsePageRange("3", 10);
        start.Should().Be(3);
        end.Should().Be(3);
    }

    [Fact]
    public void ParsePageRange_Range_ReturnsCorrectRange()
    {
        var (start, end) = PdfDocumentReader.ParsePageRange("2-5", 10);
        start.Should().Be(2);
        end.Should().Be(5);
    }

    [Fact]
    public void ParsePageRange_RangeExceedingTotal_ClampsToTotal()
    {
        var (start, end) = PdfDocumentReader.ParsePageRange("1-20", 10);
        start.Should().Be(1);
        end.Should().Be(10);
    }

    [Fact]
    public void ParsePageRange_SinglePageExceedingTotal_ClampsToTotal()
    {
        var (start, end) = PdfDocumentReader.ParsePageRange("99", 10);
        start.Should().Be(10);
        end.Should().Be(10);
    }

    [Fact]
    public void ParsePageRange_InvalidString_ReturnsAllPages()
    {
        var (start, end) = PdfDocumentReader.ParsePageRange("abc", 10);
        start.Should().Be(1);
        end.Should().Be(10);
    }
}

// ============================================================
// PdfDocumentReader integration tests (real PDF file via TestDocumentFactory)
// ============================================================
public class PdfDocumentReaderIntegrationTests : IDisposable
{
    private readonly string _pdfPath;
    private readonly PdfDocumentReader _reader;

    public PdfDocumentReaderIntegrationTests()
    {
        _pdfPath = TestDocumentFactory.WriteTempPdf("Hello World Sample Document");
        _reader = new PdfDocumentReader();
    }

    public void Dispose()
    {
        if (File.Exists(_pdfPath)) File.Delete(_pdfPath);
    }

    [Fact]
    public async Task ReadTextAsync_ValidPdf_ReturnsExtractedText()
    {
        var result = await _reader.ReadTextAsync(_pdfPath, new DocumentReadOptions(), CancellationToken.None);

        result.Should().NotBeNull();
        result.PageCount.Should().Be(1);
        result.Truncated.Should().BeFalse();
        result.Text.Should().Contain("Hello");
    }

    [Fact]
    public async Task GetInfoAsync_ValidPdf_ReturnsPageCount()
    {
        var info = await _reader.GetInfoAsync(_pdfPath, CancellationToken.None);

        info.Should().NotBeNull();
        info.Format.Should().Be("PDF");
        info.PageCount.Should().Be(1);
        info.FileSizeBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SearchAsync_ValidPdf_FindsExistingWord()
    {
        var matches = (await _reader.SearchAsync(_pdfPath, "Hello", false, CancellationToken.None)).ToList();

        matches.Should().NotBeEmpty();
        matches[0].Page.Should().Be(1);
        matches[0].Context.Should().Contain("Hello");
    }

    [Fact]
    public async Task SearchAsync_ValidPdf_CaseInsensitive_FindsMatch()
    {
        var matches = (await _reader.SearchAsync(_pdfPath, "hello", false, CancellationToken.None)).ToList();
        matches.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SearchAsync_ValidPdf_CaseSensitive_MissesWrongCase()
    {
        var matches = (await _reader.SearchAsync(_pdfPath, "hello", true, CancellationToken.None)).ToList();
        // "Hello" starts with uppercase; searching lowercase with case-sensitive should return 0 matches
        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_NonExistentWord_ReturnsEmpty()
    {
        var matches = (await _reader.SearchAsync(_pdfPath, "ZZZnonexistentZZZ", false, CancellationToken.None)).ToList();
        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task ExtractTablesAsync_ReturnsEmpty_ForPhase81()
    {
        // Argha - 2026-02-26 - table extraction deferred to Phase 8.3
        var tables = (await _reader.ExtractTablesAsync(_pdfPath, CancellationToken.None)).ToList();
        tables.Should().BeEmpty();
    }
}
