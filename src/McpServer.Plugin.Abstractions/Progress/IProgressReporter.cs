// Argha - 2026-02-24 - IProgressReporter extracted to abstractions so plugin tools
// can report progress without referencing the host executable.
namespace McpServer.Progress;

/// <summary>
/// Reports progress during a long-running tool call.
/// The server emits MCP notifications/progress messages to the client for each Report() call.
/// Use <see cref="NullProgressReporter.Instance"/> when the client sent no progressToken.
/// </summary>
public interface IProgressReporter
{
    /// <summary>
    /// Reports the current progress of a long-running operation.
    /// </summary>
    /// <param name="progress">Current progress value (in arbitrary units: bytes, rows, percent, etc.).</param>
    /// <param name="total">Optional total; omit when the total is not known in advance.</param>
    void Report(double progress, double? total = null);
}
