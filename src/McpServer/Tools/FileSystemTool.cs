using McpServer.Progress;
using McpServer.Protocol;
using McpServer.Configuration;
using Microsoft.Extensions.Options;

namespace McpServer.Tools;

/// <summary>
/// Tool for safe file system operations within allowed directories
/// </summary>
public class FileSystemTool : ITool
{
    private readonly FileSystemSettings _settings;

    public FileSystemTool(IOptions<FileSystemSettings> settings)
    {
        _settings = settings.Value;
    }

    public string Name => "filesystem";

    public string Description => "Read files, list directories, and search for files within allowed directories. Use this for accessing local files and folder contents.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "read", "list", "search", "info", "allowed_paths" }
            },
            ["path"] = new()
            {
                Type = "string",
                Description = "File or directory path (relative to allowed directories)"
            },
            ["pattern"] = new()
            {
                Type = "string",
                Description = "Search pattern for 'search' action (e.g., '*.txt', '*.cs')"
            },
            ["recursive"] = new()
            {
                Type = "string",
                Description = "Whether to search recursively (true/false)",
                Default = "false"
            }
        },
        Required = new List<string> { "action" }
    };

    // Argha - 2026-02-24 - added IProgressReporter; used by read and search sub-operations
    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, IProgressReporter? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action") ?? "list";

            var result = action.ToLower() switch
            {
                "read" => await ReadFileAsync(arguments, progress, cancellationToken),
                "list" => ListDirectory(arguments),
                "search" => SearchFiles(arguments, progress),
                "info" => GetFileInfo(arguments),
                "allowed_paths" => GetAllowedPaths(),
                _ => $"Unknown action: {action}. Use 'read', 'list', 'search', 'info', or 'allowed_paths'."
            };

            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = result }
                }
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = "Error: Access denied. The path is outside allowed directories." }
                },
                IsError = true
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = $"Error: {ex.Message}" }
                },
                IsError = true
            };
        }
    }

    // Argha - 2026-02-24 - progress added: 0=start, fileInfo.Length=done (units: bytes)
    private async Task<string> ReadFileAsync(Dictionary<string, object>? arguments, IProgressReporter? progress, CancellationToken cancellationToken)
    {
        var path = GetStringArg(arguments, "path");
        if (string.IsNullOrEmpty(path))
            return "Error: 'path' parameter is required.";

        var fullPath = ResolvePath(path);
        ValidatePath(fullPath);

        if (!File.Exists(fullPath))
            return $"Error: File not found: {path}";

        var fileInfo = new FileInfo(fullPath);

        // Limit file size for reading
        const long maxSize = 1024 * 1024; // 1 MB
        if (fileInfo.Length > maxSize)
            return $"Error: File too large ({fileInfo.Length / 1024}KB). Maximum size is {maxSize / 1024}KB.";

        progress?.Report(0, fileInfo.Length);
        var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
        // Argha - 2026-02-24 - report full size on completion so client progress bar reaches 100%
        progress?.Report(fileInfo.Length, fileInfo.Length);
        return $"File: {path}\nSize: {fileInfo.Length} bytes\n\n--- Content ---\n{content}";
    }

    private string ListDirectory(Dictionary<string, object>? arguments)
    {
        var path = GetStringArg(arguments, "path");
        
        if (string.IsNullOrEmpty(path))
        {
            // List all allowed directories
            return "Allowed directories:\n" + 
                   string.Join("\n", _settings.AllowedPaths.Select(p => $"  📁 {p}"));
        }

        var fullPath = ResolvePath(path);
        ValidatePath(fullPath);

        if (!Directory.Exists(fullPath))
            return $"Error: Directory not found: {path}";

        var entries = new List<string>();
        
        foreach (var dir in Directory.GetDirectories(fullPath))
        {
            entries.Add($"  📁 {Path.GetFileName(dir)}/");
        }

        foreach (var file in Directory.GetFiles(fullPath))
        {
            var info = new FileInfo(file);
            var size = FormatSize(info.Length);
            entries.Add($"  📄 {Path.GetFileName(file)} ({size})");
        }

        if (entries.Count == 0)
            return $"Directory '{path}' is empty.";

        return $"Contents of '{path}':\n{string.Join("\n", entries)}";
    }

    // Argha - 2026-02-24 - progress added: reports files found so far every 10 files (total unknown)
    private string SearchFiles(Dictionary<string, object>? arguments, IProgressReporter? progress)
    {
        var path = GetStringArg(arguments, "path") ?? _settings.AllowedPaths.FirstOrDefault() ?? ".";
        var pattern = GetStringArg(arguments, "pattern") ?? "*.*";
        var recursive = GetStringArg(arguments, "recursive")?.ToLower() == "true";

        var fullPath = ResolvePath(path);
        ValidatePath(fullPath);

        if (!Directory.Exists(fullPath))
            return $"Error: Directory not found: {path}";

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = new List<string>();
        var scanned = 0;
        foreach (var file in Directory.EnumerateFiles(fullPath, pattern, searchOption))
        {
            files.Add(file);
            scanned++;
            // Argha - 2026-02-24 - total is unknown for search; omit second arg so client shows open-ended spinner
            if (scanned % 10 == 0)
                progress?.Report(scanned);
            if (scanned >= 100)
                break; // Limit results
        }

        if (files.Count == 0)
            return $"No files matching '{pattern}' found in '{path}'.";

        var results = files.Select(f =>
        {
            var relativePath = Path.GetRelativePath(fullPath, f);
            var info = new FileInfo(f);
            return $"  📄 {relativePath} ({FormatSize(info.Length)})";
        });

        var countNote = files.Count == 100 ? " (showing first 100)" : "";
        return $"Found {files.Count} file(s) matching '{pattern}'{countNote}:\n{string.Join("\n", results)}";
    }

    private string GetFileInfo(Dictionary<string, object>? arguments)
    {
        var path = GetStringArg(arguments, "path");
        if (string.IsNullOrEmpty(path))
            return "Error: 'path' parameter is required.";

        var fullPath = ResolvePath(path);
        ValidatePath(fullPath);

        if (File.Exists(fullPath))
        {
            var info = new FileInfo(fullPath);
            return $"File: {path}\n" +
                   $"  Size: {FormatSize(info.Length)} ({info.Length} bytes)\n" +
                   $"  Created: {info.CreationTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"  Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"  Extension: {info.Extension}\n" +
                   $"  Read-only: {info.IsReadOnly}";
        }
        else if (Directory.Exists(fullPath))
        {
            var info = new DirectoryInfo(fullPath);
            var fileCount = Directory.GetFiles(fullPath).Length;
            var dirCount = Directory.GetDirectories(fullPath).Length;
            return $"Directory: {path}\n" +
                   $"  Created: {info.CreationTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"  Modified: {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"  Contains: {fileCount} file(s), {dirCount} folder(s)";
        }
        else
        {
            return $"Error: Path not found: {path}";
        }
    }

    private string GetAllowedPaths()
    {
        if (_settings.AllowedPaths.Count == 0)
            return "No paths are configured. Add paths to appsettings.json under FileSystem.AllowedPaths.";

        return "Allowed paths for file operations:\n" +
               string.Join("\n", _settings.AllowedPaths.Select(p => $"  ✅ {p}"));
    }

    private string ResolvePath(string path)
    {
        // Check if it's an absolute path within allowed directories
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        // Try to resolve relative to each allowed path
        foreach (var allowedPath in _settings.AllowedPaths)
        {
            var combined = Path.GetFullPath(Path.Combine(allowedPath, path));
            if (File.Exists(combined) || Directory.Exists(combined))
                return combined;
        }

        // Default to first allowed path
        var basePath = _settings.AllowedPaths.FirstOrDefault() ?? Environment.CurrentDirectory;
        return Path.GetFullPath(Path.Combine(basePath, path));
    }

    // Argha - 2026-02-17 - fixed path traversal: append trailing separator to prevent C:\AllowedPathEvil matching C:\AllowedPath
    private void ValidatePath(string fullPath)
    {
        var normalizedPath = Path.GetFullPath(fullPath);

        var isAllowed = _settings.AllowedPaths.Any(allowed =>
        {
            var normalizedAllowed = Path.GetFullPath(allowed);
            // Ensure trailing separator so "C:\Projects" won't match "C:\ProjectsEvil"
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
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }
}
