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
