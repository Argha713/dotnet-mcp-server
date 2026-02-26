// Argha - 2026-02-26 - Phase 8: abstraction over format-specific document readers

namespace McpServer.Documents;

/// <summary>
/// Reads a single document format. Implementations are per-format (PDF, DOCX, XLSX, PPTX).
/// </summary>
public interface IDocumentReader
{
    /// <summary>
    /// File extensions this reader handles, lowercase with leading dot (e.g. ".pdf", ".docx").
    /// </summary>
    string[] SupportedExtensions { get; }

    /// <summary>
    /// Extract text content from a document.
    /// </summary>
    Task<DocumentContent> ReadTextAsync(string path, DocumentReadOptions options, CancellationToken ct);

    /// <summary>
    /// Return document metadata without reading full content.
    /// </summary>
    Task<DocumentInfo> GetInfoAsync(string path, CancellationToken ct);

    /// <summary>
    /// Extract tables from the document.
    /// </summary>
    Task<IEnumerable<DocumentTable>> ExtractTablesAsync(string path, CancellationToken ct);

    /// <summary>
    /// Find all occurrences of <paramref name="query"/> in the document.
    /// </summary>
    Task<IEnumerable<DocumentSearchMatch>> SearchAsync(string path, string query, bool caseSensitive, CancellationToken ct);

    /// <summary>
    /// List worksheets (Excel only). Other formats return an empty enumerable.
    /// </summary>
    Task<IEnumerable<WorksheetSummary>> ListSheetsAsync(string path, CancellationToken ct) =>
        Task.FromResult(Enumerable.Empty<WorksheetSummary>());
}
