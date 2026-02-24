using McpServer.Configuration;
using McpServer.Progress;
using McpServer.Protocol;
using Microsoft.Extensions.Options;

namespace McpServer.Tools;

// Argha - 2026-02-18 - Environment variable tool with hardcoded blocklist for sensitive vars
public class EnvironmentTool : ITool
{
    private readonly EnvironmentSettings _settings;

    // Argha - 2026-02-18 - hardcoded blocklist: these can NEVER be weakened via config
    private static readonly HashSet<string> BlockedExactNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AWS_SECRET_ACCESS_KEY",
        "AWS_SESSION_TOKEN",
        "AZURE_CLIENT_SECRET",
        "GITHUB_TOKEN",
        "GH_TOKEN",
        "NPM_TOKEN",
        "NUGET_API_KEY",
        "DOCKER_PASSWORD",
        "DATABASE_URL",
        "CONNECTION_STRING",
        "DB_PASSWORD"
    };

    // Argha - 2026-02-18 - pattern blocklist: any var name containing these substrings is blocked
    private static readonly string[] BlockedPatterns = new[]
    {
        "PASSWORD",
        "SECRET",
        "TOKEN",
        "KEY",
        "CREDENTIAL",
        "PRIVATE",
        "API_KEY",
        "APIKEY",
        "AUTH"
    };

    private const string MaskedValue = "********";

    public EnvironmentTool(IOptions<EnvironmentSettings> settings)
    {
        _settings = settings.Value;
    }

    public string Name => "environment";

    public string Description => "Check environment variables: get values, list variables, check existence. Sensitive variables are automatically masked for security.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "get", "list", "has" }
            },
            ["name"] = new()
            {
                Type = "string",
                Description = "Environment variable name (for 'get' and 'has')"
            },
            ["filter"] = new()
            {
                Type = "string",
                Description = "Filter pattern for 'list' action (case-insensitive substring match)"
            }
        },
        Required = new List<string> { "action" }
    };

    // Argha - 2026-02-24 - progress not used; environment read operations complete instantly
    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, IProgressReporter? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action") ?? "";

            var result = action.ToLower() switch
            {
                "get" => GetVariable(arguments),
                "list" => ListVariables(arguments),
                "has" => HasVariable(arguments),
                _ => $"Unknown action: {action}. Use 'get', 'list', or 'has'."
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

    private string GetVariable(Dictionary<string, object>? arguments)
    {
        var name = GetStringArg(arguments, "name");

        if (string.IsNullOrEmpty(name))
            return "Error: 'name' parameter is required.";

        if (IsSensitive(name))
            return $"{name} = {MaskedValue} (sensitive variable - value is masked)";

        var value = Environment.GetEnvironmentVariable(name);
        if (value == null)
            return $"Environment variable '{name}' is not set.";

        return $"{name} = {value}";
    }

    private string ListVariables(Dictionary<string, object>? arguments)
    {
        var filter = GetStringArg(arguments, "filter");

        var envVars = Environment.GetEnvironmentVariables();
        var entries = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (System.Collections.DictionaryEntry entry in envVars)
        {
            var key = entry.Key?.ToString() ?? "";
            var value = entry.Value?.ToString() ?? "";

            if (!string.IsNullOrEmpty(filter) &&
                !key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                continue;

            entries[key] = IsSensitive(key) ? MaskedValue : value;
        }

        if (entries.Count == 0)
        {
            return string.IsNullOrEmpty(filter)
                ? "No environment variables found."
                : $"No environment variables matching '{filter}' found.";
        }

        var lines = entries.Select(e => $"  {e.Key} = {e.Value}");
        var filterNote = string.IsNullOrEmpty(filter) ? "" : $" matching '{filter}'";
        return $"Environment variables{filterNote} ({entries.Count}):\n{string.Join("\n", lines)}";
    }

    private string HasVariable(Dictionary<string, object>? arguments)
    {
        var name = GetStringArg(arguments, "name");

        if (string.IsNullOrEmpty(name))
            return "Error: 'name' parameter is required.";

        // Argha - 2026-02-18 - has is safe even for blocklisted vars: only returns true/false
        var exists = Environment.GetEnvironmentVariable(name) != null;
        return $"{name}: {(exists ? "exists" : "not set")}";
    }

    // Argha - 2026-02-18 - check if variable name matches blocklist (hardcoded + config)
    private bool IsSensitive(string name)
    {
        if (BlockedExactNames.Contains(name))
            return true;

        var upperName = name.ToUpperInvariant();
        foreach (var pattern in BlockedPatterns)
        {
            if (upperName.Contains(pattern))
                return true;
        }

        // Argha - 2026-02-18 - config can ADD more blocked vars but never remove hardcoded ones
        if (_settings.AdditionalBlockedVariables != null)
        {
            foreach (var blocked in _settings.AdditionalBlockedVariables)
            {
                if (name.Equals(blocked, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }
}
