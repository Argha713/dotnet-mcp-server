// Argha - 2026-02-26 - Phase 8: document processing tool (PDF, Word, Excel, PowerPoint)

using McpServer.Configuration;
using McpServer.Documents;
using McpServer.Progress;
using McpServer.Protocol;
using Microsoft.Extensions.Options;
using System.Text;

namespace McpServer.Tools;

/// <summary>
/// Tool for reading and analyzing enterprise documents (PDF, Word, Excel, PowerPoint).
/// Uses AllowedPaths from FileSystemSettings — same path validation as the filesystem tool.
/// </summary>
public class DocumentTool : ITool
{
    private readonly FileSystemSettings _fsSettings;
    private readonly DocumentSettings _docSettings;
    private readonly IEnumerable<IDocumentReader> _readers;

    public DocumentTool(
        IOptions<FileSystemSettings> fsSettings,
        IOptions<DocumentSettings> docSettings,
        IEnumerable<IDocumentReader> readers)
    {
        _fsSettings = fsSettings.Value;
        _docSettings = docSettings.Value;
        _readers = readers;
    }

    public string Name => "document";

    public string Description =>
        "Read and analyze enterprise documents. Supports PDF, Word (.docx), Excel (.xlsx), and PowerPoint (.pptx). " +
        "Extract text, metadata, tables, and search within files. Paths must be within configured allowed directories.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "Action to perform",
                Enum = new List<string> { "read", "info", "search" }
            },
            ["path"] = new()
            {
                Type = "string",
                Description = "Absolute or relative path to the document file"
            },
            ["pages"] = new()
            {
                Type = "string",
                Description = "PDF only: page range to read, e.g. '1-5' or '3'. Default: all pages."
            },
            ["query"] = new()
            {
                Type = "string",
                Description = "search action: text to find within the document"
            },
            ["case_sensitive"] = new()
            {
                Type = "string",
                Description = "search action: 'true' for case-sensitive search. Default: false.",
                Default = "false"
            }
        },
        Required = new List<string> { "action", "path" }
    };

    public async Task<ToolCallResult> ExecuteAsync(
        Dictionary<string, object>? arguments,
        IProgressReporter? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action")?.ToLower();
            var path = GetStringArg(arguments, "path");

            if (string.IsNullOrWhiteSpace(path))
                return Error("'path' parameter is required.");

            // Argha - 2026-02-26 - resolve and validate path against AllowedPaths (same logic as filesystem tool)
            var fullPath = ResolvePath(path);
            ValidatePath(fullPath);

            if (!File.Exists(fullPath))
                return Error($"File not found: {path}");

            // Argha - 2026-02-26 - enforce file size limit (larger than text limit because binary formats)
            var fileInfo = new FileInfo(fullPath);
            long maxBytes = (long)_docSettings.MaxFileSizeMb * 1024 * 1024;
            if (fileInfo.Length > maxBytes)
                return Error($"File too large ({fileInfo.Length / 1024 / 1024.0:F1} MB). Maximum size is {_docSettings.MaxFileSizeMb} MB.");

            // Argha - 2026-02-26 - find a reader for this file extension
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            var reader = _readers.FirstOrDefault(r =>
                r.SupportedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase));

            if (reader == null)
            {
                var supported = _readers
                    .SelectMany(r => r.SupportedExtensions)
                    .Distinct()
                    .OrderBy(e => e);
                return Error($"Unsupported file format '{ext}'. Supported formats: {string.Join(", ", supported)}.");
            }

            var text = action switch
            {
                "read" => await ExecuteReadAsync(reader, fullPath, arguments, cancellationToken),
                "info" => await ExecuteInfoAsync(reader, fullPath, fileInfo, cancellationToken),
                "search" => await ExecuteSearchAsync(reader, fullPath, arguments, cancellationToken),
                _ => $"Unknown action '{action}'. Use 'read', 'info', or 'search'."
            };

            return Ok(text);
        }
        catch (UnauthorizedAccessException)
        {
            return Error("Access denied. The path is outside allowed directories.");
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    // Argha - 2026-02-26 - read: extract text from the document
    private async Task<string> ExecuteReadAsync(
        IDocumentReader reader, string path,
        Dictionary<string, object>? arguments, CancellationToken ct)
    {
        var options = new DocumentReadOptions(
            PageRange: GetStringArg(arguments, "pages"),
            MaxCharsOutput: _docSettings.MaxOutputChars);

        var content = await reader.ReadTextAsync(path, options, ct);

        var sb = new StringBuilder(content.Text);
        if (content.Truncated && content.TruncationMessage != null)
        {
            sb.AppendLine();
            sb.AppendLine($"[{content.TruncationMessage}]");
        }

        return sb.ToString();
    }

    // Argha - 2026-02-26 - info: return document metadata
    private static async Task<string> ExecuteInfoAsync(
        IDocumentReader reader, string path, FileInfo fileInfo, CancellationToken ct)
    {
        var info = await reader.GetInfoAsync(path, ct);

        var sb = new StringBuilder();
        sb.AppendLine($"File:     {Path.GetFileName(path)}");
        sb.AppendLine($"Format:   {info.Format}");
        sb.AppendLine($"Size:     {FormatSize(info.FileSizeBytes)}");
        if (info.Title != null) sb.AppendLine($"Title:    {info.Title}");
        if (info.Author != null) sb.AppendLine($"Author:   {info.Author}");
        if (info.Created.HasValue) sb.AppendLine($"Created:  {info.Created:yyyy-MM-dd HH:mm:ss} UTC");
        if (info.Modified.HasValue) sb.AppendLine($"Modified: {info.Modified:yyyy-MM-dd HH:mm:ss} UTC");
        if (info.PageCount.HasValue) sb.AppendLine($"Pages:    {info.PageCount}");
        if (info.SlideCount.HasValue) sb.AppendLine($"Slides:   {info.SlideCount}");
        if (info.SheetCount.HasValue) sb.AppendLine($"Sheets:   {info.SheetCount}");
        if (info.WordCount.HasValue) sb.AppendLine($"Words:    {info.WordCount:N0}");

        return sb.ToString().TrimEnd();
    }

    // Argha - 2026-02-26 - search: find text occurrences with context
    private static async Task<string> ExecuteSearchAsync(
        IDocumentReader reader, string path,
        Dictionary<string, object>? arguments, CancellationToken ct)
    {
        var query = GetStringArg(arguments, "query");
        if (string.IsNullOrWhiteSpace(query))
            return "Error: 'query' parameter is required for the 'search' action.";

        var caseSensitive = GetStringArg(arguments, "case_sensitive")?.ToLower() == "true";

        var matches = (await reader.SearchAsync(path, query, caseSensitive, ct)).ToList();

        if (matches.Count == 0)
            return $"No matches found for '{query}' in {Path.GetFileName(path)}.";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {matches.Count} match(es) for '{query}' in {Path.GetFileName(path)}:");
        sb.AppendLine();

        foreach (var match in matches)
        {
            sb.AppendLine($"  Page {match.Page}: {match.Context}");
        }

        return sb.ToString().TrimEnd();
    }

    // Argha - 2026-02-26 - mirrors FileSystemTool.ResolvePath: absolute paths used as-is, relative resolved against AllowedPaths
    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        foreach (var allowed in _fsSettings.AllowedPaths)
        {
            var combined = Path.GetFullPath(Path.Combine(allowed, path));
            if (File.Exists(combined) || Directory.Exists(combined))
                return combined;
        }

        var basePath = _fsSettings.AllowedPaths.FirstOrDefault() ?? Environment.CurrentDirectory;
        return Path.GetFullPath(Path.Combine(basePath, path));
    }

    // Argha - 2026-02-26 - mirrors FileSystemTool.ValidatePath: trailing-separator fix prevents prefix attacks
    private void ValidatePath(string fullPath)
    {
        var normalizedPath = Path.GetFullPath(fullPath);

        var isAllowed = _fsSettings.AllowedPaths.Any(allowed =>
        {
            var normalizedAllowed = Path.GetFullPath(allowed);
            if (!normalizedAllowed.EndsWith(Path.DirectorySeparatorChar))
                normalizedAllowed += Path.DirectorySeparatorChar;

            return normalizedPath.StartsWith(normalizedAllowed, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.Equals(Path.GetFullPath(allowed), StringComparison.OrdinalIgnoreCase);
        });

        if (!isAllowed)
            throw new UnauthorizedAccessException($"Path '{fullPath}' is outside allowed directories.");
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private static ToolCallResult Ok(string text) => new()
    {
        Content = new List<ContentBlock> { new() { Type = "text", Text = text } }
    };

    private static ToolCallResult Error(string message) => new()
    {
        Content = new List<ContentBlock> { new() { Type = "text", Text = $"Error: {message}" } },
        IsError = true
    };

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value)) return null;
        return value?.ToString();
    }
}
