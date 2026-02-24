using McpServer.Progress;
using McpServer.Protocol;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace McpServer.Tools;

// Argha - 2026-02-18 - Text processing tool: regex, word count, diff, formatting
public class TextTool : ITool
{
    // Argha - 2026-02-18 - 1 MB input cap to prevent memory abuse
    private const int MaxInputSize = 1024 * 1024;
    // Argha - 2026-02-18 - 5 second regex timeout to prevent ReDoS attacks
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    public string Name => "text";

    public string Description => "Process text: regex match/replace, word count, diff two texts, format JSON/XML. Use this for text analysis and transformation.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "regex_match", "regex_replace", "word_count", "diff_text", "format_json", "format_xml" }
            },
            ["text"] = new()
            {
                Type = "string",
                Description = "The input text to process"
            },
            ["pattern"] = new()
            {
                Type = "string",
                Description = "Regex pattern (for regex_match and regex_replace)"
            },
            ["replacement"] = new()
            {
                Type = "string",
                Description = "Replacement string (for regex_replace)"
            },
            ["text1"] = new()
            {
                Type = "string",
                Description = "First text (for diff_text)"
            },
            ["text2"] = new()
            {
                Type = "string",
                Description = "Second text (for diff_text)"
            }
        },
        Required = new List<string> { "action" }
    };

    // Argha - 2026-02-24 - progress not used; text operations complete synchronously
    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, IProgressReporter? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action") ?? "";

            var result = action.ToLower() switch
            {
                "regex_match" => RegexMatch(arguments),
                "regex_replace" => RegexReplace(arguments),
                "word_count" => WordCount(arguments),
                "diff_text" => DiffText(arguments),
                "format_json" => FormatJson(arguments),
                "format_xml" => FormatXml(arguments),
                _ => $"Unknown action: {action}. Use 'regex_match', 'regex_replace', 'word_count', 'diff_text', 'format_json', or 'format_xml'."
            };

            return Task.FromResult(new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = result }
                }
            });
        }
        catch (RegexMatchTimeoutException)
        {
            return Task.FromResult(new ToolCallResult
            {
                Content = new List<ContentBlock>
                {
                    new() { Type = "text", Text = "Error: Regex operation timed out after 5 seconds. The pattern may be too complex." }
                },
                IsError = true
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

    private string RegexMatch(Dictionary<string, object>? arguments)
    {
        var pattern = GetStringArg(arguments, "pattern");
        var text = GetStringArg(arguments, "text");

        if (string.IsNullOrEmpty(pattern))
            return "Error: 'pattern' parameter is required.";
        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (text.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        var regex = new Regex(pattern, RegexOptions.None, RegexTimeout);
        var matches = regex.Matches(text);

        if (matches.Count == 0)
            return "No matches found.";

        var lines = new List<string> { $"Found {matches.Count} match(es):" };
        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            lines.Add($"\nMatch {i + 1}: \"{match.Value}\" (position {match.Index})");
            if (match.Groups.Count > 1)
            {
                for (int g = 1; g < match.Groups.Count; g++)
                {
                    lines.Add($"  Group {g}: \"{match.Groups[g].Value}\"");
                }
            }
        }

        return string.Join("\n", lines);
    }

    private string RegexReplace(Dictionary<string, object>? arguments)
    {
        var pattern = GetStringArg(arguments, "pattern");
        var replacement = GetStringArg(arguments, "replacement");
        var text = GetStringArg(arguments, "text");

        if (string.IsNullOrEmpty(pattern))
            return "Error: 'pattern' parameter is required.";
        if (replacement == null)
            return "Error: 'replacement' parameter is required.";
        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (text.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        var regex = new Regex(pattern, RegexOptions.None, RegexTimeout);
        var count = regex.Matches(text).Count;
        var result = regex.Replace(text, replacement);

        return $"Replacements made: {count}\n\n--- Result ---\n{result}";
    }

    private string WordCount(Dictionary<string, object>? arguments)
    {
        var text = GetStringArg(arguments, "text");

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (text.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        var chars = text.Length;
        var lines = text.Split('\n').Length;
        var words = Regex.Split(text.Trim(), @"\s+", RegexOptions.None, RegexTimeout)
            .Where(w => !string.IsNullOrEmpty(w)).Count();
        // Argha - 2026-02-18 - sentence detection: split on .!? followed by space or end
        var sentences = Regex.Matches(text, @"[.!?]+(?:\s|$)", RegexOptions.None, RegexTimeout).Count;
        if (sentences == 0 && text.Trim().Length > 0)
            sentences = 1;

        return $"Word count statistics:\n" +
               $"  Characters: {chars}\n" +
               $"  Words: {words}\n" +
               $"  Lines: {lines}\n" +
               $"  Sentences: {sentences}";
    }

    private string DiffText(Dictionary<string, object>? arguments)
    {
        var text1 = GetStringArg(arguments, "text1");
        var text2 = GetStringArg(arguments, "text2");

        if (text1 == null)
            return "Error: 'text1' parameter is required.";
        if (text2 == null)
            return "Error: 'text2' parameter is required.";
        if (text1.Length > MaxInputSize || text2.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        var lines1 = text1.Split('\n');
        var lines2 = text2.Split('\n');

        if (text1 == text2)
            return "Texts are identical.";

        // Argha - 2026-02-18 - simple line-by-line LCS-based diff
        var diff = ComputeLineDiff(lines1, lines2);
        return $"Diff result ({diff.Count} lines):\n{string.Join("\n", diff)}";
    }

    // Argha - 2026-02-18 - LCS diff algorithm for line-by-line comparison
    private static List<string> ComputeLineDiff(string[] lines1, string[] lines2)
    {
        int m = lines1.Length, n = lines2.Length;
        var dp = new int[m + 1, n + 1];

        for (int i = 1; i <= m; i++)
            for (int j = 1; j <= n; j++)
                dp[i, j] = lines1[i - 1] == lines2[j - 1]
                    ? dp[i - 1, j - 1] + 1
                    : Math.Max(dp[i - 1, j], dp[i, j - 1]);

        var result = new List<string>();
        int x = m, y = n;
        while (x > 0 || y > 0)
        {
            if (x > 0 && y > 0 && lines1[x - 1] == lines2[y - 1])
            {
                result.Add($"  {lines1[x - 1]}");
                x--; y--;
            }
            else if (y > 0 && (x == 0 || dp[x, y - 1] >= dp[x - 1, y]))
            {
                result.Add($"+ {lines2[y - 1]}");
                y--;
            }
            else
            {
                result.Add($"- {lines1[x - 1]}");
                x--;
            }
        }

        result.Reverse();
        return result;
    }

    private string FormatJson(Dictionary<string, object>? arguments)
    {
        var text = GetStringArg(arguments, "text");

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (text.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        try
        {
            var element = JsonSerializer.Deserialize<JsonElement>(text);
            var formatted = JsonSerializer.Serialize(element, new JsonSerializerOptions { WriteIndented = true });
            return $"Formatted JSON:\n{formatted}";
        }
        catch (JsonException ex)
        {
            return $"Error: Invalid JSON - {ex.Message}";
        }
    }

    private string FormatXml(Dictionary<string, object>? arguments)
    {
        var text = GetStringArg(arguments, "text");

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (text.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        try
        {
            var doc = XDocument.Parse(text);
            return $"Formatted XML:\n{doc.ToString()}";
        }
        catch (System.Xml.XmlException ex)
        {
            return $"Error: Invalid XML - {ex.Message}";
        }
    }

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }
}
