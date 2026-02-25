using McpServer.Audit;
using McpServer.Configuration;
using McpServer.Logging;
using McpServer.Progress;
using McpServer.Prompts;
using McpServer.Protocol;
using McpServer.RateLimiting;
using McpServer.Resources;
using McpServer.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text.Json;

namespace McpServer;

/// <summary>
/// Main MCP Server that handles JSON-RPC communication over stdio
/// </summary>
public class McpServerHandler
{
    private readonly IEnumerable<ITool> _tools;
    // Argha - 2026-02-24 - resource providers registered alongside tools
    private readonly IEnumerable<IResourceProvider> _resourceProviders;
    // Argha - 2026-02-24 - prompt providers for prompts/list and prompts/get
    private readonly IEnumerable<IPromptProvider> _promptProviders;
    private readonly ServerSettings _serverSettings;
    private readonly ILogger<McpServerHandler> _logger;
    // Argha - 2026-02-24 - sink receives the stdout writer at RunAsync startup and forwards logs to client
    private readonly McpLogSink _logSink;
    // Argha - 2026-02-25 - Phase 6.2: persists an audit record for every tool call
    private readonly IAuditLogger _auditLogger;
    // Argha - 2026-02-25 - Phase 6.3: enforces per-tool call-rate limits
    private readonly IRateLimiter _rateLimiter;
    private readonly JsonSerializerOptions _jsonOptions;
    // Argha - 2026-02-17 - initialization gate: reject tool calls before handshake completes (MCP spec)
    private bool _initialized;

