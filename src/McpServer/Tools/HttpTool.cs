using McpServer.Protocol;
using McpServer.Configuration;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace McpServer.Tools;

/// <summary>
/// Tool for making HTTP requests to allowed external APIs
/// </summary>
public class HttpTool : ITool
{
    private readonly HttpClient _httpClient;
    private readonly HttpSettings _settings;

    public HttpTool(HttpClient httpClient, IOptions<HttpSettings> settings)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
    }

    public string Name => "http_request";

    public string Description => "Make HTTP requests to allowed external APIs. Use this to fetch data from REST APIs, webhooks, or web services. Only GET and POST methods are supported.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "get", "post", "allowed_hosts" }
            },
            ["url"] = new()
            {
                Type = "string",
                Description = "The URL to request (must be in allowed hosts list)"
            },
            ["headers"] = new()
            {
                Type = "string",
                Description = "Optional JSON object of headers (e.g., '{\"Authorization\": \"Bearer token\"}')"
            },
            ["body"] = new()
            {
                Type = "string",
                Description = "Request body for POST requests (JSON string)"
            }
        },
        Required = new List<string> { "action" }
    };

    public async Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action") ?? "allowed_hosts";

            var result = action.ToLower() switch
            {
                "get" => await ExecuteGetAsync(arguments, cancellationToken),
                "post" => await ExecutePostAsync(arguments, cancellationToken),
                "allowed_hosts" => GetAllowedHosts(),
                _ => $"Unknown action: {action}. Use 'get', 'post', or 'allowed_hosts'."
            };

            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = result }
                }
            };
        }
        catch (Exception ex)
        {
            return new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = $"Error: {ex.Message}" }
                },
                IsError = true
            };
        }
    }

    private string GetAllowedHosts()
    {
        if (_settings.AllowedHosts.Count == 0)
            return "No hosts are configured. Add hosts to appsettings.json under Http.AllowedHosts.\n\n" +
                   "Example hosts:\n  • api.github.com\n  • jsonplaceholder.typicode.com\n  • httpbin.org";

        var sb = new StringBuilder("Allowed hosts for HTTP requests:\n");
        foreach (var host in _settings.AllowedHosts)
        {
            sb.AppendLine($"  ✅ {host}");
        }
        return sb.ToString();
    }

    private async Task<string> ExecuteGetAsync(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var url = GetStringArg(arguments, "url");
        if (string.IsNullOrEmpty(url))
            return "Error: 'url' parameter is required.";

        ValidateUrl(url);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddHeaders(request, arguments);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await FormatResponseAsync(response, cancellationToken);
    }

    private async Task<string> ExecutePostAsync(Dictionary<string, object>? arguments, CancellationToken cancellationToken)
    {
        var url = GetStringArg(arguments, "url");
        var body = GetStringArg(arguments, "body");

        if (string.IsNullOrEmpty(url))
            return "Error: 'url' parameter is required.";

        ValidateUrl(url);

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        AddHeaders(request, arguments);

        if (!string.IsNullOrEmpty(body))
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await FormatResponseAsync(response, cancellationToken);
    }

    private void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Invalid URL: {url}");

        if (uri.Scheme != "https" && uri.Scheme != "http")
            throw new ArgumentException("Only HTTP and HTTPS URLs are allowed.");

        var host = uri.Host.ToLower();
        var isAllowed = _settings.AllowedHosts.Any(allowed =>
            host.Equals(allowed, StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("." + allowed, StringComparison.OrdinalIgnoreCase));

        if (!isAllowed)
            throw new UnauthorizedAccessException(
                $"Host '{uri.Host}' is not in the allowed hosts list. Use 'allowed_hosts' action to see permitted hosts.");
    }

    private void AddHeaders(HttpRequestMessage request, Dictionary<string, object>? arguments)
    {
        var headersJson = GetStringArg(arguments, "headers");
        if (string.IsNullOrEmpty(headersJson))
            return;

        try
        {
            var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
            if (headers != null)
            {
                foreach (var header in headers)
                {
                    // Skip content-type as it's set separately
                    if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                }
            }
        }
        catch (JsonException)
        {
            // Ignore invalid headers JSON
        }
    }

    private async Task<string> FormatResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Status: {(int)response.StatusCode} {response.StatusCode}");
        sb.AppendLine();

        // Selected headers
        sb.AppendLine("Headers:");
        var interestingHeaders = new[] { "Content-Type", "Content-Length", "Date", "Server", "X-RateLimit-Remaining" };
        foreach (var headerName in interestingHeaders)
        {
            if (response.Headers.TryGetValues(headerName, out var values) ||
                response.Content.Headers.TryGetValues(headerName, out values))
            {
                sb.AppendLine($"  {headerName}: {string.Join(", ", values)}");
            }
        }
        sb.AppendLine();

        // Body
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        
        // Limit response size
        const int maxLength = 10000;
        if (body.Length > maxLength)
        {
            body = body.Substring(0, maxLength) + $"\n... (truncated, {body.Length - maxLength} more characters)";
        }

        // Try to format JSON
        if (response.Content.Headers.ContentType?.MediaType?.Contains("json") == true)
        {
            try
            {
                var json = JsonSerializer.Deserialize<JsonElement>(body);
                body = JsonSerializer.Serialize(json, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { /* Keep original body */ }
        }

        sb.AppendLine("Body:");
        sb.AppendLine(body);

        return sb.ToString();
    }

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }
}
