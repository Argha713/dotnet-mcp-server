// Argha - 2026-02-27 - Phase 8.2: PowerPoint document text extraction, metadata, and search via OpenXml SDK

using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace McpServer.Documents;

/// <summary>
/// Reads PowerPoint (.pptx) presentations using DocumentFormat.OpenXml. No native dependencies.
/// </summary>
public class PowerPointDocumentReader : IDocumentReader
{
    public string[] SupportedExtensions => new[] { ".pptx" };

    public async Task<DocumentContent> ReadTextAsync(string path, DocumentReadOptions options, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var doc = PresentationDocument.Open(path, false);
            var presentationPart = doc.PresentationPart!;
            var slideIdList = presentationPart.Presentation.SlideIdList;
            var slideIds = slideIdList?.ChildElements.OfType<SlideId>().ToList()
                           ?? new List<SlideId>();
            int totalSlides = slideIds.Count;

            var (startSlide, endSlide) = ParsePageRange(options.PageRange, totalSlides);

            var sb = new StringBuilder();
            sb.AppendLine($"Document: {Path.GetFileName(path)}");
            sb.AppendLine($"Slides: {totalSlides} total" +
                          (options.PageRange != null ? $", reading slides {startSlide}–{endSlide}" : string.Empty));
            sb.AppendLine();

            bool truncated = false;
            string? truncationMessage = null;

            for (int i = startSlide - 1; i < Math.Min(endSlide, slideIds.Count); i++)
            {
                ct.ThrowIfCancellationRequested();

                var slideId = slideIds[i];
                if (slideId.RelationshipId?.Value is not string relId) continue;

                var slidePart = presentationPart.GetPartById(relId) as SlidePart;
                if (slidePart?.Slide == null) continue;

                // Argha - 2026-02-27 - extract all a:t (Drawing.Text) elements from shapes on the slide
                var slideTexts = slidePart.Slide
                    .Descendants<A.Text>()
                    .Select(t => t.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t));

                var slideText = string.Join(" ", slideTexts);

                if (sb.Length + slideText.Length + 1 > options.MaxCharsOutput)
                {
                    var remaining = options.MaxCharsOutput - sb.Length;
                    if (remaining > 0)
                        sb.Append(slideText[..remaining]);
                    truncated = true;
                    truncationMessage = $"Output truncated at {options.MaxCharsOutput:N0} characters. " +
                                       $"{endSlide - i} slide(s) not fully read.";
                    break;
                }

                if (totalSlides > 1)
                    sb.AppendLine($"--- Slide {i + 1} ---");
                sb.AppendLine(slideText);
                sb.AppendLine();
            }

            return new DocumentContent(
                Text: sb.ToString(),
                PageCount: totalSlides,
                Truncated: truncated,
                TruncationMessage: truncationMessage);
        }, ct);
    }

    public async Task<DocumentInfo> GetInfoAsync(string path, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var fileInfo = new FileInfo(path);
            using var doc = PresentationDocument.Open(path, false);

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

            int? slideCount = doc.PresentationPart?
                .Presentation.SlideIdList?
                .ChildElements.OfType<SlideId>().Count();

            return new DocumentInfo(
                Format: "PowerPoint",
                Title: title,
                Author: author,
                Created: created,
                Modified: modified,
                PageCount: null,
                WordCount: null,
                SheetCount: null,
                SlideCount: slideCount,
                FileSizeBytes: fileInfo.Length);
        }, ct);
    }

    // Argha - 2026-02-27 - Phase 8.3: table extraction deferred
    public Task<IEnumerable<DocumentTable>> ExtractTablesAsync(string path, CancellationToken ct) =>
        Task.FromResult(Enumerable.Empty<DocumentTable>());

    // Argha - 2026-02-27 - list_sheets not applicable; explicit implementation so callers can invoke
    // through the concrete type (C# default interface methods are only reachable via the interface)
    public Task<IEnumerable<WorksheetSummary>> ListSheetsAsync(string path, CancellationToken ct) =>
        Task.FromResult(Enumerable.Empty<WorksheetSummary>());

    public async Task<IEnumerable<DocumentSearchMatch>> SearchAsync(
        string path, string query, bool caseSensitive, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            using var doc = PresentationDocument.Open(path, false);
            var presentationPart = doc.PresentationPart!;
            var slideIdList = presentationPart.Presentation.SlideIdList;
            var slideIds = slideIdList?.ChildElements.OfType<SlideId>().ToList()
                           ?? new List<SlideId>();

            var matches = new List<DocumentSearchMatch>();
            var comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            for (int i = 0; i < slideIds.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var slideId = slideIds[i];
                if (slideId.RelationshipId?.Value is not string relId) continue;

                var slidePart = presentationPart.GetPartById(relId) as SlidePart;
                if (slidePart?.Slide == null) continue;

                var slideText = string.Join(" ",
                    slidePart.Slide.Descendants<A.Text>()
                        .Select(t => t.Text)
                        .Where(t => !string.IsNullOrWhiteSpace(t)));

                int slideNum = i + 1;
                int searchFrom = 0;
                while (true)
                {
                    int idx = slideText.IndexOf(query, searchFrom, comparison);
                    if (idx < 0) break;

                    // Argha - 2026-02-27 - 50 chars of context on each side of the match
                    int ctxStart = Math.Max(0, idx - 50);
                    int ctxEnd = Math.Min(slideText.Length, idx + query.Length + 50);
                    string context = slideText[ctxStart..ctxEnd];
                    if (ctxStart > 0) context = "..." + context;
                    if (ctxEnd < slideText.Length) context += "...";

                    matches.Add(new DocumentSearchMatch(slideNum, query, context));
                    searchFrom = idx + query.Length;

                    // Argha - 2026-02-27 - cap at 100 matches to prevent response bloat
                    if (matches.Count >= 100) return (IEnumerable<DocumentSearchMatch>)matches;
                }
            }

            return (IEnumerable<DocumentSearchMatch>)matches;
        }, ct);
    }

    // Argha - 2026-02-27 - parse "1-5", "3", or null → (startSlide, endSlide) clamped to [1..totalSlides]
    // Same logic as PdfDocumentReader.ParsePageRange — copied here to avoid cross-class dependency
    internal static (int Start, int End) ParsePageRange(string? pageRange, int totalSlides)
    {
        if (totalSlides == 0) return (1, 0);
        if (string.IsNullOrWhiteSpace(pageRange))
            return (1, totalSlides);

        var parts = pageRange.Trim().Split('-');

        if (parts.Length == 1 && int.TryParse(parts[0].Trim(), out int single))
        {
            single = Math.Clamp(single, 1, totalSlides);
            return (single, single);
        }

        if (parts.Length == 2)
        {
            int.TryParse(parts[0].Trim(), out int start);
            int.TryParse(parts[1].Trim(), out int end);
            start = Math.Clamp(start, 1, totalSlides);
            end = end == 0 ? totalSlides : Math.Clamp(end, start, totalSlides);
            return (start, end);
        }

        return (1, totalSlides);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
