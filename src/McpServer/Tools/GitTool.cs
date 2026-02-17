using McpServer.Configuration;
using McpServer.Protocol;
using Microsoft.Extensions.Options;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace McpServer.Tools;

// Argha - 2026-02-18 - Git tool: read-only git operations with path validation and command injection prevention
public class GitTool : ITool
{
    private readonly FileSystemSettings _settings;

    // Argha - 2026-02-18 - 30 second process timeout
    private const int ProcessTimeoutMs = 30_000;
    // Argha - 2026-02-18 - 100 KB max output
    private const int MaxOutputSize = 100 * 1024;
    // Argha - 2026-02-18 - only allow safe characters in git arguments
    private static readonly Regex SafeArgRegex = new(@"^[a-zA-Z0-9._/\\\-@~^:]+$", RegexOptions.Compiled);

    public GitTool(IOptions<FileSystemSettings> settings)
    {
        _settings = settings.Value;
    }

    public string Name => "git";

    public string Description => "Read-only Git operations: status, log, diff, branch list, blame. Use this to inspect Git repositories within allowed directories.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "status", "log", "diff", "branch_list", "blame" }
            },
            ["path"] = new()
            {
                Type = "string",
                Description = "Path to the Git repository (must be within allowed directories)"
            },
            ["max_count"] = new()
            {
                Type = "string",
                Description = "Maximum number of log entries (1-100, default: 10)"
            },
            ["branch"] = new()
            {
                Type = "string",
                Description = "Branch name for log (optional)"
            },
            ["target"] = new()
            {
                Type = "string",
                Description = "Diff target (e.g., 'HEAD~1', 'main', a commit hash)"
            },
            ["file"] = new()
            {
                Type = "string",
                Description = "Specific file for diff or blame"
            }
        },
        Required = new List<string> { "action" }
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action") ?? "";

            var result = action.ToLower() switch
            {
                "status" => await GitStatus(arguments, cancellationToken),
                "log" => await GitLog(arguments, cancellationToken),
                "diff" => await GitDiff(arguments, cancellationToken),
                "branch_list" => await GitBranchList(arguments, cancellationToken),
                "blame" => await GitBlame(arguments, cancellationToken),
                _ => $"Unknown action: {action}. Use 'status', 'log', 'diff', 'branch_list', or 'blame'."
            };

            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = result }
                }
            };
        }
        catch (Win32Exception)
        {
            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = "Error: Git is not installed or not found in PATH." }
                },
                IsError = true
            };
        }
        catch (FileNotFoundException)
        {
            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = "Error: Git is not installed or not found in PATH." }
                },
                IsError = true
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = $"Error: Access denied. {ex.Message}" }
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

    private async Task<string> GitStatus(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var path = GetAndValidatePath(arguments);
        return await RunGitCommandAsync(path, new[] { "status", "--porcelain=v1" }, cancellationToken);
    }

    private async Task<string> GitLog(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var path = GetAndValidatePath(arguments);
        var maxCountStr = GetStringArg(arguments, "max_count");
        var branch = GetStringArg(arguments, "branch");

        var maxCount = 10;
        if (int.TryParse(maxCountStr, out var parsed))
            maxCount = Math.Clamp(parsed, 1, 100);

        var args = new List<string> { "log", $"--max-count={maxCount}", "--format=%H %an %ad %s", "--date=short" };

        if (!string.IsNullOrEmpty(branch))
        {
            ValidateArgSafety(branch);
            args.Add(branch);
        }

        return await RunGitCommandAsync(path, args.ToArray(), cancellationToken);
    }

    private async Task<string> GitDiff(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var path = GetAndValidatePath(arguments);
        var target = GetStringArg(arguments, "target");
        var file = GetStringArg(arguments, "file");

        var args = new List<string> { "diff" };

        if (!string.IsNullOrEmpty(target))
        {
            ValidateArgSafety(target);
            args.Add(target);
        }

        if (!string.IsNullOrEmpty(file))
        {
            ValidateFilePath(file);
            args.Add("--");
            args.Add(file);
        }

        return await RunGitCommandAsync(path, args.ToArray(), cancellationToken);
    }

    private async Task<string> GitBranchList(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var path = GetAndValidatePath(arguments);
        return await RunGitCommandAsync(path, new[] { "branch", "-a" }, cancellationToken);
    }

    private async Task<string> GitBlame(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var path = GetAndValidatePath(arguments);
        var file = GetStringArg(arguments, "file");

        if (string.IsNullOrEmpty(file))
            return "Error: 'file' parameter is required for blame.";

        ValidateFilePath(file);
        return await RunGitCommandAsync(path, new[] { "blame", file }, cancellationToken);
    }

    // Argha - 2026-02-18 - core git command runner with timeout, output cap, and ProcessStartInfo.ArgumentList for injection prevention
    private async Task<string> RunGitCommandAsync(string workingDirectory, string[] args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Argha - 2026-02-18 - use ArgumentList (collection API) to prevent command injection via string concatenation
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        // Argha - 2026-02-18 - read stdout/stderr concurrently to prevent deadlocks
        var outputTask = ReadStreamWithCapAsync(process.StandardOutput, MaxOutputSize);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProcessTimeoutMs);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return "Error: Git command timed out after 30 seconds.";
        }

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            if (!string.IsNullOrWhiteSpace(error))
                return $"Git error (exit code {process.ExitCode}):\n{error.Trim()}";
            return $"Git error (exit code {process.ExitCode}).";
        }

        if (string.IsNullOrWhiteSpace(output))
            return "No output (clean state or empty result).";

        return output.Trim();
    }

    private static async Task<string> ReadStreamWithCapAsync(System.IO.StreamReader reader, int maxChars)
    {
        var sb = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            if (sb.Length + read > maxChars)
            {
                sb.Append(buffer, 0, maxChars - sb.Length);
                sb.Append("\n... (output truncated at 100KB)");
                break;
            }
            sb.Append(buffer, 0, read);
        }
        return sb.ToString();
    }

    // Argha - 2026-02-18 - validate repo path is within allowed directories (reuses FileSystemSettings)
    private string GetAndValidatePath(Dictionary<string, object>? arguments)
    {
        var path = GetStringArg(arguments, "path");

        if (string.IsNullOrEmpty(path))
            throw new ArgumentException("'path' parameter is required.");

        var fullPath = Path.GetFullPath(path);

        var isAllowed = _settings.AllowedPaths.Any(allowed =>
        {
            var normalizedAllowed = Path.GetFullPath(allowed);
            if (!normalizedAllowed.EndsWith(Path.DirectorySeparatorChar))
                normalizedAllowed += Path.DirectorySeparatorChar;

            return fullPath.StartsWith(normalizedAllowed, StringComparison.OrdinalIgnoreCase)
                || fullPath.Equals(Path.GetFullPath(allowed), StringComparison.OrdinalIgnoreCase);
        });

        if (!isAllowed)
            throw new UnauthorizedAccessException($"Path '{path}' is outside allowed directories.");

        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"Directory not found: {path}");

        return fullPath;
    }

    // Argha - 2026-02-18 - validate git argument against safe character regex to prevent injection
    private static void ValidateArgSafety(string arg)
    {
        if (!SafeArgRegex.IsMatch(arg))
            throw new ArgumentException($"Argument contains unsafe characters: '{arg}'. Only alphanumeric, '.', '_', '/', '\\', '-', '@', '~', '^', ':' are allowed.");
    }

    // Argha - 2026-02-18 - reject path traversal in file arguments
    private static void ValidateFilePath(string file)
    {
        if (file.Contains(".."))
            throw new ArgumentException($"Path traversal ('..') is not allowed in file arguments.");
        ValidateArgSafety(file);
    }

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }
}
