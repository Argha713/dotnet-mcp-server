// Argha - 2026-02-24 - JsonSchema and JsonSchemaProperty extracted to abstractions
// so plugin tools can declare their InputSchema without referencing the host executable.
using System.Text.Json.Serialization;

namespace McpServer.Protocol;

/// <summary>
/// JSON Schema object used to declare a tool's accepted input parameters.
/// </summary>
public class JsonSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, JsonSchemaProperty>? Properties { get; set; }

    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }
}

/// <summary>
/// Describes a single property in a tool's input schema.
/// </summary>
public class JsonSchemaProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; set; }

    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Default { get; set; }
}
