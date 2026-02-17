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

// Run the server
var server = serviceProvider.GetRequiredService<McpServerHandler>();
var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

await server.RunAsync(cts.Token);
