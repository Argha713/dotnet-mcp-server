using McpServer.Progress;
using McpServer.Protocol;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace McpServer.Tools;

// Argha - 2026-02-18 - System info tool: OS, processes, network (read-only, no process kill)
public class SystemInfoTool : ITool
{
    // Argha - 2026-02-18 - cap unfiltered process list at 50 to prevent excessive output
    private const int DefaultProcessLimit = 20;
    private const int MaxProcessLimit = 50;

    public string Name => "system_info";

    public string Description => "Get system information: OS details, running processes, network interfaces. Read-only system diagnostics.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "system_info", "processes", "network" }
            },
            ["filter"] = new()
            {
                Type = "string",
                Description = "Filter processes by name (for 'processes' action)"
            },
            ["sort_by"] = new()
            {
                Type = "string",
                Description = "Sort processes by: memory, cpu, name (default: memory)"
            },
            ["top"] = new()
            {
                Type = "string",
                Description = "Number of top processes to return (default: 20, max: 50)"
            }
        },
        Required = new List<string> { "action" }
    };

    // Argha - 2026-02-24 - progress not used; system info collection is near-instant
    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, IProgressReporter? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action") ?? "";

            var result = action.ToLower() switch
            {
                "system_info" => GetSystemInfo(),
                "processes" => GetProcesses(arguments),
                "network" => GetNetworkInfo(),
                _ => $"Unknown action: {action}. Use 'system_info', 'processes', or 'network'."
            };

            return Task.FromResult(new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = result }
                }
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = $"Error: {ex.Message}" }
                },
                IsError = true
            });
        }
    }

    private static string GetSystemInfo()
    {
        var lines = new List<string>
        {
            "System Information:",
            $"  OS: {RuntimeInformation.OSDescription}",
            $"  OS Architecture: {RuntimeInformation.OSArchitecture}",
            $"  .NET Version: {Environment.Version}",
            $"  Runtime: {RuntimeInformation.FrameworkDescription}",
            $"  Machine Name: {Environment.MachineName}",
            $"  Processor Count: {Environment.ProcessorCount}",
            $"  64-bit OS: {Environment.Is64BitOperatingSystem}",
            $"  64-bit Process: {Environment.Is64BitProcess}",
            $"  System Uptime: {TimeSpan.FromMilliseconds(Environment.TickCount64):d\\.hh\\:mm\\:ss}"
        };

        // Argha - 2026-02-18 - disk drive info
        lines.Add("\nDisk Drives:");
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady)
                {
                    var usedPercent = ((double)(drive.TotalSize - drive.AvailableFreeSpace) / drive.TotalSize * 100);
                    lines.Add($"  {drive.Name} ({drive.DriveType}) - " +
                              $"{FormatSize(drive.AvailableFreeSpace)} free / {FormatSize(drive.TotalSize)} total " +
                              $"({usedPercent:F1}% used)");
                }
            }
        }
        catch (Exception ex)
        {
            lines.Add($"  Error reading drives: {ex.Message}");
        }

        return string.Join("\n", lines);
    }

    private static string GetProcesses(Dictionary<string, object>? arguments)
    {
        var filter = GetStringArg(arguments, "filter");
        var sortBy = GetStringArg(arguments, "sort_by") ?? "memory";
        var topStr = GetStringArg(arguments, "top");
        var top = DefaultProcessLimit;
        if (int.TryParse(topStr, out var parsedTop))
            top = Math.Clamp(parsedTop, 1, MaxProcessLimit);

        var processes = Process.GetProcesses();
        var processInfos = new List<(string Name, int Pid, long Memory)>();

        foreach (var proc in processes)
        {
            try
            {
                if (!string.IsNullOrEmpty(filter) &&
                    !proc.ProcessName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                processInfos.Add((proc.ProcessName, proc.Id, proc.WorkingSet64));
            }
            catch
            {
                // Argha - 2026-02-18 - some processes deny access, skip them
            }
            finally
            {
                proc.Dispose();
            }
        }

        // Argha - 2026-02-18 - sort by requested field
        processInfos = sortBy.ToLower() switch
        {
            "name" => processInfos.OrderBy(p => p.Name).ToList(),
            "cpu" => processInfos.OrderByDescending(p => p.Memory).ToList(),
            _ => processInfos.OrderByDescending(p => p.Memory).ToList()
        };

        var limited = processInfos.Take(top).ToList();

        if (limited.Count == 0)
        {
            return string.IsNullOrEmpty(filter)
                ? "No processes found."
                : $"No processes matching '{filter}' found.";
        }

        var lines = new List<string>();
        var filterNote = string.IsNullOrEmpty(filter) ? "" : $" matching '{filter}'";
        lines.Add($"Processes{filterNote} (showing top {limited.Count} of {processInfos.Count}, sorted by {sortBy}):");
        lines.Add($"  {"PID",-8} {"Memory",-12} {"Name"}");
        lines.Add($"  {"---",-8} {"------",-12} {"----"}");

        foreach (var p in limited)
        {
            lines.Add($"  {p.Pid,-8} {FormatSize(p.Memory),-12} {p.Name}");
        }

        return string.Join("\n", lines);
    }

    private static string GetNetworkInfo()
    {
        var lines = new List<string> { "Network Interfaces:" };

        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var ni in interfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up)
                {
                    lines.Add($"\n  {ni.Name} ({ni.NetworkInterfaceType}):");
                    lines.Add($"    Status: {ni.OperationalStatus}");
                    lines.Add($"    Speed: {ni.Speed / 1_000_000} Mbps");

                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        // Argha - 2026-02-18 - show IPv4 and IPv6, but no MAC addresses
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork ||
                            addr.Address.AddressFamily == AddressFamily.InterNetworkV6)
                        {
                            lines.Add($"    IP: {addr.Address} ({addr.Address.AddressFamily})");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            lines.Add($"  Error reading network interfaces: {ex.Message}");
        }

        if (lines.Count == 1)
            lines.Add("  No active network interfaces found.");

        return string.Join("\n", lines);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }
}
