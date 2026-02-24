// Argha - 2026-02-24 - exposes allowed filesystem paths as MCP resources via file:// URIs
using McpServer.Configuration;
using McpServer.Protocol;
using Microsoft.Extensions.Options;

namespace McpServer.Resources;

/// <summary>
/// Exposes files within configured AllowedPaths as MCP resources.
/// URIs use the file:// scheme (e.g. file:///C:/projects/readme.md).
/// Applies the same 1 MB read limit and path-traversal checks as FileSystemTool.
/// </summary>
public class FileSystemResourceProvider : IResourceProvider
{
    private readonly FileSystemSettings _settings;
    private const long MaxReadSize = 1024 * 1024; // 1 MB — intentional cap, do not raise
    private const int MaxFilesPerPath = 200;       // list cap per allowed path

    public FileSystemResourceProvider(IOptions<FileSystemSettings> settings)
    {
        _settings = settings.Value;
    }

    public bool CanHandle(string uri) =>
        uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

    public Task<IEnumerable<Resource>> ListResourcesAsync(CancellationToken cancellationToken)
    {
        var resources = new List<Resource>();

        foreach (var allowedPath in _settings.AllowedPaths)
        {
            if (!Directory.Exists(allowedPath))
                continue;

            var files = Directory.GetFiles(allowedPath, "*", SearchOption.AllDirectories)
                .Take(MaxFilesPerPath);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileInfo = new FileInfo(file);
                var relativePath = Path.GetRelativePath(allowedPath, file);

                resources.Add(new Resource
                {
                    Uri = PathToFileUri(file),
                    Name = Path.GetFileName(file),
                    Description = $"{relativePath} ({FormatSize(fileInfo.Length)})",
                    MimeType = GetMimeType(file)
                });
            }
        }

        return Task.FromResult<IEnumerable<Resource>>(resources);
    }

    public async Task<ResourceContents> ReadResourceAsync(string uri, CancellationToken cancellationToken)
    {
        var fullPath = FileUriToPath(uri);

        // Argha - 2026-02-24 - reuse same path-traversal guard as FileSystemTool
        ValidatePath(fullPath);

        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Resource not found: {uri}");

        var fileInfo = new FileInfo(fullPath);
        if (fileInfo.Length > MaxReadSize)
            throw new InvalidOperationException(
                $"File too large ({fileInfo.Length / 1024}KB). Maximum size is {MaxReadSize / 1024}KB.");

        var mimeType = GetMimeType(fullPath);

        if (IsTextMimeType(mimeType))
        {
            var text = await File.ReadAllTextAsync(fullPath, cancellationToken);
            return new ResourceContents { Uri = uri, MimeType = mimeType, Text = text };
        }
        else
        {
            var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
            return new ResourceContents { Uri = uri, MimeType = mimeType, Blob = Convert.ToBase64String(bytes) };
        }
    }

    // Argha - 2026-02-24 - mirrors FileSystemTool.ValidatePath — trailing separator prevents C:\AllowedPathEvil matching C:\AllowedPath
    private void ValidatePath(string fullPath)
    {
        var normalizedPath = Path.GetFullPath(fullPath);

        var isAllowed = _settings.AllowedPaths.Any(allowed =>
        {
            var normalizedAllowed = Path.GetFullPath(allowed);
            if (!normalizedAllowed.EndsWith(Path.DirectorySeparatorChar))
                normalizedAllowed += Path.DirectorySeparatorChar;

            return normalizedPath.StartsWith(normalizedAllowed, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.Equals(Path.GetFullPath(allowed), StringComparison.OrdinalIgnoreCase);
        });

        if (!isAllowed)
            throw new UnauthorizedAccessException("Path is outside allowed directories.");
    }

    internal static string PathToFileUri(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var uriPath = fullPath.Replace('\\', '/');
        // Absolute paths need three slashes: file:///C:/...
        if (!uriPath.StartsWith('/'))
            uriPath = '/' + uriPath;
        return $"file://{uriPath}";
    }

    internal static string FileUriToPath(string uri)
    {
        if (!uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Not a file URI: {uri}");

        var path = uri.Substring(7); // strip "file://"

        // file:///C:/... on Windows → strip the leading slash before the drive letter
        if (path.Length > 2 && path[0] == '/' && path[2] == ':')
            path = path.Substring(1);

        return Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string GetMimeType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".txt"              => "text/plain",
            ".md"               => "text/markdown",
            ".cs"               => "text/x-csharp",
            ".json"             => "application/json",
            ".xml"              => "application/xml",
            ".html" or ".htm"   => "text/html",
            ".css"              => "text/css",
            ".js"               => "application/javascript",
            ".ts"               => "application/typescript",
            ".py"               => "text/x-python",
            ".yaml" or ".yml"   => "application/yaml",
            ".csv"              => "text/csv",
            ".log"              => "text/plain",
            ".sh"               => "text/x-shellscript",
            ".ps1"              => "text/plain",
            ".sql"              => "application/sql",
            ".png"              => "image/png",
            ".jpg" or ".jpeg"   => "image/jpeg",
            ".gif"              => "image/gif",
            ".pdf"              => "application/pdf",
            _                   => "application/octet-stream"
        };

    private static bool IsTextMimeType(string mimeType) =>
        mimeType.StartsWith("text/") ||
        mimeType is "application/json"
                 or "application/xml"
                 or "application/yaml"
                 or "application/sql"
                 or "application/javascript"
                 or "application/typescript";

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}
