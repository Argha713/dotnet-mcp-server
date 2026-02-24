// Argha - 2026-02-24 - ITool moved to McpServer.Plugin.Abstractions so plugin DLLs can
// implement it without referencing the host executable. The type is now provided transitively via
// the <ProjectReference> to McpServer.Plugin.Abstractions in McpServer.csproj.
// Original definition preserved below for reference.

// using McpServer.Progress;
// using McpServer.Protocol;
//
// namespace McpServer.Tools;
//
// public interface ITool
// {
//     string Name { get; }
//     string Description { get; }
//     JsonSchema InputSchema { get; }
//     Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, IProgressReporter? progress = null, CancellationToken cancellationToken = default);
// }
