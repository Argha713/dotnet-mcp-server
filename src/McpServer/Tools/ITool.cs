using McpServer.Progress;
using McpServer.Protocol;

namespace McpServer.Tools;

/// <summary>
/// Interface for MCP tools
/// </summary>
public interface ITool
{
    /// <summary>
    /// Unique name of the tool
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema for the tool's input parameters
    /// </summary>
    JsonSchema InputSchema { get; }

    // Argha - 2026-02-24 - added IProgressReporter? progress parameter for MCP progress notifications
    /// <summary>
    /// Execute the tool with given arguments.
    /// Pass a real IProgressReporter when the client supplied a progressToken; otherwise null (NullProgressReporter).
    /// </summary>
    Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, IProgressReporter? progress = null, CancellationToken cancellationToken = default);
}
