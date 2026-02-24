using McpServer;
using McpServer.Configuration;
using McpServer.Logging;
using McpServer.Plugins;
using McpServer.Prompts;
using McpServer.Resources;
using McpServer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Argha - 2026-02-23 - for global tool installs the tool store is read-only; prefer user config dir
// (%APPDATA%\dotnet-mcp-server on Windows, ~/.config/dotnet-mcp-server on Linux/Mac) when it has an
// appsettings.json, falling back to BaseDirectory for local dotnet-run development.
static string ResolveConfigDirectory()
{
    var userConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "dotnet-mcp-server");
    if (File.Exists(Path.Combine(userConfigDir, "appsettings.json")))
        return userConfigDir;
    return AppContext.BaseDirectory;
}

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(ResolveConfigDirectory())
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// Argha - 2026-02-23 - health check mode: validate all configured connections and paths, then exit
if (args.Contains("--validate"))
{
    var exitCode = await ConfigurationValidator.RunAsync(configuration);
    Environment.Exit(exitCode);
}

// Argha - 2026-02-23 - init wizard: generate appsettings.json for first-run setup, then exit
// Write to user config dir so global tool installs don't need write access to the tool store
if (args.Contains("--init"))
{
    var outputDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "dotnet-mcp-server");
    Directory.CreateDirectory(outputDir);
    var outputPath = Path.Combine(outputDir, "appsettings.json");
    var exitCode = await InitWizard.RunAsync(outputPath);
    Environment.Exit(exitCode);
}

// Setup dependency injection
var services = new ServiceCollection();

// Add configuration
services.Configure<ServerSettings>(configuration.GetSection(ServerSettings.SectionName));
services.Configure<FileSystemSettings>(configuration.GetSection(FileSystemSettings.SectionName));
services.Configure<SqlSettings>(configuration.GetSection(SqlSettings.SectionName));
services.Configure<HttpSettings>(configuration.GetSection(HttpSettings.SectionName));
services.Configure<EnvironmentSettings>(configuration.GetSection(EnvironmentSettings.SectionName));

// Argha - 2026-02-24 - register McpLogSink before logging so McpLoggerProvider can receive it
services.AddSingleton<McpLogSink>();

// Add logging (to stderr so it doesn't interfere with stdio protocol)
services.AddLogging(builder =>
{
    builder.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
    // Argha - 2026-02-24 - also forward logs to the MCP client via notifications/message
    builder.Services.AddSingleton<ILoggerProvider, McpLoggerProvider>();
    builder.SetMinimumLevel(
        configuration.GetValue<LogLevel>("Logging:LogLevel:Default", LogLevel.Information));
});

// Add HttpClient for HTTP tool
services.AddHttpClient<HttpTool>(client =>
{
    var timeout = configuration.GetValue<int>("Http:TimeoutSeconds", 30);
    client.Timeout = TimeSpan.FromSeconds(timeout);
});

// Register tools
services.AddSingleton<ITool, DateTimeTool>();
services.AddSingleton<ITool, FileSystemTool>();
services.AddSingleton<ITool, SqlQueryTool>();
services.AddSingleton<ITool, HttpTool>();
// Argha - 2026-02-18 - Phase 2 new tools
services.AddSingleton<ITool, TextTool>();
services.AddSingleton<ITool, DataTransformTool>();
services.AddSingleton<ITool, EnvironmentTool>();
services.AddSingleton<ITool, SystemInfoTool>();
services.AddSingleton<ITool, GitTool>();

// Argha - 2026-02-24 - Phase 5.1: load plugin tools from the configured plugins directory.
// A dedicated minimal logger factory is used here because the full DI-managed logger (with the
// MCP log sink) is not yet built — plugin loading happens before BuildServiceProvider().
var pluginsConfig = configuration
    .GetSection(PluginsSettings.SectionName)
    .Get<PluginsSettings>() ?? new PluginsSettings();
services.Configure<PluginsSettings>(configuration.GetSection(PluginsSettings.SectionName));

if (pluginsConfig.Enabled)
{
    var pluginsDir = Path.IsPathRooted(pluginsConfig.Directory)
        ? pluginsConfig.Directory
        : Path.Combine(ResolveConfigDirectory(), pluginsConfig.Directory);

    using var pluginLoggerFactory = LoggerFactory.Create(b =>
        b.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace)
         .SetMinimumLevel(LogLevel.Information));

    var pluginLoader = new PluginLoader(pluginsDir, configuration, pluginLoggerFactory);
    foreach (var tool in pluginLoader.LoadPlugins())
        services.AddSingleton<ITool>(tool);   // register as instance, not type
}

// Argha - 2026-02-24 - register resource providers
services.AddSingleton<IResourceProvider, FileSystemResourceProvider>();

// Argha - 2026-02-24 - register built-in prompt provider
services.AddSingleton<IPromptProvider, BuiltInPromptProvider>();

// Register MCP server
services.AddSingleton<McpServerHandler>();

// Build service provider
var serviceProvider = services.BuildServiceProvider();

// Argha - 2026-02-17 - validate configuration on startup and warn about issues
ValidateConfiguration(configuration, serviceProvider.GetRequiredService<ILogger<McpServerHandler>>());

// Run the server
var server = serviceProvider.GetRequiredService<McpServerHandler>();
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await server.RunAsync(cts.Token);

// Argha - 2026-02-17 - startup config validation: warn about misconfigured paths, connections, hosts
static void ValidateConfiguration(IConfiguration configuration, ILogger logger)
{
    // Validate FileSystem AllowedPaths
    var allowedPaths = configuration.GetSection("FileSystem:AllowedPaths").Get<List<string>>() ?? new();
    if (allowedPaths.Count == 0)
    {
        logger.LogWarning("No FileSystem:AllowedPaths configured. FileSystem tool will not be able to access any directories.");
    }
    else
    {
        foreach (var path in allowedPaths)
        {
            if (!Directory.Exists(path))
                logger.LogWarning("FileSystem AllowedPath does not exist: {Path}", path);
        }
    }

    // Validate SQL connections
    var sqlSection = configuration.GetSection("Sql:Connections");
    var sqlConnections = sqlSection.GetChildren().ToList();
    foreach (var conn in sqlConnections)
    {
        var connStr = conn["ConnectionString"];
        if (string.IsNullOrWhiteSpace(connStr))
            logger.LogWarning("SQL connection '{Name}' has an empty ConnectionString.", conn.Key);
    }

    // Validate HTTP AllowedHosts
    var allowedHosts = configuration.GetSection("Http:AllowedHosts").Get<List<string>>() ?? new();
    if (allowedHosts.Count == 0)
    {
        logger.LogWarning("No Http:AllowedHosts configured. HTTP tool will not be able to make any requests.");
    }
    else
    {
        foreach (var host in allowedHosts)
        {
            if (host.Contains("://") || host.Contains('/') || host.Contains(' '))
                logger.LogWarning("Http AllowedHost '{Host}' looks malformed. Use hostname only (e.g., 'api.github.com'), not a full URL.", host);
        }
    }
}
