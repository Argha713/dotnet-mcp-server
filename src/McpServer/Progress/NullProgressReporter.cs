// Argha - 2026-02-24 - no-op reporter used when client sends no progressToken; zero overhead
namespace McpServer.Progress;

/// <summary>
/// No-op IProgressReporter used when the client did not supply a progressToken.
/// Use the singleton Instance to avoid allocations.
/// </summary>
public class NullProgressReporter : IProgressReporter
{
    /// <summary>Singleton — reuse instead of allocating new instances.</summary>
    public static readonly NullProgressReporter Instance = new();

    /// <inheritdoc />
    public void Report(double progress, double? total = null) { }
}
