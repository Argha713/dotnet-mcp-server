// Argha - 2026-02-24 - prompt provider abstraction, mirrors IResourceProvider plugin pattern
using McpServer.Protocol;

namespace McpServer.Prompts;

/// <summary>
/// Provides MCP prompts (parameterized message templates) from a particular source.
/// Implement and register as IPromptProvider to expose new prompt sets.
/// </summary>
public interface IPromptProvider
{
    /// <summary>
    /// Returns true if this provider owns the prompt with the given name
    /// </summary>
    bool CanHandle(string name);

    /// <summary>
    /// Lists all prompts available from this provider
    /// </summary>
    Task<IEnumerable<Prompt>> ListPromptsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Renders a prompt by name with the supplied arguments
    /// </summary>
    Task<GetPromptResult> GetPromptAsync(string name, Dictionary<string, string>? arguments, CancellationToken cancellationToken);
}
