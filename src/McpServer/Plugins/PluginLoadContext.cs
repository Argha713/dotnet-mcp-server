// Argha - 2026-02-24 - isolated AssemblyLoadContext per plugin DLL.
// Each plugin runs in its own load context so its transitive dependencies cannot conflict
// with each other or with the host. The abstractions assembly is explicitly shared from the
// host context so that ITool type identity is preserved across the boundary — without this,
// typeof(ITool) in the plugin != typeof(ITool) in the host and IsAssignableFrom returns false.
using System.Reflection;
using System.Runtime.Loader;
using McpServer.Tools;

namespace McpServer.Plugins;

/// <summary>
/// Isolates a single plugin DLL and its dependencies from the host and from other plugins.
/// Sharing <c>McpServer.Plugin.Abstractions</c> with the host ensures the contract types
/// (ITool, PluginContext, ToolCallResult, etc.) are the same Type objects on both sides.
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    // Argha - 2026-02-24 - ITool lives in McpServer.Plugin.Abstractions; typeof(ITool).Assembly
    // gives us the host's already-loaded copy of that assembly. We return this same instance
    // whenever the plugin requests it, guaranteeing shared type identity.
    private static readonly Assembly AbstractionsAssembly = typeof(ITool).Assembly;

    public PluginLoadContext(string pluginPath) : base(isCollectible: false)
    {
        // Argha - 2026-02-24 - AssemblyDependencyResolver reads the plugin's .deps.json to
        // resolve its transitive dependencies from the plugin's own directory.
        _resolver = new AssemblyDependencyResolver(pluginPath);
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Argha - 2026-02-24 - Share the abstractions assembly from the host. This is the
        // critical line: without it, the plugin's ITool and the host's ITool would be different
        // Type objects (loaded from different contexts) and the cast would silently fail.
        if (assemblyName.Name == AbstractionsAssembly.GetName().Name)
            return AbstractionsAssembly;

        // Resolve plugin's own dependencies from its directory (its .deps.json / runtimeconfig)
        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (assemblyPath != null)
            return LoadFromAssemblyPath(assemblyPath);

        // Fall back to the default load context for .NET runtime assemblies
        return null;
    }
}
