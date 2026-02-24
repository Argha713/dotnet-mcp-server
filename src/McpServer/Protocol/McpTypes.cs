using System.Text.Json.Serialization;

namespace McpServer.Protocol;

/// <summary>
/// MCP Server capabilities
/// </summary>
public class ServerCapabilities
{
    [JsonPropertyName("tools")]
    public ToolsCapability? Tools { get; set; }

    [JsonPropertyName("resources")]
    public ResourcesCapability? Resources { get; set; }

    [JsonPropertyName("prompts")]
    public PromptsCapability? Prompts { get; set; }

    // Argha - 2026-02-24 - advertise logging capability so clients know they can call logging/setLevel
    [JsonPropertyName("logging")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LoggingCapability? Logging { get; set; }
}

public class ToolsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; } = false;
}

public class ResourcesCapability
{
    [JsonPropertyName("subscribe")]
    public bool Subscribe { get; set; } = false;

    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; } = false;
}

public class PromptsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; } = false;
}

// Argha - 2026-02-24 - MCP logging capability (no sub-fields in protocol version 2024-11-05)
public class LoggingCapability { }

/// <summary>
/// MCP Initialize request params
/// </summary>
public class InitializeParams
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = string.Empty;

    [JsonPropertyName("capabilities")]
    public ClientCapabilities? Capabilities { get; set; }

    [JsonPropertyName("clientInfo")]
    public ClientInfo? ClientInfo { get; set; }
}

public class ClientCapabilities
{
    [JsonPropertyName("roots")]
    public RootsCapability? Roots { get; set; }

    [JsonPropertyName("sampling")]
    public SamplingCapability? Sampling { get; set; }
}

public class RootsCapability
{
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

public class SamplingCapability { }

public class ClientInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// MCP Initialize response result
/// </summary>
public class InitializeResult
{
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();

    [JsonPropertyName("serverInfo")]
    public ServerInfo ServerInfo { get; set; } = new();
}

public class ServerInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}

/// <summary>
/// MCP Tool definition
/// </summary>
public class ToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public JsonSchema InputSchema { get; set; } = new();
}

/// <summary>
/// JSON Schema for tool inputs
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

/// <summary>
/// MCP Tool call request
/// </summary>
public class ToolCallParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, object>? Arguments { get; set; }

    // Argha - 2026-02-24 - _meta carries optional progressToken from the client (MCP progress protocol)
    [JsonPropertyName("_meta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolCallMeta? Meta { get; set; }
}

// Argha - 2026-02-24 - MCP _meta block on tools/call requests; carries progressToken when client wants progress notifications
/// <summary>
/// Optional metadata on tools/call requests; clients set progressToken to request progress notifications.
/// </summary>
public class ToolCallMeta
{
    [JsonPropertyName("progressToken")]
    public string? ProgressToken { get; set; }
}

// Argha - 2026-02-24 - MCP notifications/progress protocol types (sent by server during long-running tool calls)

/// <summary>
/// A JSON-RPC notification emitted by the server during a long-running tool call.
/// No id — no response expected by the client.
/// </summary>
public class ProgressNotification
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "notifications/progress";

    [JsonPropertyName("params")]
    public ProgressParams Params { get; set; } = new();
}

/// <summary>
/// Params payload inside notifications/progress
/// </summary>
public class ProgressParams
{
    [JsonPropertyName("progressToken")]
    public string ProgressToken { get; set; } = string.Empty;

    [JsonPropertyName("progress")]
    public double Progress { get; set; }

    [JsonPropertyName("total")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Total { get; set; }
}

/// <summary>
/// MCP Tool call result
/// </summary>
public class ToolCallResult
{
    [JsonPropertyName("content")]
    public List<ContentBlock> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; } = false;
}

/// <summary>
/// Content block (text or image)
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

/// <summary>
/// List tools response
/// </summary>
public class ListToolsResult
{
    [JsonPropertyName("tools")]
    public List<ToolDefinition> Tools { get; set; } = new();
}

// Argha - 2026-02-24 - MCP resource models for resources/list and resources/read

/// <summary>
/// A single MCP resource (file, config, etc.) exposed by the server
/// </summary>
public class Resource
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }
}

/// <summary>
/// resources/list response
/// </summary>
public class ListResourcesResult
{
    [JsonPropertyName("resources")]
    public List<Resource> Resources { get; set; } = new();
}

/// <summary>
/// resources/read request params
/// </summary>
public class ReadResourceParams
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
}

/// <summary>
/// Contents of a single resource (text or blob)
/// </summary>
public class ResourceContents
{
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("blob")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Blob { get; set; }
}

/// <summary>
/// resources/read response
/// </summary>
public class ReadResourceResult
{
    [JsonPropertyName("contents")]
    public List<ResourceContents> Contents { get; set; } = new();
}

// Argha - 2026-02-24 - MCP prompt models for prompts/list and prompts/get

/// <summary>
/// Describes a single argument accepted by a prompt template
/// </summary>
public class PromptArgument
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; } = false;
}

/// <summary>
/// A named, parameterized prompt template exposed by the server
/// </summary>
public class Prompt
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("arguments")]
    public List<PromptArgument> Arguments { get; set; } = new();
}

/// <summary>
/// prompts/list response
/// </summary>
public class ListPromptsResult
{
    [JsonPropertyName("prompts")]
    public List<Prompt> Prompts { get; set; } = new();
}

/// <summary>
/// prompts/get request params
/// </summary>
public class GetPromptParams
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, string>? Arguments { get; set; }
}

/// <summary>
/// The content block inside a prompt message (text only for built-in prompts)
/// </summary>
public class PromptMessageContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }
}

/// <summary>
/// A single rendered message in a prompt result
/// </summary>
public class PromptMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "user";

    [JsonPropertyName("content")]
    public PromptMessageContent Content { get; set; } = new();
}

/// <summary>
/// prompts/get response
/// </summary>
public class GetPromptResult
{
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }

    [JsonPropertyName("messages")]
    public List<PromptMessage> Messages { get; set; } = new();
}

// Argha - 2026-02-24 - MCP logging protocol models for logging/setLevel and notifications/message

/// <summary>
/// Syslog-style log levels used by the MCP logging protocol.
/// Order matters: values increase with severity for threshold comparisons.
/// </summary>
public enum McpLogLevel
{
    Debug = 0,
    Info = 1,
    Notice = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    Alert = 6,
    Emergency = 7
}

/// <summary>
/// Params for the logging/setLevel request sent by the client
/// </summary>
public class SetLevelParams
{
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;
}

/// <summary>
/// Body of a notifications/message notification sent by the server to the client
/// </summary>
public class LogMessageNotification
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; } = "notifications/message";

    [JsonPropertyName("params")]
    public LogMessageParams Params { get; set; } = new();
}

/// <summary>
/// Params payload inside notifications/message
/// </summary>
public class LogMessageParams
{
    [JsonPropertyName("level")]
    public string Level { get; set; } = string.Empty;

    [JsonPropertyName("logger")]
    public string Logger { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}
