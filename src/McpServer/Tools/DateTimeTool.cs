using McpServer.Progress;
using McpServer.Protocol;

namespace McpServer.Tools;

/// <summary>
/// Tool for getting current date/time and timezone conversions
/// </summary>
public class DateTimeTool : ITool
{
    public string Name => "datetime";

    public string Description => "Get current date/time or convert between timezones. Use this when you need to know the current time or convert times between different timezones.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "now", "convert", "list_timezones" }
            },
            ["timezone"] = new()
            {
                Type = "string",
                Description = "Target timezone (e.g., 'America/New_York', 'Europe/London', 'Asia/Kolkata')"
            },
            ["datetime"] = new()
            {
                Type = "string",
                Description = "ISO 8601 datetime string for conversion (e.g., '2024-01-15T10:30:00')"
            },
            ["from_timezone"] = new()
            {
                Type = "string",
                Description = "Source timezone for conversion"
            }
        },
        Required = new List<string> { "action" }
    };

    // Argha - 2026-02-24 - progress not used; datetime actions complete instantly
    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, IProgressReporter? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action") ?? "now";

            var result = action.ToLower() switch
            {
                "now" => GetCurrentTime(arguments),
                "convert" => ConvertTime(arguments),
                "list_timezones" => ListTimezones(),
                _ => $"Unknown action: {action}. Use 'now', 'convert', or 'list_timezones'."
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

    private string GetCurrentTime(Dictionary<string, object>? arguments)
    {
        var timezone = GetStringArg(arguments, "timezone");
        
        if (string.IsNullOrEmpty(timezone))
        {
            var now = DateTime.UtcNow;
            return $"Current UTC time: {now:yyyy-MM-dd HH:mm:ss} (UTC)\n" +
                   $"ISO 8601: {now:O}";
        }

        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
            return $"Current time in {timezone}: {localTime:yyyy-MM-dd HH:mm:ss}\n" +
                   $"UTC offset: {tz.GetUtcOffset(localTime)}";
        }
        catch (TimeZoneNotFoundException)
        {
            // Try common timezone aliases
            var mappedTz = MapTimezone(timezone);
            if (mappedTz != null)
            {
                var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, mappedTz);
                return $"Current time in {timezone}: {localTime:yyyy-MM-dd HH:mm:ss}\n" +
                       $"UTC offset: {mappedTz.GetUtcOffset(localTime)}";
            }
            return $"Unknown timezone: {timezone}. Use 'list_timezones' to see available options.";
        }
    }

    private string ConvertTime(Dictionary<string, object>? arguments)
    {
        var datetimeStr = GetStringArg(arguments, "datetime");
        var fromTz = GetStringArg(arguments, "from_timezone") ?? "UTC";
        var toTz = GetStringArg(arguments, "timezone");

        if (string.IsNullOrEmpty(datetimeStr))
            return "Error: 'datetime' parameter is required for conversion.";
        
        if (string.IsNullOrEmpty(toTz))
            return "Error: 'timezone' parameter is required for conversion.";

        if (!DateTime.TryParse(datetimeStr, out var datetime))
            return $"Error: Could not parse datetime '{datetimeStr}'. Use ISO 8601 format.";

        try
        {
            var sourceZone = MapTimezone(fromTz) ?? TimeZoneInfo.FindSystemTimeZoneById(fromTz);
            var targetZone = MapTimezone(toTz) ?? TimeZoneInfo.FindSystemTimeZoneById(toTz);

            var utcTime = TimeZoneInfo.ConvertTimeToUtc(datetime, sourceZone);
            var convertedTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, targetZone);

            return $"Conversion result:\n" +
                   $"  From: {datetime:yyyy-MM-dd HH:mm:ss} ({fromTz})\n" +
                   $"  To:   {convertedTime:yyyy-MM-dd HH:mm:ss} ({toTz})";
        }
        catch (Exception ex)
        {
            return $"Error converting time: {ex.Message}";
        }
    }

    private string ListTimezones()
    {
        var commonZones = new[]
        {
            "UTC",
            "America/New_York (US Eastern)",
            "America/Chicago (US Central)",
            "America/Denver (US Mountain)",
            "America/Los_Angeles (US Pacific)",
            "Europe/London (UK)",
            "Europe/Paris (Central Europe)",
            "Europe/Berlin (Germany)",
            "Asia/Kolkata (India)",
            "Asia/Tokyo (Japan)",
            "Asia/Shanghai (China)",
            "Australia/Sydney (Australia Eastern)"
        };

        return "Common timezones:\n" + string.Join("\n", commonZones.Select(z => $"  • {z}"));
    }

    private TimeZoneInfo? MapTimezone(string timezone)
    {
        // Map common timezone names to system IDs
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EST"] = "America/New_York",
            ["EDT"] = "America/New_York",
            ["CST"] = "America/Chicago",
            ["CDT"] = "America/Chicago",
            ["MST"] = "America/Denver",
            ["MDT"] = "America/Denver",
            ["PST"] = "America/Los_Angeles",
            ["PDT"] = "America/Los_Angeles",
            ["GMT"] = "UTC",
            ["IST"] = "Asia/Kolkata",
            ["JST"] = "Asia/Tokyo",
            ["CET"] = "Europe/Paris",
            ["CEST"] = "Europe/Paris"
        };

        if (mappings.TryGetValue(timezone, out var mapped))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(mapped); }
            catch { }
        }

        try { return TimeZoneInfo.FindSystemTimeZoneById(timezone); }
        catch { return null; }
    }

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }
}
