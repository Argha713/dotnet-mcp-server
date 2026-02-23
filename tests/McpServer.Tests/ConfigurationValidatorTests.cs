// Argha - 2026-02-23 - tests for the --validate health check runner
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace McpServer.Tests;

public class ConfigurationValidatorTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task EmptyConfig_NoConfiguredResources_ReturnsZero()
    {
        var config = BuildConfig(new());
        var result = await ConfigurationValidator.RunAsync(config);
        result.Should().Be(0);
    }

    [Fact]
    public async Task FileSystem_ExistingPath_ReturnsZero()
    {
        var config = BuildConfig(new() { ["FileSystem:AllowedPaths:0"] = Path.GetTempPath() });
        var result = await ConfigurationValidator.RunAsync(config);
        result.Should().Be(0);
    }

    [Fact]
    public async Task FileSystem_NonExistentPath_ReturnsOne()
    {
        var config = BuildConfig(new() { ["FileSystem:AllowedPaths:0"] = @"C:\this\path\does\not\exist\xyz_mcp_test_123" });
        var result = await ConfigurationValidator.RunAsync(config);
        result.Should().Be(1);
    }

    [Fact]
    public async Task FileSystem_MixedPaths_ReturnsOne()
    {
        var config = BuildConfig(new()
        {
            ["FileSystem:AllowedPaths:0"] = Path.GetTempPath(),
            ["FileSystem:AllowedPaths:1"] = @"C:\nonexistent\xyz_mcp_test_456"
        });
        var result = await ConfigurationValidator.RunAsync(config);
        result.Should().Be(1);
    }

    [Fact]
    public async Task Sql_EmptyConnectionString_ReturnsOne()
    {
        var config = BuildConfig(new()
        {
            ["Sql:Connections:MyDb:ConnectionString"] = "",
            ["Sql:Connections:MyDb:Description"] = "Test DB"
        });
        var result = await ConfigurationValidator.RunAsync(config);
        result.Should().Be(1);
    }

    [Fact]
    public async Task Http_UnresolvableHost_ReturnsOne()
    {
        // .invalid TLD is reserved by RFC 6761 — guaranteed never to resolve
        var config = BuildConfig(new() { ["Http:AllowedHosts:0"] = "host.that.will.never.resolve.invalid" });
        var result = await ConfigurationValidator.RunAsync(config);
        result.Should().Be(1);
    }

    [Fact]
    public async Task Http_Localhost_ReturnsZero()
    {
        var config = BuildConfig(new() { ["Http:AllowedHosts:0"] = "localhost" });
        var result = await ConfigurationValidator.RunAsync(config);
        result.Should().Be(0);
    }
}
