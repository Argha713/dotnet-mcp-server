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

    /// <summary>
    /// Execute the tool with given arguments
    /// </summary>
    Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, CancellationToken cancellationToken = default);
}
