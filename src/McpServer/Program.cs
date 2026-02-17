using McpServer;
using McpServer.Configuration;
using McpServer.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Build configuration
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

// Setup dependency injection
var services = new ServiceCollection();

// Add configuration
services.Configure<ServerSettings>(configuration.GetSection(ServerSettings.SectionName));
services.Configure<FileSystemSettings>(configuration.GetSection(FileSystemSettings.SectionName));
services.Configure<SqlSettings>(configuration.GetSection(SqlSettings.SectionName));
services.Configure<HttpSettings>(configuration.GetSection(HttpSettings.SectionName));

// Add logging (to stderr so it doesn't interfere with stdio protocol)
services.AddLogging(builder =>
{
    builder.AddConsole(options =>
    {
        options.LogToStandardErrorThreshold = LogLevel.Trace;
    });
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
