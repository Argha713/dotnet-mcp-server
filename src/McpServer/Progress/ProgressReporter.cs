// Argha - 2026-02-24 - real progress reporter: delegates to McpLogSink.WriteProgress which holds the stdout lock
using McpServer.Logging;

namespace McpServer.Progress;

/// <summary>
/// Emits MCP notifications/progress lines to the client via McpLogSink.
/// Created per-request when the client sends a progressToken in _meta.
/// </summary>
public class ProgressReporter : IProgressReporter
{
    private readonly string _progressToken;
    private readonly McpLogSink _sink;

    public ProgressReporter(string progressToken, McpLogSink sink)
    {
        _progressToken = progressToken;
        _sink = sink;
    }

    /// <inheritdoc />
    public void Report(double progress, double? total = null)
        => _sink.WriteProgress(_progressToken, progress, total);
}
