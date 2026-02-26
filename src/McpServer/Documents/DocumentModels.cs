// Argha - 2026-02-26 - Phase 8: document processing model types used by IDocumentReader implementations

namespace McpServer.Documents;

/// <summary>
/// Options controlling how a document is read.
/// </summary>
public record DocumentReadOptions(
    /// <summary>
    /// Excel: specific worksheet name to read. Null = first sheet.
    /// </summary>
    string? Sheet = null,
    /// <summary>
    /// PDF: page range to extract (e.g. "1-5", "3"). Null = all pages.
    /// </summary>
    string? PageRange = null,
    /// <summary>
    /// Maximum number of output characters before truncation.
    /// </summary>
    int MaxCharsOutput = 50_000,
    /// <summary>
    /// Excel: maximum rows to return per sheet read.
    /// </summary>
    int MaxRows = 5_000
);

/// <summary>
/// Extracted text content from a document.
/// </summary>
public record DocumentContent(
    string Text,
    int PageCount,
    bool Truncated,
    string? TruncationMessage = null
);

/// <summary>
/// Metadata about a document.
/// </summary>
public record DocumentInfo(
    string Format,
    string? Title,
    string? Author,
    DateTimeOffset? Created,
    DateTimeOffset? Modified,
    int? PageCount,
    int? WordCount,
    int? SheetCount,
    int? SlideCount,
    long FileSizeBytes
);

/// <summary>
/// A table extracted from a document, expressed as rows of cells.
/// </summary>
public record DocumentTable(
    string? Title,
    List<List<string>> Rows
);

/// <summary>
/// A search match within a document with surrounding context.
/// </summary>
public record DocumentSearchMatch(
    int Page,
    string MatchedText,
    string Context
);

/// <summary>
/// Summary of a single worksheet (name + dimensions).
/// </summary>
public record WorksheetSummary(
    string Name,
    int RowCount,
    int ColumnCount
);