    public McpServerHandler(
        IEnumerable<ITool> tools,
        IEnumerable<IResourceProvider> resourceProviders,
        IEnumerable<IPromptProvider> promptProviders,
        IOptions<ServerSettings> serverSettings,
        ILogger<McpServerHandler> logger,
        McpLogSink logSink,
        // Argha - 2026-02-25 - Phase 6.2: audit logger injected; NullAuditLogger used in tests
        IAuditLogger auditLogger,
        // Argha - 2026-02-25 - Phase 6.3: rate limiter injected; NullRateLimiter used in tests
        IRateLimiter rateLimiter)
    {
        _tools = tools;
        _resourceProviders = resourceProviders;
        _promptProviders = promptProviders;
        _serverSettings = serverSettings.Value;
        _logger = logger;
        _logSink = logSink;
        _auditLogger = auditLogger;
        _rateLimiter = rateLimiter;
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

        // Argha - 2026-02-24 - hand the writer to the log sink so ILogger calls can reach the client
        _logSink.Initialize(writer);

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

    // Argha - 2026-02-17 - changed from private to internal for unit test access via InternalsVisibleTo
    internal async Task<JsonRpcResponse?> ProcessMessageAsync(string message, CancellationToken cancellationToken)
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

        // Argha - 2026-02-17 - reject tool operations before initialize handshake (MCP spec compliance)
        if (!_initialized && request.Method != "initialize" && request.Method != "ping"
            && request.Method != "notifications/initialized")
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.InvalidRequest,
                    Message = "Server not initialized. Send 'initialize' request first."
                }
            };
        }

        return request.Method switch
        {
            "initialize" => HandleInitialize(request),
            "tools/list" => HandleListTools(request),
            "tools/call" => await HandleToolCallAsync(request, cancellationToken),
            // Argha - 2026-02-24 - resources protocol methods
            "resources/list" => await HandleListResourcesAsync(request, cancellationToken),
            "resources/read" => await HandleReadResourceAsync(request, cancellationToken),
            // Argha - 2026-02-24 - prompts protocol methods
            "prompts/list" => await HandleListPromptsAsync(request, cancellationToken),
            "prompts/get" => await HandleGetPromptAsync(request, cancellationToken),
            // Argha - 2026-02-24 - logging protocol: client sets the minimum forwarding level
            "logging/setLevel" => HandleSetLevel(request),
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
        _initialized = true;

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
                // Argha - 2026-02-24 - advertise Resources, Prompts and Logging capabilities
                Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability { ListChanged = false },
                    Resources = new ResourcesCapability { Subscribe = false, ListChanged = false },
                    Prompts = new PromptsCapability { ListChanged = false },
                    // Argha - 2026-02-24 - empty object signals the client that logging/setLevel is supported
                    Logging = new LoggingCapability()
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

        // Argha - 2026-02-24 - extract progressToken from _meta; create real reporter if present, no-op otherwise
        var progressToken = callParams.Meta?.ProgressToken;
        IProgressReporter progressReporter = !string.IsNullOrEmpty(progressToken)
            ? new ProgressReporter(progressToken, _logSink)
            : NullProgressReporter.Instance;

        // Argha - 2026-02-25 - Phase 6.2: unique ID ties this audit entry to one tools/call request
        var correlationId = Guid.NewGuid().ToString("N");
        // Argha - 2026-02-25 - Phase 6.2: capture wall-clock duration for the audit record
        var sw = Stopwatch.StartNew();
        // Argha - 2026-02-25 - Phase 6.2: extract action for the audit record (most tools have an "action" arg)
        var action = arguments?.GetValueOrDefault("action")?.ToString();

        // Argha - 2026-02-25 - Phase 6.3: enforce per-tool call-rate limit before execution
        if (!_rateLimiter.TryAcquire(callParams.Name))
        {
            sw.Stop();
            _logger.LogWarning("Rate limit exceeded for tool '{Tool}'", callParams.Name);
            await WriteAuditAsync(callParams.Name, action, arguments, "RateLimited", "Rate limit exceeded", correlationId, sw.ElapsedMilliseconds);

            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = new ToolCallResult
                {
                    Content = new List<ContentBlock>
                    {
                        new() { Type = "text", Text = $"Rate limit exceeded for tool '{callParams.Name}'. Please wait a moment and try again." }
                    },
                    IsError = true
                }
            };
        }

        try
        {
            var result = await tool.ExecuteAsync(arguments, progressReporter, cancellationToken);
            sw.Stop();

            // Argha - 2026-02-25 - Phase 6.2: record successful invocation
            await WriteAuditAsync(callParams.Name, action, arguments, "Success", null, correlationId, sw.ElapsedMilliseconds);

            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = result
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Tool execution failed: {ToolName}", callParams.Name);

            // Argha - 2026-02-25 - Phase 6.2: record failure with error details
            await WriteAuditAsync(callParams.Name, action, arguments, "Failure", ex.Message, correlationId, sw.ElapsedMilliseconds);

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

    // Argha - 2026-02-25 - Phase 6.2: builds and writes an AuditRecord; never propagates exceptions
    private async Task WriteAuditAsync(
        string toolName,
        string? action,
        Dictionary<string, object>? arguments,
        string outcome,
        string? errorMessage,
        string correlationId,
        long durationMs)
    {
        try
        {
            var record = new AuditRecord
            {
                Timestamp = DateTimeOffset.UtcNow,
                CorrelationId = correlationId,
                ToolName = toolName,
                Action = action,
                // Argha - 2026-02-25 - raw arguments; FileAuditLogger applies sanitization before writing
                Arguments = arguments,
                Outcome = outcome,
                ErrorMessage = errorMessage,
                DurationMs = durationMs,
            };

            await _auditLogger.LogCallAsync(record);
        }
        catch (Exception ex)
        {
            // Argha - 2026-02-25 - audit failure is non-fatal: log to stderr but do not surface to caller
            _logger.LogWarning(ex, "Audit log write failed for tool '{Tool}'; call completed normally.", toolName);
        }
    }

    // Argha - 2026-02-24 - aggregate resources from all registered providers
    private async Task<JsonRpcResponse> HandleListResourcesAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("List resources request received");

        var resources = new List<Resource>();
        foreach (var provider in _resourceProviders)
        {
            var providerResources = await provider.ListResourcesAsync(cancellationToken);
            resources.AddRange(providerResources);
        }

        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = new ListResourcesResult { Resources = resources }
        };
    }

    // Argha - 2026-02-24 - route read to the provider that owns the URI scheme
    private async Task<JsonRpcResponse> HandleReadResourceAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (!request.Params.HasValue)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = "Missing params" }
            };
        }

        var readParams = JsonSerializer.Deserialize<ReadResourceParams>(
            request.Params.Value.GetRawText(), _jsonOptions);

        if (readParams == null || string.IsNullOrEmpty(readParams.Uri))
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = "Missing uri parameter" }
            };
        }

        _logger.LogInformation("Resource read: {Uri}", readParams.Uri);

        var provider = _resourceProviders.FirstOrDefault(p => p.CanHandle(readParams.Uri));
        if (provider == null)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.MethodNotFound,
                    Message = $"No resource provider for URI scheme: {readParams.Uri}"
                }
            };
        }

        try
        {
            var contents = await provider.ReadResourceAsync(readParams.Uri, cancellationToken);
            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = new ReadResourceResult { Contents = new List<ResourceContents> { contents } }
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.InvalidParams,
                    Message = "Access denied: path is outside allowed directories."
                }
            };
        }
        catch (FileNotFoundException ex)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = ex.Message }
            };
        }
        catch (InvalidOperationException ex)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = ex.Message }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Resource read failed: {Uri}", readParams.Uri);
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InternalError, Message = $"Error reading resource: {ex.Message}" }
            };
        }
    }

    // Argha - 2026-02-24 - aggregate prompts from all registered providers
    private async Task<JsonRpcResponse> HandleListPromptsAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        _logger.LogDebug("List prompts request received");

        var prompts = new List<Prompt>();
        foreach (var provider in _promptProviders)
        {
            var providerPrompts = await provider.ListPromptsAsync(cancellationToken);
            prompts.AddRange(providerPrompts);
        }

        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = new ListPromptsResult { Prompts = prompts }
        };
    }

    // Argha - 2026-02-24 - route get to the provider that owns the prompt name
    private async Task<JsonRpcResponse> HandleGetPromptAsync(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        if (!request.Params.HasValue)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = "Missing params" }
            };
        }

        var getParams = JsonSerializer.Deserialize<GetPromptParams>(
            request.Params.Value.GetRawText(), _jsonOptions);

        if (getParams == null || string.IsNullOrEmpty(getParams.Name))
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = "Missing name parameter" }
            };
        }

        _logger.LogInformation("Prompt get: {Name}", getParams.Name);

        var provider = _promptProviders.FirstOrDefault(p => p.CanHandle(getParams.Name));
        if (provider == null)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.MethodNotFound,
                    Message = $"Prompt not found: {getParams.Name}"
                }
            };
        }

        try
        {
            var result = await provider.GetPromptAsync(getParams.Name, getParams.Arguments, cancellationToken);
            return new JsonRpcResponse
            {
                Id = request.Id,
                Result = result
            };
        }
        catch (ArgumentException ex)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = ex.Message }
            };
        }
    }

    // Argha - 2026-02-24 - handle logging/setLevel: update the MCP log forwarding threshold
    private JsonRpcResponse HandleSetLevel(JsonRpcRequest request)
    {
        if (!request.Params.HasValue)
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = "Missing params" }
            };
        }

        var setParams = JsonSerializer.Deserialize<SetLevelParams>(
            request.Params.Value.GetRawText(), _jsonOptions);

        if (setParams == null || string.IsNullOrEmpty(setParams.Level))
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError { Code = JsonRpcErrorCodes.InvalidParams, Message = "Missing level parameter" }
            };
        }

        if (!Enum.TryParse<McpLogLevel>(setParams.Level, ignoreCase: true, out var level))
        {
            return new JsonRpcResponse
            {
                Id = request.Id,
                Error = new JsonRpcError
                {
                    Code = JsonRpcErrorCodes.InvalidParams,
                    Message = $"Invalid log level: '{setParams.Level}'. Valid values: debug, info, notice, warning, error, critical, alert, emergency"
                }
            };
        }

        _logSink.SetLevel(level);
        _logger.LogInformation("MCP log level set to {Level}", setParams.Level);

        return new JsonRpcResponse
        {
            Id = request.Id,
            Result = new { }
        };
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
