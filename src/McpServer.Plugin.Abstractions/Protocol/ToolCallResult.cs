// Argha - 2026-02-24 - Phase 5.1: ToolCallResult and ContentBlock extracted to abstractions
// so plugin tools can return results without referencing the host executable.
using System.Text.Json.Serialization;

namespace McpServer.Protocol;

/// <summary>
/// The result of a tool call. Set IsError=true (with a descriptive text block) on failure;
/// never throw from ExecuteAsync — unhandled exceptions are caught by the server but give poor error messages.
/// </summary>
public class ToolCallResult
{
    [JsonPropertyName("content")]
    public List<ContentBlock> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; } = false;
}

/// <summary>
/// A single content block within a tool result. Type "text" is the most common;
/// type "image" is supported by some clients (set Data to base64 and MimeType accordingly).
/// </summary>
public class ContentBlock
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Data { get; set; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }
}
