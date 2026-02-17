using McpServer.Configuration;
using McpServer.Protocol;
using McpServer.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace McpServer;

/// <summary>
/// Main MCP Server that handles JSON-RPC communication over stdio
/// </summary>
public class McpServerHandler
{
    private readonly IEnumerable<ITool> _tools;
    private readonly ServerSettings _serverSettings;
    private readonly ILogger<McpServerHandler> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public McpServerHandler(
        IEnumerable<ITool> tools,
        IOptions<ServerSettings> serverSettings,
        ILogger<McpServerHandler> logger)
    {
        _tools = tools;
        _serverSettings = serverSettings.Value;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    /// Run the server, reading from stdin and writing to stdout
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("MCP Server starting...");
        _logger.LogInformation("Available tools: {Tools}", string.Join(", ", _tools.Select(t => t.Name)));

        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();
        using var reader = new StreamReader(stdin);
        using var writer = new StreamWriter(stdout) { AutoFlush = true };

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line == null) break; // EOF

                if (string.IsNullOrWhiteSpace(line)) continue;

                _logger.LogDebug("Received: {Message}", line);

                var response = await ProcessMessageAsync(line, cancellationToken);
                
                if (response != null)
                {
                    var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                    _logger.LogDebug("Sending: {Response}", responseJson);
                    await writer.WriteLineAsync(responseJson);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
            }
        }

        _logger.LogInformation("MCP Server shutting down...");
    }

    private async Task<JsonRpcResponse?> ProcessMessageAsync(string message, CancellationToken cancellationToken)
    {
        JsonRpcRequest? request;
        
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcRequest>(message, _jsonOptions);
        }
        catch (JsonException ex)
        {
            return new JsonRpcResponse
            {
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.ParseError,
                    Message = $"Parse error: {ex.Message}"
                }
            };
        }

        if (request == null)
        {
            return new JsonRpcResponse
            {
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.InvalidRequest,
                    Message = "Invalid request"
                }
            };
        }

        // Handle notifications (no id = no response expected)
        if (request.Id == null && request.Method == "notifications/initialized")
        {
            _logger.LogInformation("Client initialized notification received");
            return null;
        }

        return request.Method switch
        {
            "initialize" => HandleInitialize(request),
            "tools/list" => HandleListTools(request),
            "tools/call" => await HandleToolCallAsync(request, cancellationToken),
            "ping" => HandlePing(request),
            _ => new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.MethodNotFound,
                    Message = $"Method not found: {request.Method}"
                }
            }
        };
    }

    private JsonRpcResponse HandleInitialize(JsonRpcRequest request)
    {
        _logger.LogInformation("Initialize request received");

        InitializeParams? initParams = null;
        if (request.Params.HasValue)
        {
            initParams = JsonSerializer.Deserialize<InitializeParams>(
                request.Params.Value.GetRawText(), _jsonOptions);
        }

        _logger.LogInformation("Client: {ClientName} {ClientVersion}", 
            initParams?.ClientInfo?.Name ?? "Unknown",
            initParams?.ClientInfo?.Version ?? "Unknown");

        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = new InitializeResult
            {
                ProtocolVersion = "2024-11-05",
                Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = false }
                },
                ServerInfo = new ServerInfo
                {
                    Name = _serverSettings.Name,
                    Version = _serverSettings.Version
                }
            }
        };
    }

    private JsonRpcResponse HandleListTools(JsonRpcRequest request)
    {
        _logger.LogDebug("List tools request received");

        var toolDefinitions = _tools.Select(t => new ToolDefinition
        {
            Name = t.Name,
            Description = t.Description,
            InputSchema = t.InputSchema
        }).ToList();

        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = new ListToolsResult
            {
                Tools = toolDefinitions
            }
        };
    }

    private async Task<JsonRpcResponse> HandleToolCallAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (!request.Params.HasValue)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.InvalidParams,
                    Message = "Missing params"
                }
            };
        }

        var callParams = JsonSerializer.Deserialize<ToolCallParams>(
            request.Params.Value.GetRawText(), _jsonOptions);

        if (callParams == null || string.IsNullOrEmpty(callParams.Name))
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.InvalidParams,
                    Message = "Missing tool name"
                }
            };
        }

        _logger.LogInformation("Tool call: {ToolName}", callParams.Name);

        var tool = _tools.FirstOrDefault(t => t.Name == callParams.Name);
        if (tool == null)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.MethodNotFound,
                    Message = $"Tool not found: {callParams.Name}"
                }
            };
        }

        // Convert JsonElement values to proper types
        var arguments = ConvertArguments(callParams.Arguments);

        try
        {
            var result = await tool.ExecuteAsync(arguments, cancellationToken);
            
            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = result
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", callParams.Name);
            
            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = new ToolCallResult
                {
                    Content = new List<ContentBlock>
                    {
                        new() { Type = "text", Text = $"Error executing tool: {ex.Message}" }
                    },
                    IsError = true
                }
            };
        }
    }

    private JsonRpcResponse HandlePing(JsonRpcRequest request)
    {
        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = new { }
        };
    }

    private Dictionary<string, object>? ConvertArguments(Dictionary<string, object>? arguments)
    {
        if (arguments == null) return null;

        var result = new Dictionary<string, object>();
        foreach (var kvp in arguments)
        {
            if (kvp.Value is JsonElement element)
            {
                result[kvp.Key] = element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString() ?? "",
                    JsonValueKind.Number => element.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null!,
                    _ => element.GetRawText()
                };
            }
            else
            {
                result[kvp.Key] = kvp.Value;
            }
        }
        return result;
    }
}
