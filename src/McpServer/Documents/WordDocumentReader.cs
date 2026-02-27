// Argha - 2026-02-27 - Phase 8.2: Word document text extraction, metadata, and search via OpenXml SDK

using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace McpServer.Documents;

/// <summary>
/// Reads Word (.docx) documents using DocumentFormat.OpenXml. No native dependencies.
/// </summary>
public class WordDocumentReader : IDocumentReader
{
    public string[] SupportedExtensions => new[] { ".docx" };

    public async Task<DocumentContent> ReadTextAsync(string path, DocumentReadOptions options, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var doc = WordprocessingDocument.Open(path, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            int pageCount = GetPageCount(doc);

            var sb = new StringBuilder();
            sb.AppendLine($"Document: {Path.GetFileName(path)}");
            sb.AppendLine();

            bool truncated = false;
            string? truncationMessage = null;

            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                ct.ThrowIfCancellationRequested();

                // Argha - 2026-02-27 - concatenate all Text runs within the paragraph
                var paraText = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));

                if (sb.Length + paraText.Length + 1 > options.MaxCharsOutput)
                {
                    var remaining = options.MaxCharsOutput - sb.Length;
                    if (remaining > 0)
                        sb.Append(paraText[..remaining]);
                    truncated = true;
                    truncationMessage = $"Output truncated at {options.MaxCharsOutput:N0} characters.";
                    break;
                }

                sb.AppendLine(paraText);
            }

            return new DocumentContent(
                Text: sb.ToString(),
                PageCount: pageCount,
                Truncated: truncated,
                TruncationMessage: truncationMessage);
        }, ct);
    }

    public async Task<DocumentInfo> GetInfoAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var fileInfo = new FileInfo(path);
            using var doc = WordprocessingDocument.Open(path, false);

            // Argha - 2026-02-27 - use PackageProperties for standard Dublin Core metadata
            var props = doc.PackageProperties;
            string? title = NullIfEmpty(props.Title);
            string? author = NullIfEmpty(props.Creator);
            DateTimeOffset? created = props.Created.HasValue
                ? new DateTimeOffset(props.Created.Value, TimeSpan.Zero)
                : null;
            DateTimeOffset? modified = props.Modified.HasValue
                ? new DateTimeOffset(props.Modified.Value, TimeSpan.Zero)
                : null;

            // Argha - 2026-02-27 - page and word count come from ExtendedFilePropertiesPart
            var extended = doc.ExtendedFilePropertiesPart?.Properties;
            int? pages = null;
            int? words = null;
            if (extended?.Pages?.Text != null && int.TryParse(extended.Pages.Text, out var p)) pages = p;
            if (extended?.Words?.Text != null && int.TryParse(extended.Words.Text, out var w)) words = w;

            // Argha - 2026-02-27 - fallback to 1 page when metadata is absent (new/minimal documents)
            pages ??= GetPageCount(doc);

            return new DocumentInfo(
                Format: "Word",
                Title: title,
                Author: author,
                Created: created,
                Modified: modified,
                PageCount: pages,
                WordCount: words,
                SheetCount: null,
                SlideCount: null,
                FileSizeBytes: fileInfo.Length);
        }, ct);
    }

    // Argha - 2026-02-27 - Phase 8.3: extract tables from Word documents using OpenXml Table elements
    public async Task<IEnumerable<DocumentTable>> ExtractTablesAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var doc = WordprocessingDocument.Open(path, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var tables = new List<DocumentTable>();

            foreach (var table in body.Descendants<Table>())
            {
                ct.ThrowIfCancellationRequested();

                var rows = new List<List<string>>();
                foreach (var row in table.Descendants<TableRow>())
                {
                    // Argha - 2026-02-27 - each cell may contain multiple paragraphs; join them with a space
                    var cells = row.Descendants<TableCell>()
                        .Select(cell =>
                        {
                            var paragraphs = cell.Descendants<Paragraph>()
                                .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text)))
                                .Where(s => !string.IsNullOrEmpty(s));
                            return string.Join(" ", paragraphs);
                        })
                        .ToList();

                    if (cells.Count > 0)
                        rows.Add(cells);
                }

                if (rows.Count > 0)
                    tables.Add(new DocumentTable(null, rows));
            }

            return (IEnumerable<DocumentTable>)tables;
        }, ct);
    }

    public async Task<IEnumerable<DocumentSearchMatch>> SearchAsync(
        string path, string query, bool caseSensitive, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var doc = WordprocessingDocument.Open(path, false);
            var body = doc.MainDocumentPart!.Document.Body!;
            var matches = new List<DocumentSearchMatch>();
            var comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            // Argha - 2026-02-27 - extract full document text; Word has no reliable per-page API
            // so all matches are reported at page 1
            var fullText = string.Concat(
                body.Descendants<Paragraph>()
                    .Select(pg => string.Concat(pg.Descendants<Text>().Select(t => t.Text)) + "\n"));

            int searchFrom = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                int idx = fullText.IndexOf(query, searchFrom, comparison);
                if (idx < 0) break;

                // Argha - 2026-02-27 - 50 chars of context on each side of the match
                int ctxStart = Math.Max(0, idx - 50);
                int ctxEnd = Math.Min(fullText.Length, idx + query.Length + 50);
                string context = fullText[ctxStart..ctxEnd];
                if (ctxStart > 0) context = "..." + context;
                if (ctxEnd < fullText.Length) context += "...";

                matches.Add(new DocumentSearchMatch(1, query, context));
                searchFrom = idx + query.Length;

                // Argha - 2026-02-27 - cap at 100 matches to prevent response bloat
                if (matches.Count >= 100) break;
            }

            return (IEnumerable<DocumentSearchMatch>)matches;
        }, ct);
    }

    // Argha - 2026-02-27 - try ExtendedFilePropertiesPart first; new/minimal docs may lack it
    private static int GetPageCount(WordprocessingDocument doc)
    {
        var pagesText = doc.ExtendedFilePropertiesPart?.Properties?.Pages?.Text;
        if (pagesText != null && int.TryParse(pagesText, out var pages) && pages > 0)
            return pages;
        return 1;
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
