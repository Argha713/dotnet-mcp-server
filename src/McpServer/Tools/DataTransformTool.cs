using McpServer.Progress;
using McpServer.Protocol;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace McpServer.Tools;

// Argha - 2026-02-18 - Data transformation tool: JSON query, CSV/JSON/XML conversion, base64, hashing
public class DataTransformTool : ITool
{
    // Argha - 2026-02-18 - 1 MB input cap
    private const int MaxInputSize = 1024 * 1024;

    private static readonly HashSet<string> AllowedHashAlgorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        "md5", "sha1", "sha256", "sha384", "sha512"
    };

    public string Name => "data_transform";

    public string Description => "Transform data between formats: JSON query, CSV/JSON/XML conversion, base64 encode/decode, and hashing. Use this for data format conversions.";

    public JsonSchema InputSchema => new()
    {
        Type = "object",
        Properties = new Dictionary<string, JsonSchemaProperty>
        {
            ["action"] = new()
            {
                Type = "string",
                Description = "The action to perform",
                Enum = new List<string> { "json_query", "csv_to_json", "json_to_csv", "xml_to_json", "base64_encode", "base64_decode", "hash" }
            },
            ["text"] = new()
            {
                Type = "string",
                Description = "The input text/data to transform"
            },
            ["query"] = new()
            {
                Type = "string",
                Description = "Dot-notation query for json_query (e.g., 'data.users[0].name', 'items[*].id')"
            },
            ["delimiter"] = new()
            {
                Type = "string",
                Description = "Delimiter for CSV operations (default: ',')"
            },
            ["algorithm"] = new()
            {
                Type = "string",
                Description = "Hash algorithm: md5, sha1, sha256, sha384, sha512 (default: sha256)"
            }
        },
        Required = new List<string> { "action" }
    };

    // Argha - 2026-02-24 - progress not used; data transform operations complete synchronously
    public Task<ToolCallResult> ExecuteAsync(Dictionary<string, object>? arguments, IProgressReporter? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var action = GetStringArg(arguments, "action") ?? "";

            var result = action.ToLower() switch
            {
                "json_query" => JsonQuery(arguments),
                "csv_to_json" => CsvToJson(arguments),
                "json_to_csv" => JsonToCsv(arguments),
                "xml_to_json" => XmlToJson(arguments),
                "base64_encode" => Base64Encode(arguments),
                "base64_decode" => Base64Decode(arguments),
                "hash" => ComputeHash(arguments),
                _ => $"Unknown action: {action}. Use 'json_query', 'csv_to_json', 'json_to_csv', 'xml_to_json', 'base64_encode', 'base64_decode', or 'hash'."
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

    // Argha - 2026-02-18 - custom dot-notation JSON query (no external JSONPath library)
    private string JsonQuery(Dictionary<string, object>? arguments)
    {
        var text = GetStringArg(arguments, "text");
        var query = GetStringArg(arguments, "query");

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (string.IsNullOrEmpty(query))
            return "Error: 'query' parameter is required.";
        if (text.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        try
        {
            var root = JsonSerializer.Deserialize<JsonElement>(text);
            var results = NavigateJsonPath(root, query);

            if (results.Count == 0)
                return "No results found for the given query.";

            if (results.Count == 1)
            {
                var formatted = JsonSerializer.Serialize(results[0], new JsonSerializerOptions { WriteIndented = true });
                return $"Query result:\n{formatted}";
            }

            var items = results.Select(r => JsonSerializer.Serialize(r, new JsonSerializerOptions { WriteIndented = true }));
            return $"Query returned {results.Count} results:\n{string.Join("\n", items)}";
        }
        catch (JsonException ex)
        {
            return $"Error: Invalid JSON - {ex.Message}";
        }
    }

    // Argha - 2026-02-18 - navigate JSON using dot-notation: data.users[0].name, items[*].id
    private static List<JsonElement> NavigateJsonPath(JsonElement element, string path)
    {
        var current = new List<JsonElement> { element };
        var segments = ParsePathSegments(path);

        foreach (var segment in segments)
        {
            var next = new List<JsonElement>();
            foreach (var el in current)
            {
                if (segment.IsWildcardIndex)
                {
                    if (el.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in el.EnumerateArray())
                            next.Add(item);
                    }
                }
                else if (segment.ArrayIndex.HasValue)
                {
                    if (el.ValueKind == JsonValueKind.Array)
                    {
                        var idx = segment.ArrayIndex.Value;
                        if (idx >= 0 && idx < el.GetArrayLength())
                            next.Add(el[idx]);
                    }
                }
                else if (!string.IsNullOrEmpty(segment.PropertyName))
                {
                    if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(segment.PropertyName, out var prop))
                        next.Add(prop);
                }
            }
            current = next;
        }

        return current;
    }

    private static List<PathSegment> ParsePathSegments(string path)
    {
        var segments = new List<PathSegment>();
        var parts = path.Split('.');

        foreach (var part in parts)
        {
            // Argha - 2026-02-18 - handle array notation like items[0] or items[*]
            var bracketIdx = part.IndexOf('[');
            if (bracketIdx >= 0)
            {
                var propName = part[..bracketIdx];
                if (!string.IsNullOrEmpty(propName))
                    segments.Add(new PathSegment { PropertyName = propName });

                var bracketContent = part[(bracketIdx + 1)..^1];
                if (bracketContent == "*")
                    segments.Add(new PathSegment { IsWildcardIndex = true });
                else if (int.TryParse(bracketContent, out var idx))
                    segments.Add(new PathSegment { ArrayIndex = idx });
            }
            else
            {
                segments.Add(new PathSegment { PropertyName = part });
            }
        }

        return segments;
    }

    private class PathSegment
    {
        public string? PropertyName { get; set; }
        public int? ArrayIndex { get; set; }
        public bool IsWildcardIndex { get; set; }
    }

    private string CsvToJson(Dictionary<string, object>? arguments)
    {
        var text = GetStringArg(arguments, "text");
        var delimiter = GetStringArg(arguments, "delimiter") ?? ",";

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (text.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        var delimChar = delimiter.Length > 0 ? delimiter[0] : ',';
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r')).ToList();

        if (lines.Count == 0)
            return "Error: CSV input is empty.";

        var headers = ParseCsvLine(lines[0], delimChar);
        var records = new List<Dictionary<string, string>>();

        for (int i = 1; i < lines.Count; i++)
        {
            var values = ParseCsvLine(lines[i], delimChar);
            var record = new Dictionary<string, string>();
            for (int j = 0; j < headers.Count; j++)
            {
                record[headers[j]] = j < values.Count ? values[j] : "";
            }
            records.Add(record);
        }

        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
        return $"Converted {records.Count} record(s) to JSON:\n{json}";
    }

    // Argha - 2026-02-18 - RFC 4180 CSV parsing: handle quoted fields containing commas and quotes
    private static List<string> ParseCsvLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private string JsonToCsv(Dictionary<string, object>? arguments)
    {
        var text = GetStringArg(arguments, "text");

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (text.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        try
        {
            var array = JsonSerializer.Deserialize<JsonElement>(text);
            if (array.ValueKind != JsonValueKind.Array)
                return "Error: JSON must be an array of objects for CSV conversion.";

            var items = array.EnumerateArray().ToList();
            if (items.Count == 0)
                return "Error: JSON array is empty.";

            // Argha - 2026-02-18 - collect all unique keys across all objects for headers
            var headers = new List<string>();
            var headerSet = new HashSet<string>();
            foreach (var item in items)
            {
                if (item.ValueKind != JsonValueKind.Object)
                    return "Error: All array items must be objects for CSV conversion.";
                foreach (var prop in item.EnumerateObject())
                {
                    if (headerSet.Add(prop.Name))
                        headers.Add(prop.Name);
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(EscapeCsvField)));

            foreach (var item in items)
            {
                var values = headers.Select(h =>
                {
                    if (item.TryGetProperty(h, out var val))
                    {
                        return val.ValueKind switch
                        {
                            JsonValueKind.String => val.GetString() ?? "",
                            JsonValueKind.Null => "",
                            _ => val.GetRawText()
                        };
                    }
                    return "";
                });
                sb.AppendLine(string.Join(",", values.Select(EscapeCsvField)));
            }

            return $"Converted {items.Count} record(s) to CSV:\n{sb.ToString().TrimEnd()}";
        }
        catch (JsonException ex)
        {
            return $"Error: Invalid JSON - {ex.Message}";
        }
    }

    private static string EscapeCsvField(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            return $"\"{field.Replace("\"", "\"\"")}\"";
        return field;
    }

    private string XmlToJson(Dictionary<string, object>? arguments)
    {
        var text = GetStringArg(arguments, "text");

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (text.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        try
        {
            // Argha - 2026-02-18 - XXE prevention: prohibit DTD processing and disable external resolver
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            };

            using var stringReader = new System.IO.StringReader(text);
            using var xmlReader = XmlReader.Create(stringReader, settings);
            var doc = XDocument.Load(xmlReader);

            var json = ConvertXmlElementToDict(doc.Root!);
            var result = new Dictionary<string, object> { [doc.Root!.Name.LocalName] = json };
            var formatted = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
            return $"Converted XML to JSON:\n{formatted}";
        }
        catch (XmlException ex)
        {
            return $"Error: Invalid XML - {ex.Message}";
        }
    }

    // Argha - 2026-02-18 - recursive XML to dictionary: attributes as @attr, text as #text
    private static object ConvertXmlElementToDict(XElement element)
    {
        var dict = new Dictionary<string, object>();

        foreach (var attr in element.Attributes())
        {
            dict[$"@{attr.Name.LocalName}"] = attr.Value;
        }

        var childGroups = element.Elements().GroupBy(e => e.Name.LocalName).ToList();
        foreach (var group in childGroups)
        {
            var items = group.ToList();
            if (items.Count == 1)
                dict[group.Key] = ConvertXmlElementToDict(items[0]);
            else
                dict[group.Key] = items.Select(ConvertXmlElementToDict).ToList();
        }

        if (!element.HasElements && !string.IsNullOrEmpty(element.Value))
        {
            if (dict.Count == 0)
                return element.Value;
            dict["#text"] = element.Value;
        }

        return dict;
    }

    private string Base64Encode(Dictionary<string, object>? arguments)
    {
        var text = GetStringArg(arguments, "text");

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (text.Length > MaxInputSize)
            return $"Error: Input text exceeds maximum size of {MaxInputSize / 1024}KB.";

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        return $"Base64 encoded:\n{encoded}";
    }

    private string Base64Decode(Dictionary<string, object>? arguments)
    {
        var text = GetStringArg(arguments, "text");

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";

        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(text));
            return $"Base64 decoded:\n{decoded}";
        }
        catch (FormatException)
        {
            return "Error: Invalid Base64 string.";
        }
    }

    private string ComputeHash(Dictionary<string, object>? arguments)
    {
        var text = GetStringArg(arguments, "text");
        var algorithm = GetStringArg(arguments, "algorithm") ?? "sha256";

        if (string.IsNullOrEmpty(text))
            return "Error: 'text' parameter is required.";
        if (!AllowedHashAlgorithms.Contains(algorithm))
            return $"Error: Unsupported hash algorithm '{algorithm}'. Supported: {string.Join(", ", AllowedHashAlgorithms)}.";

        var bytes = Encoding.UTF8.GetBytes(text);
        byte[] hashBytes = algorithm.ToLower() switch
        {
            "md5" => MD5.HashData(bytes),
            "sha1" => SHA1.HashData(bytes),
            "sha256" => SHA256.HashData(bytes),
            "sha384" => SHA384.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            _ => SHA256.HashData(bytes)
        };

        var hex = Convert.ToHexString(hashBytes).ToLower();
        return $"Hash ({algorithm.ToUpper()}):\n{hex}";
    }

    private static string? GetStringArg(Dictionary<string, object>? args, string key)
    {
        if (args == null || !args.TryGetValue(key, out var value))
            return null;
        return value?.ToString();
    }
}
