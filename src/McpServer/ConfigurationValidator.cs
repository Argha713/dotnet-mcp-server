using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace McpServer;

// Argha - 2026-02-23 - health check runner invoked by the --validate CLI flag
internal static class ConfigurationValidator
{
    internal static async Task<int> RunAsync(IConfiguration configuration)
    {
        Console.WriteLine("Checking configuration...");
        Console.WriteLine();

        var passCount = 0;
        var failCount = 0;

        // FileSystem checks
        Console.WriteLine("FileSystem");
        var allowedPaths = configuration.GetSection("FileSystem:AllowedPaths").Get<List<string>>() ?? [];
        if (allowedPaths.Count == 0)
        {
            Console.WriteLine("  (no AllowedPaths configured)");
        }
        else
        {
            foreach (var path in allowedPaths)
            {
                if (Directory.Exists(path))
                {
                    Console.WriteLine($"  \u2713 {path}");
                    passCount++;
                }
                else
                {
                    Console.WriteLine($"  \u2717 {path}  (directory not found)");
                    failCount++;
                }
            }
        }

        // SQL checks
        Console.WriteLine();
        Console.WriteLine("SQL");
        var sqlConnections = configuration.GetSection("Sql:Connections").GetChildren().ToList();
        if (sqlConnections.Count == 0)
        {
            Console.WriteLine("  (no connections configured)");
        }
        else
        {
            foreach (var conn in sqlConnections)
            {
                var connStr = conn["ConnectionString"];
                if (string.IsNullOrWhiteSpace(connStr))
                {
                    Console.WriteLine($"  \u2717 {conn.Key}  (empty connection string)");
                    failCount++;
                    continue;
                }

                try
                {
                    using var sqlConn = new SqlConnection(connStr);
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    await sqlConn.OpenAsync(cts.Token);
                    sw.Stop();
                    Console.WriteLine($"  \u2713 {conn.Key}  ({sw.ElapsedMilliseconds}ms)");
                    passCount++;
                }
                catch (Exception ex)
                {
                    // Argha - 2026-02-23 - first line only keeps output readable; full error visible in debug logs
                    var message = ex.Message.Split('\n')[0].Trim();
                    Console.WriteLine($"  \u2717 {conn.Key}  ({message})");
                    failCount++;
                }
            }
        }

        // HTTP checks
        Console.WriteLine();
        Console.WriteLine("HTTP");
        var allowedHosts = configuration.GetSection("Http:AllowedHosts").Get<List<string>>() ?? [];
        if (allowedHosts.Count == 0)
        {
            Console.WriteLine("  (no AllowedHosts configured)");
        }
        else
        {
            foreach (var host in allowedHosts)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var addresses = await Dns.GetHostAddressesAsync(host, cts.Token);
                    var ip = addresses.Length > 0 ? $"  ({addresses[0]})" : string.Empty;
                    Console.WriteLine($"  \u2713 {host}{ip}");
                    passCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  \u2717 {host}  ({ex.Message})");
                    failCount++;
                }
            }
        }

        Console.WriteLine();

        if (passCount == 0 && failCount == 0)
        {
            Console.WriteLine("No resources configured. Add settings to appsettings.json.");
            return 0;
        }

        Console.WriteLine(failCount == 0
            ? $"All checks passed ({passCount} passed)."
            : $"{failCount} check(s) failed, {passCount} passed.");

        return failCount > 0 ? 1 : 0;
    }
}
