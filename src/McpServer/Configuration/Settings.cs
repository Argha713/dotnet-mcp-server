namespace McpServer.Configuration;

/// <summary>
/// Configuration for file system access
/// </summary>
public class FileSystemSettings
{
    public const string SectionName = "FileSystem";
    
    /// <summary>
    /// List of allowed directory paths
    /// </summary>
    public List<string> AllowedPaths { get; set; } = new();
}

/// <summary>
/// Configuration for SQL database connections
/// </summary>
public class SqlSettings
{
    public const string SectionName = "Sql";
    
    /// <summary>
    /// Named database connections
    /// </summary>
    public Dictionary<string, SqlConnectionConfig> Connections { get; set; } = new();
}

public class SqlConnectionConfig
{
    /// <summary>
    /// Connection string for the database
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    // Argha - 2026-02-25 - Phase 6.1: which DB engine to use; defaults to SqlServer for backwards compatibility
    /// <summary>
    /// Database provider: SqlServer | PostgreSQL | MySQL | SQLite (default: SqlServer)
    /// </summary>
    public string Provider { get; set; } = "SqlServer";

    /// <summary>
    /// Human-readable description
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Configuration for HTTP requests
/// </summary>
public class HttpSettings
{
    public const string SectionName = "Http";
    
    /// <summary>
    /// List of allowed host names for HTTP requests
    /// </summary>
    public List<string> AllowedHosts { get; set; } = new();
    
    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;
}

// Argha - 2026-02-18 - configuration for environment variable tool blocklist
public class EnvironmentSettings
{
    public const string SectionName = "Environment";

    /// <summary>
    /// Additional variable names to block beyond the hardcoded blocklist
    /// </summary>
    public List<string> AdditionalBlockedVariables { get; set; } = new();
}

// Argha - 2026-02-24 - configuration for the plugin loader
public class PluginsSettings
{
    public const string SectionName = "Plugins";

    /// <summary>
    /// Set to false to disable all plugin loading. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to the plugins directory. Relative paths are resolved from the config directory
    /// (%APPDATA%\dotnet-mcp-server on Windows, ~/.config/dotnet-mcp-server on Linux/macOS).
    /// Default: "plugins"
    /// </summary>
    public string Directory { get; set; } = "plugins";
}

// Argha - 2026-02-25 - Phase 6.3: per-tool sliding window rate limit settings
public class RateLimitSettings
{
    public const string SectionName = "RateLimit";

    /// <summary>
    /// Set to false to disable all rate limiting. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum calls per minute for any tool not listed in <see cref="ToolLimits"/>.
    /// Set to 0 for unlimited. Default: 60.
    /// </summary>
    public int DefaultLimitPerMinute { get; set; } = 60;

    /// <summary>
    /// Per-tool call limits (calls per minute). Overrides <see cref="DefaultLimitPerMinute"/>.
    /// Set a tool's value to 0 to make it unlimited regardless of the default.
    /// Example: { "sql_query": 20, "http_request": 30, "datetime": 0 }
    /// </summary>
    public Dictionary<string, int> ToolLimits { get; set; } = new();
}

// Argha - 2026-02-25 - Phase 6.2: audit log settings
public class AuditSettings
{
    public const string SectionName = "Audit";

    /// <summary>
    /// Set to false to disable all audit logging. Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Directory where audit-yyyy-MM-dd.jsonl files are written.
    /// An empty value is resolved at startup to {configDir}/audit/.
    /// </summary>
    public string LogDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Number of days to retain audit files. Files older than this are deleted on startup.
    /// Set to 0 to disable retention cleanup. Default: 30.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// When true, argument values whose keys look like credentials (password, token, etc.)
    /// are replaced with "[REDACTED]" before writing to disk. Default: true.
    /// </summary>
    public bool SanitizeArguments { get; set; } = true;
}

/// <summary>
/// General server configuration
/// </summary>
public class ServerSettings
{
    public const string SectionName = "Server";
    
    /// <summary>
    /// Server name shown to clients
    /// </summary>
    public string Name { get; set; } = "dotnet-mcp-server";
    
    /// <summary>
    /// Server version
    /// </summary>
    public string Version { get; set; } = "1.0.0";
}
