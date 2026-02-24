// Argha - 2026-02-24 - Phase 5.1: ITool extracted to McpServer.Plugin.Abstractions so plugin DLLs can
// implement the interface without referencing the host executable.
using McpServer.Progress;
using McpServer.Protocol;

namespace McpServer.Tools;

/// <summary>
/// Contract that all MCP tools — both built-in and plugin — must implement.
/// Plugin authors: reference McpServer.Plugin.Abstractions and implement this interface,
/// then drop your compiled DLL into the configured plugins directory.
/// </summary>
public interface ITool
{
    /// <summary>Unique name used by clients to invoke this tool via tools/call.</summary>
    string Name { get; }

    /// <summary>Human-readable description shown to the AI assistant in tools/list.</summary>
    string Description { get; }

    /// <summary>JSON Schema describing the accepted input parameters.</summary>
    JsonSchema InputSchema { get; }

    /// <summary>
    /// Execute the tool. Return a <see cref="ToolCallResult"/> with IsError=false on success
    /// or IsError=true (with a descriptive message) on failure — do not throw.
    /// </summary>
    /// <param name="arguments">Deserialized input arguments from the client.</param>
    /// <param name="progress">
    /// Optional progress reporter. Call progress.Report(current, total) during long operations.
    /// Will be <see cref="NullProgressReporter.Instance"/> when the client sent no progressToken.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the server is shutting down.</param>
    Task<ToolCallResult> ExecuteAsync(
        Dictionary<string, object>? arguments,
        IProgressReporter? progress = null,
        CancellationToken cancellationToken = default);
}
