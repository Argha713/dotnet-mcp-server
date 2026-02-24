// Argha - 2026-02-24 - Phase 5.1: NullProgressReporter extracted to abstractions.
// Plugin tools can safely call progress?.Report() without null-checking if they receive
// NullProgressReporter.Instance when the client sent no progressToken.
namespace McpServer.Progress;

/// <summary>
/// No-op IProgressReporter for when the client did not request progress notifications.
/// Use <see cref="Instance"/> to avoid allocations.
/// </summary>
public sealed class NullProgressReporter : IProgressReporter
{
    /// <summary>Singleton — reuse instead of allocating new instances.</summary>
    public static readonly NullProgressReporter Instance = new();

    private NullProgressReporter() { }

    /// <inheritdoc />
    public void Report(double progress, double? total = null) { }
}
