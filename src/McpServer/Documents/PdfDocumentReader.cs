// Argha - 2026-02-26 - Phase 8.1: PDF text extraction, metadata, and search via PdfPig
// Argha - 2026-02-27 - Phase 8.3: heuristic table extraction using word bounding boxes

using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace McpServer.Documents;

/// <summary>
/// Reads PDF documents using PdfPig. Pure .NET, no native dependencies.
/// </summary>
public class PdfDocumentReader : IDocumentReader
{
    public string[] SupportedExtensions => new[] { ".pdf" };

    public async Task<DocumentContent> ReadTextAsync(string path, DocumentReadOptions options, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var doc = PdfDocument.Open(path);
            int totalPages = doc.NumberOfPages;
            var (startPage, endPage) = ParsePageRange(options.PageRange, totalPages);

            var sb = new StringBuilder();
            sb.AppendLine($"Document: {Path.GetFileName(path)}");
            sb.AppendLine($"Pages: {totalPages} total" + (options.PageRange != null ? $", reading pages {startPage}–{endPage}" : string.Empty));
            sb.AppendLine();

            bool truncated = false;
            string? truncationMessage = null;

            for (int pageNum = startPage; pageNum <= endPage; pageNum++)
            {
                ct.ThrowIfCancellationRequested();

                var page = doc.GetPage(pageNum);
                var pageText = string.Join(" ", page.GetWords().Select(w => w.Text));

                if (sb.Length + pageText.Length > options.MaxCharsOutput)
                {
                    var remaining = options.MaxCharsOutput - sb.Length;
                    if (remaining > 0)
                        sb.Append(pageText[..remaining]);
                    truncated = true;
                    truncationMessage = $"Output truncated at {options.MaxCharsOutput:N0} characters. {endPage - pageNum + 1} page(s) not fully read.";
                    break;
                }

                if (totalPages > 1)
                    sb.AppendLine($"--- Page {pageNum} ---");
                sb.AppendLine(pageText);
                sb.AppendLine();
            }

            if (sb.Length <= 50)
            {
                // Argha - 2026-02-26 - likely a scanned (image-only) PDF; no text layer
                return new DocumentContent(
                    Text: $"No text could be extracted from '{Path.GetFileName(path)}'. The file may be a scanned (image-only) PDF with no text layer.",
                    PageCount: totalPages,
                    Truncated: false);
            }

            return new DocumentContent(
                Text: sb.ToString(),
                PageCount: totalPages,
                Truncated: truncated,
                TruncationMessage: truncationMessage);
        }, ct);
    }

    public async Task<DocumentInfo> GetInfoAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var fileInfo = new FileInfo(path);
            using var doc = PdfDocument.Open(path);
            var info = doc.Information;

            return new DocumentInfo(
                Format: "PDF",
                Title: NullIfEmpty(info.Title),
                Author: NullIfEmpty(info.Author),
                Created: ParsePdfDate(info.CreationDate),
                Modified: ParsePdfDate(info.ModifiedDate),
                PageCount: doc.NumberOfPages,
                WordCount: null,
                SheetCount: null,
                SlideCount: null,
                FileSizeBytes: fileInfo.Length);
        }, ct);
    }

    // Argha - 2026-02-27 - Phase 8.3: heuristic table detection from word bounding boxes
    // Words separated by >CellGapThreshold pts horizontally = separate cells.
    // Rows with ≥2 consistently X-aligned cells across ≥2 rows are emitted as a table.
    public async Task<IEnumerable<DocumentTable>> ExtractTablesAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var doc = PdfDocument.Open(path);
            var allTables = new List<DocumentTable>();

            for (int pageNum = 1; pageNum <= doc.NumberOfPages; pageNum++)
            {
                ct.ThrowIfCancellationRequested();
                var page = doc.GetPage(pageNum);
                allTables.AddRange(DetectTablesOnPage(page));
            }

            return (IEnumerable<DocumentTable>)allTables;
        }, ct);
    }

    // Argha - 2026-02-27 - detect grid-like text arrangements on a single PDF page.
    // Words within rowTolerance pts in Y → same row; words separated by >cellGapThreshold pts in X → separate cells.
    private static List<DocumentTable> DetectTablesOnPage(Page page)
    {
        const double rowTolerance = 8.0;
        const double cellGapThreshold = 40.0;
        const int minRows = 2;
        const int minCols = 2;

        var words = page.GetWords()
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        if (words.Count < minRows * minCols)
            return new List<DocumentTable>();

        // Step 1: group words into rows by Y coordinate proximity
        // Each bucket stores the reference Y and a mutable list of word spans
        var rowBuckets = new List<(double yRef, List<(double xStart, double xEnd, string text)> spans)>();
        foreach (var word in words)
        {
            double y = word.BoundingBox.Bottom;
            var bucket = rowBuckets.FirstOrDefault(b => Math.Abs(b.yRef - y) <= rowTolerance);
            if (bucket.spans != null)
            {
                // Argha - 2026-02-27 - List<T> is a reference type; Add mutates the shared instance
                bucket.spans.Add((word.BoundingBox.Left, word.BoundingBox.Right, word.Text));
            }
            else
            {
                rowBuckets.Add((y, new List<(double, double, string)>
                    { (word.BoundingBox.Left, word.BoundingBox.Right, word.Text) }));
            }
        }

        // Step 2: within each row, merge adjacent words into cells (large X gap → new cell)
        var cellRows = rowBuckets.Select(bucket =>
        {
            var sorted = bucket.spans.OrderBy(w => w.xStart).ToList();
            var cells = new List<(double xStart, string text)>();
            var curText = sorted[0].text;
            var curX = sorted[0].xStart;
            var curRight = sorted[0].xEnd;

            for (int i = 1; i < sorted.Count; i++)
            {
                double gap = sorted[i].xStart - curRight;
                if (gap >= cellGapThreshold)
                {
                    cells.Add((curX, curText));
                    curText = sorted[i].text;
                    curX = sorted[i].xStart;
                }
                else
                {
                    curText += " " + sorted[i].text;
                }
                curRight = sorted[i].xEnd;
            }
            cells.Add((curX, curText));
            return cells;
        }).ToList();

        // Step 3: keep only rows with ≥2 cells
        var multiColRows = cellRows.Where(r => r.Count >= minCols).ToList();
        if (multiColRows.Count < minRows) return new List<DocumentTable>();

        // Step 4: require consistent column count across rows
        int colCount = multiColRows[0].Count;
        var alignedRows = multiColRows.Where(r => r.Count == colCount).ToList();
        if (alignedRows.Count < minRows) return new List<DocumentTable>();

        // Step 5: verify column X alignment (first row is reference; ±20pt tolerance)
        var refX = alignedRows[0].Select(c => c.xStart).ToList();
        bool wellAligned = alignedRows.All(row =>
            row.Select(c => c.xStart).Zip(refX).All(pair => Math.Abs(pair.First - pair.Second) <= 20.0));

        if (!wellAligned) return new List<DocumentTable>();

        var tableRows = alignedRows.Select(row => row.Select(c => c.text).ToList()).ToList();
        return new List<DocumentTable> { new DocumentTable(null, tableRows) };
    }

    public async Task<IEnumerable<DocumentSearchMatch>> SearchAsync(
        string path, string query, bool caseSensitive, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var doc = PdfDocument.Open(path);
            var matches = new List<DocumentSearchMatch>();
            var comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            for (int pageNum = 1; pageNum <= doc.NumberOfPages; pageNum++)
            {
                ct.ThrowIfCancellationRequested();

                var page = doc.GetPage(pageNum);
                var pageText = string.Join(" ", page.GetWords().Select(w => w.Text));

                int searchFrom = 0;
                while (true)
                {
                    int idx = pageText.IndexOf(query, searchFrom, comparison);
                    if (idx < 0) break;

                    // Argha - 2026-02-26 - 50 chars of context on each side of the match
                    int ctxStart = Math.Max(0, idx - 50);
                    int ctxEnd = Math.Min(pageText.Length, idx + query.Length + 50);
                    string context = pageText[ctxStart..ctxEnd];
                    if (ctxStart > 0) context = "..." + context;
                    if (ctxEnd < pageText.Length) context += "...";

                    matches.Add(new DocumentSearchMatch(pageNum, query, context));
                    searchFrom = idx + query.Length;

                    // Argha - 2026-02-26 - cap at 100 matches to prevent response bloat
                    if (matches.Count >= 100) return (IEnumerable<DocumentSearchMatch>)matches;
                }
            }

            return (IEnumerable<DocumentSearchMatch>)matches;
        }, ct);
    }

    // Argha - 2026-02-26 - parse PDF date string ("D:YYYYMMDDHHmmSS...") to DateTimeOffset
    private static DateTimeOffset? ParsePdfDate(string? pdfDate)
    {
        if (string.IsNullOrWhiteSpace(pdfDate)) return null;

        var s = pdfDate.Trim();
        if (s.StartsWith("D:", StringComparison.OrdinalIgnoreCase))
            s = s[2..];

        if (s.Length >= 8
            && int.TryParse(s[..4], out int year)
            && int.TryParse(s[4..6], out int month)
            && int.TryParse(s[6..8], out int day))
        {
            int hour = 0, minute = 0, second = 0;
            if (s.Length >= 10) int.TryParse(s[8..10], out hour);
            if (s.Length >= 12) int.TryParse(s[10..12], out minute);
            if (s.Length >= 14) int.TryParse(s[12..14], out second);
            try { return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero); }
            catch { return null; }
        }

        if (DateTimeOffset.TryParse(s, out var dt)) return dt;
        return null;
    }

    // Argha - 2026-02-26 - parse "1-5", "3", or null → (startPage, endPage) clamped to [1..totalPages]
    internal static (int Start, int End) ParsePageRange(string? pageRange, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(pageRange))
            return (1, totalPages);

        var parts = pageRange.Trim().Split('-');

        if (parts.Length == 1 && int.TryParse(parts[0].Trim(), out int single))
        {
            single = Math.Clamp(single, 1, totalPages);
            return (single, single);
        }

        if (parts.Length == 2)
        {
            int.TryParse(parts[0].Trim(), out int start);
            int.TryParse(parts[1].Trim(), out int end);
            start = Math.Clamp(start, 1, totalPages);
            end = end == 0 ? totalPages : Math.Clamp(end, start, totalPages);
            return (start, end);
        }

        return (1, totalPages);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
