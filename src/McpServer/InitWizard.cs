using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace McpServer;

// Argha - 2026-02-23 - interactive first-run wizard that writes appsettings.json
internal static class InitWizard
{
    internal static async Task<int> RunAsync(
        string outputPath,
        TextReader? input = null,
        TextWriter? output = null)
    {
        input ??= Console.In;
        output ??= Console.Out;

        await output.WriteLineAsync("dotnet-mcp-server setup wizard");
        await output.WriteLineAsync($"Output: {outputPath}");
        await output.WriteLineAsync();

        // Argha - 2026-02-23 - prompt for overwrite when file already exists
        if (File.Exists(outputPath))
        {
            await output.WriteAsync("appsettings.json already exists. Overwrite? [y/N] ");
            var response = input.ReadLine()?.Trim().ToLowerInvariant();
            if (response != "y" && response != "yes")
            {
                await output.WriteLineAsync("Aborted.");
                return 0;
            }
            await output.WriteLineAsync();
        }

        // FileSystem paths
        await output.WriteLineAsync("=== FileSystem ===");
        await output.WriteLineAsync("Directories the server is allowed to read (press Enter to finish):");
        var allowedPaths = new List<string>();
        while (true)
        {
            await output.WriteAsync($"  Path {allowedPaths.Count + 1}: ");
            var line = input.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line)) break;
            allowedPaths.Add(line);
        }

        // SQL connections
        await output.WriteLineAsync();
        await output.WriteLineAsync("=== SQL ===");
        await output.WriteLineAsync("SQL Server connections (leave connection name blank to finish):");
        var sqlConnections = new List<(string Name, string ConnectionString, string Description)>();
        while (true)
        {
            await output.WriteAsync("  Connection name (e.g. MyDB): ");
            var name = input.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(name)) break;

            await output.WriteAsync("  Connection string: ");
            var connStr = input.ReadLine()?.Trim() ?? string.Empty;

            await output.WriteAsync("  Description (optional): ");
            var desc = input.ReadLine()?.Trim() ?? string.Empty;

            sqlConnections.Add((name, connStr, desc));
        }

        // HTTP allowed hosts
        await output.WriteLineAsync();
        await output.WriteLineAsync("=== HTTP ===");
        await output.WriteLineAsync("Hostnames the server is allowed to call (press Enter to finish):");
        var allowedHosts = new List<string>();
        while (true)
        {
            await output.WriteAsync($"  Host {allowedHosts.Count + 1} (e.g. api.github.com): ");
            var line = input.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line)) break;
            allowedHosts.Add(line);
        }

        // Build JSON using JsonNodes for precise formatting
        var connectionsNode = new JsonObject();
        foreach (var (name, connStr, desc) in sqlConnections)
        {
            connectionsNode[name] = new JsonObject
            {
                ["ConnectionString"] = connStr,
                ["Description"] = desc
            };
        }

        var pathsNode = new JsonArray();
        foreach (var p in allowedPaths) pathsNode.Add(p);

        var hostsNode = new JsonArray();
        foreach (var h in allowedHosts) hostsNode.Add(h);

        var root = new JsonObject
        {
            ["Server"] = new JsonObject
            {
                ["Name"] = "dotnet-mcp-server",
                ["Version"] = "1.0.0"
            },
            ["FileSystem"] = new JsonObject
            {
                ["AllowedPaths"] = pathsNode
            },
            ["Sql"] = new JsonObject
            {
                ["Connections"] = connectionsNode
            },
            ["Http"] = new JsonObject
            {
                ["AllowedHosts"] = hostsNode,
                ["TimeoutSeconds"] = 30
            },
            ["Environment"] = new JsonObject
            {
                ["AdditionalBlockedVariables"] = new JsonArray()
            },
            ["Logging"] = new JsonObject
            {
                ["LogLevel"] = new JsonObject
                {
                    ["Default"] = "Information",
                    ["McpServer"] = "Debug"
                }
            }
        };

        // Argha - 2026-02-23 - use Utf8JsonWriter with JsonWriterOptions to avoid JsonSerializerOptions
        // TypeInfoResolver requirement introduced in .NET 10
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        root.WriteTo(writer);
        writer.Flush();
        var json = Encoding.UTF8.GetString(stream.ToArray());
        await File.WriteAllTextAsync(outputPath, json);

        await output.WriteLineAsync();
        await output.WriteLineAsync($"Configuration written to: {outputPath}");
        await output.WriteLineAsync("Run --validate to verify all connections.");
        return 0;
    }
}
