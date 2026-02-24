// Argha - 2026-02-24 - per-request progress reporter abstraction for MCP notifications/progress protocol
namespace McpServer.Progress;

/// <summary>
/// Abstraction for reporting progress during a long-running tool call.
/// Implementations emit notifications/progress notifications to the client.
/// NullProgressReporter is used when the client sends no progressToken.
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Reports current progress. progress and total are in arbitrary units (rows, bytes, percent, etc.).
    /// </summary>
    /// <param name="progress">Current progress value.</param>
    /// <param name="total">Optional total; omit when the total is not known in advance.</param>
    void Report(double progress, double? total = null);
}
