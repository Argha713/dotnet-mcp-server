// Argha - 2026-02-26 - Phase 8.1: PDF text extraction, metadata, and search via PdfPig

using System.Text;
using UglyToad.PdfPig;

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

    // Argha - 2026-02-26 - Phase 8.3: PDF table extraction deferred to Phase 8.3
    public Task<IEnumerable<DocumentTable>> ExtractTablesAsync(string path, CancellationToken ct) =>
        Task.FromResult(Enumerable.Empty<DocumentTable>());

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
