// Argha - 2026-02-24 - Phase 5.1: scans the plugins directory for DLLs, loads each in an isolated
// AssemblyLoadContext, and yields ITool instances for every public ITool implementor found.
// A bad plugin (corrupt DLL, missing dependencies, throwing constructor) is logged and skipped —
// it must never prevent the server from starting.
using McpServer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace McpServer.Plugins;

/// <summary>
/// Discovers and loads plugin tools from DLL files in the configured plugins directory.
/// </summary>
public class PluginLoader
{
    private readonly string _pluginsDirectory;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PluginLoader> _logger;

    public PluginLoader(string pluginsDirectory, IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _pluginsDirectory = pluginsDirectory;
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PluginLoader>();
    }

    /// <summary>
    /// Loads all ITool implementations found in *.dll files under the plugins directory.
    /// Yields nothing if the directory does not exist or is empty.
    /// </summary>
    public IEnumerable<ITool> LoadPlugins()
    {
        if (!Directory.Exists(_pluginsDirectory))
        {
            _logger.LogInformation(
                "Plugins directory not found — no plugins loaded: {Dir}", _pluginsDirectory);
            yield break;
        }

        var dlls = Directory.GetFiles(_pluginsDirectory, "*.dll");
        if (dlls.Length == 0)
        {
            _logger.LogInformation(
                "Plugins directory is empty — no plugins loaded: {Dir}", _pluginsDirectory);
            yield break;
        }

        _logger.LogInformation(
            "Found {Count} DLL(s) in plugins directory: {Dir}", dlls.Length, _pluginsDirectory);

        foreach (var dllPath in dlls)
            foreach (var tool in LoadPluginDll(dllPath))
                yield return tool;
    }

    private IEnumerable<ITool> LoadPluginDll(string dllPath)
    {
        var dllName = Path.GetFileName(dllPath);
        List<Type> toolTypes;

        try
        {
            var loadContext = new PluginLoadContext(dllPath);
            var assembly = loadContext.LoadFromAssemblyPath(dllPath);

            toolTypes = assembly.GetExportedTypes()
                .Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface)
                .ToList();

            if (toolTypes.Count == 0)
            {
                _logger.LogInformation(
                    "No ITool implementations found in plugin: {Dll}", dllName);
                yield break;
            }
        }
        catch (Exception ex)
        {
            // Argha - 2026-02-24 - intentional: a corrupt or incompatible plugin DLL must never
            // prevent the server from starting. Log the error and move on.
            _logger.LogError(ex, "Failed to load plugin DLL: {Dll}", dllName);
            yield break;
        }

        foreach (var toolType in toolTypes)
        {
            var tool = InstantiateTool(toolType, dllName);
            if (tool != null)
            {
                _logger.LogInformation(
                    "Loaded plugin tool '{ToolName}' ({Type}) from {Dll}",
                    tool.Name, toolType.Name, dllName);
                yield return tool;
            }
        }
    }

    private ITool? InstantiateTool(Type toolType, string dllName)
    {
        try
        {
            // Argha - 2026-02-24 - prefer ctor(PluginContext) so plugins can access config and logging;
            // fall back to parameterless ctor for simple tools that need neither.
            var ctorWithContext = toolType.GetConstructor(new[] { typeof(PluginContext) });
            if (ctorWithContext != null)
            {
                var context = BuildPluginContext(toolType.Assembly.GetName().Name ?? dllName);
                return (ITool)ctorWithContext.Invoke(new object[] { context });
            }

            var defaultCtor = toolType.GetConstructor(Type.EmptyTypes);
            if (defaultCtor != null)
                return (ITool)defaultCtor.Invoke(null);

            _logger.LogWarning(
                "Plugin type {Type} in {Dll} has no supported constructor. " +
                "Expected parameterless or ctor(PluginContext). Skipping.",
                toolType.Name, dllName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to instantiate plugin type {Type} from {Dll}", toolType.Name, dllName);
            return null;
        }
    }

    private PluginContext BuildPluginContext(string pluginName)
    {
        // Argha - 2026-02-24 - plugins read shared config from the Plugins:Config section.
        // Example: appsettings.json -> "Plugins": { "Config": { "MyApiKey": "abc123" } }
        // Plugin calls context.GetConfig("MyApiKey") -> "abc123"
        var configSection = _configuration.GetSection("Plugins:Config");
        var logger = _loggerFactory.CreateLogger(pluginName);
        return new PluginContext(key => configSection[key], logger);
    }
}
