// Argha - 2026-02-24 - resource provider abstraction, mirrors ITool plugin pattern
using McpServer.Protocol;

namespace McpServer.Resources;

/// <summary>
/// Provides MCP resources from a particular source (e.g. filesystem, config).
/// Implement and register as IResourceProvider to expose new resource types.
/// </summary>
public interface IResourceProvider
{
    /// <summary>
    /// Returns true if this provider can serve the given URI scheme
    /// </summary>
    bool CanHandle(string uri);

    /// <summary>
    /// Lists all resources available from this provider
    /// </summary>
    Task<IEnumerable<Resource>> ListResourcesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Reads the contents of a resource by URI
    /// </summary>
    Task<ResourceContents> ReadResourceAsync(string uri, CancellationToken cancellationToken);
}
