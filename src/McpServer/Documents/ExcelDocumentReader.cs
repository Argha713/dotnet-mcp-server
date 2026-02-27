// Argha - 2026-02-27 - Phase 8.2: Excel document text extraction, metadata, and search via ClosedXML

using System.Text;
using ClosedXML.Excel;

namespace McpServer.Documents;

/// <summary>
/// Reads Excel (.xlsx, .xlsm) workbooks using ClosedXML. No native dependencies.
/// </summary>
public class ExcelDocumentReader : IDocumentReader
{
    public string[] SupportedExtensions => new[] { ".xlsx", ".xlsm" };

    public async Task<DocumentContent> ReadTextAsync(string path, DocumentReadOptions options, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook(path);
            int sheetCount = workbook.Worksheets.Count;

            // Argha - 2026-02-27 - select named sheet (case-insensitive) or first sheet
            IXLWorksheet? worksheet;
            if (options.Sheet != null)
            {
                worksheet = workbook.Worksheets
                    .FirstOrDefault(w => w.Name.Equals(options.Sheet, StringComparison.OrdinalIgnoreCase));

                if (worksheet == null)
                {
                    var available = string.Join(", ", workbook.Worksheets.Select(w => w.Name));
                    return new DocumentContent(
                        $"Worksheet '{options.Sheet}' not found. Available sheets: {available}",
                        sheetCount, false);
                }
            }
            else
            {
                worksheet = workbook.Worksheets.First();
            }

            var sb = new StringBuilder();
            sb.AppendLine($"[Sheet: {worksheet.Name}]");
            sb.AppendLine();

            bool truncated = false;
            string? truncationMessage = null;
            int rowsRead = 0;

            foreach (var row in worksheet.RowsUsed())
            {
                ct.ThrowIfCancellationRequested();

                if (rowsRead >= options.MaxRows)
                {
                    truncated = true;
                    truncationMessage = $"Row limit reached ({options.MaxRows:N0} rows). Sheet may have more data.";
                    break;
                }

                // Argha - 2026-02-27 - format cells as tab-separated values; use CellsUsed to skip empty cells
                var rowText = string.Join("\t", row.CellsUsed().Select(c => c.GetString()));

                if (sb.Length + rowText.Length + 1 > options.MaxCharsOutput)
                {
                    truncated = true;
                    truncationMessage = $"Output truncated at {options.MaxCharsOutput:N0} characters.";
                    break;
                }

                sb.AppendLine(rowText);
                rowsRead++;
            }

            // Argha - 2026-02-27 - PageCount = sheet count (logical page analogous to sheet)
            return new DocumentContent(sb.ToString(), sheetCount, truncated, truncationMessage);
        }, ct);
    }

    public async Task<DocumentInfo> GetInfoAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var fileInfo = new FileInfo(path);
            using var workbook = new XLWorkbook(path);

            return new DocumentInfo(
                Format: "Excel",
                Title: NullIfEmpty(workbook.Properties.Title),
                Author: NullIfEmpty(workbook.Properties.Author),
                Created: null,
                Modified: null,
                PageCount: null,
                WordCount: null,
                SheetCount: workbook.Worksheets.Count,
                SlideCount: null,
                FileSizeBytes: fileInfo.Length);
        }, ct);
    }

    // Argha - 2026-02-27 - Phase 8.3: table extraction deferred
    public Task<IEnumerable<DocumentTable>> ExtractTablesAsync(string path, CancellationToken ct) =>
        Task.FromResult(Enumerable.Empty<DocumentTable>());

    public async Task<IEnumerable<DocumentSearchMatch>> SearchAsync(
        string path, string query, bool caseSensitive, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook(path);
            var matches = new List<DocumentSearchMatch>();
            var comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            // Argha - 2026-02-27 - page number tracks sheet index (1-based)
            int sheetPage = 1;
            foreach (var worksheet in workbook.Worksheets)
            {
                ct.ThrowIfCancellationRequested();

                foreach (var cell in worksheet.CellsUsed())
                {
                    var cellText = cell.GetString();
                    if (string.IsNullOrEmpty(cellText)) continue;

                    int searchFrom = 0;
                    while (true)
                    {
                        int idx = cellText.IndexOf(query, searchFrom, comparison);
                        if (idx < 0) break;

                        // Argha - 2026-02-27 - include sheet name + cell address in context
                        int ctxStart = Math.Max(0, idx - 50);
                        int ctxEnd = Math.Min(cellText.Length, idx + query.Length + 50);
                        string ctxText = cellText[ctxStart..ctxEnd];
                        if (ctxStart > 0) ctxText = "..." + ctxText;
                        if (ctxEnd < cellText.Length) ctxText += "...";
                        string context = $"[{worksheet.Name}!{cell.Address}] {ctxText}";

                        matches.Add(new DocumentSearchMatch(sheetPage, query, context));
                        searchFrom = idx + query.Length;

                        // Argha - 2026-02-27 - cap at 100 matches to prevent response bloat
                        if (matches.Count >= 100) return (IEnumerable<DocumentSearchMatch>)matches;
                    }
                }

                sheetPage++;
            }

            return (IEnumerable<DocumentSearchMatch>)matches;
        }, ct);
    }

    public async Task<IEnumerable<WorksheetSummary>> ListSheetsAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var workbook = new XLWorkbook(path);
            var result = new List<WorksheetSummary>();

            foreach (var worksheet in workbook.Worksheets)
            {
                ct.ThrowIfCancellationRequested();

                // Argha - 2026-02-27 - empty sheets return (0, 0)
                var lastRow = worksheet.LastRowUsed();
                var lastCol = worksheet.LastColumnUsed();
                int rowCount = lastRow?.RowNumber() ?? 0;
                int colCount = lastCol?.ColumnNumber() ?? 0;
                result.Add(new WorksheetSummary(worksheet.Name, rowCount, colCount));
            }

            return (IEnumerable<WorksheetSummary>)result;
        }, ct);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
