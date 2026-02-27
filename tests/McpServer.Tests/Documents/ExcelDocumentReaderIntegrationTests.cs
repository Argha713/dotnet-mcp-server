// Argha - 2026-02-27 - Phase 8.2: integration tests for ExcelDocumentReader using real .xlsx files

using ClosedXML.Excel;
using FluentAssertions;
using McpServer.Documents;
using Xunit;

namespace McpServer.Tests.Documents;

public class ExcelDocumentReaderIntegrationTests : IDisposable
{
    private readonly string _xlsxPath;
    private readonly ExcelDocumentReader _reader;

    public ExcelDocumentReaderIntegrationTests()
    {
        _xlsxPath = TestDocumentFactory.WriteTempXlsx("Sheet1", "Hello Excel");
        _reader = new ExcelDocumentReader();
    }

    public void Dispose()
    {
        if (File.Exists(_xlsxPath)) File.Delete(_xlsxPath);
    }

    [Fact]
    public async Task ReadTextAsync_ValidXlsx_ReturnsExtractedText()
    {
        var result = await _reader.ReadTextAsync(_xlsxPath, new DocumentReadOptions(), CancellationToken.None);

        result.Should().NotBeNull();
        result.Truncated.Should().BeFalse();
        result.Text.Should().Contain("Hello Excel");
        result.PageCount.Should().Be(1); // one sheet
    }

    [Fact]
    public async Task ReadTextAsync_SpecificSheet_ReadsNamedSheet()
    {
        // Argha - 2026-02-27 - create a two-sheet workbook, request the second sheet
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        using (var wb = new XLWorkbook())
        {
            wb.Worksheets.Add("First").Cell(1, 1).Value = "FirstSheetData";
            wb.Worksheets.Add("Second").Cell(1, 1).Value = "SecondSheetData";
            wb.SaveAs(path);
        }

        try
        {
            var options = new DocumentReadOptions(Sheet: "Second");
            var result = await _reader.ReadTextAsync(path, options, CancellationToken.None);

            result.Text.Should().Contain("SecondSheetData");
            result.Text.Should().Contain("[Sheet: Second]");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task GetInfoAsync_ValidXlsx_ReturnsMetadata()
    {
        var info = await _reader.GetInfoAsync(_xlsxPath, CancellationToken.None);

        info.Should().NotBeNull();
        info.Format.Should().Be("Excel");
        info.SheetCount.Should().Be(1);
        info.FileSizeBytes.Should().BeGreaterThan(0);
        info.PageCount.Should().BeNull();
        info.SlideCount.Should().BeNull();
    }

    [Fact]
    public async Task ListSheetsAsync_MultipleSheets_ReturnsAll()
    {
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws1 = wb.Worksheets.Add("Alpha");
            ws1.Cell(1, 1).Value = "A";
            ws1.Cell(3, 2).Value = "B"; // 3 rows, 2 cols
            wb.Worksheets.Add("Beta"); // empty
            wb.SaveAs(path);
        }

        try
        {
            var sheets = (await _reader.ListSheetsAsync(path, CancellationToken.None)).ToList();

            sheets.Should().HaveCount(2);
            sheets.Should().Contain(s => s.Name == "Alpha");
            sheets.Should().Contain(s => s.Name == "Beta");

            var alpha = sheets.First(s => s.Name == "Alpha");
            alpha.RowCount.Should().Be(3);
            alpha.ColumnCount.Should().Be(2);

            var beta = sheets.First(s => s.Name == "Beta");
            beta.RowCount.Should().Be(0);
            beta.ColumnCount.Should().Be(0);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SearchAsync_FindsMatchInCells()
    {
        var matches = (await _reader.SearchAsync(_xlsxPath, "Hello", false, CancellationToken.None)).ToList();

        matches.Should().NotBeEmpty();
        matches[0].Page.Should().Be(1);
        matches[0].Context.Should().Contain("Hello");
    }

    [Fact]
    public async Task SearchAsync_NoMatch_ReturnsEmpty()
    {
        var matches = (await _reader.SearchAsync(_xlsxPath, "ZZZnonexistentZZZ", false, CancellationToken.None)).ToList();

        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadTextAsync_RowLimit_Truncates()
    {
        // Argha - 2026-02-27 - create a sheet with more rows than the limit
        var path = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("Data");
            for (int r = 1; r <= 10; r++)
                ws.Cell(r, 1).Value = $"Row {r}";
            wb.SaveAs(path);
        }

        try
        {
            var options = new DocumentReadOptions(MaxRows: 3);
            var result = await _reader.ReadTextAsync(path, options, CancellationToken.None);

            result.Truncated.Should().BeTrue();
            result.TruncationMessage.Should().Contain("Row limit");
            result.Text.Should().Contain("Row 1");
            result.Text.Should().NotContain("Row 10");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task ExtractTablesAsync_ReturnsEmpty()
    {
        // Argha - 2026-02-27 - table extraction deferred to Phase 8.3
        var tables = (await _reader.ExtractTablesAsync(_xlsxPath, CancellationToken.None)).ToList();

        tables.Should().BeEmpty();
    }
}
