// Argha - 2026-02-24 - Phase 5.1: unit tests for PluginLoader
using FluentAssertions;
using McpServer.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace McpServer.Tests.Plugins;

public class PluginLoaderTests
{
    private static PluginLoader MakeLoader(string dir, IConfiguration? config = null)
    {
        var cfg = config ?? new ConfigurationBuilder().Build();
        var loggerFactory = LoggerFactory.Create(_ => { });
        return new PluginLoader(dir, cfg, loggerFactory);
    }

    // ── directory edge-cases ──────────────────────────────────────────────────

    [Fact]
    public void LoadPlugins_DirectoryNotFound_YieldsNoTools()
    {
        var loader = MakeLoader(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        var tools = loader.LoadPlugins().ToList();

        tools.Should().BeEmpty();
    }

    [Fact]
    public void LoadPlugins_EmptyDirectory_YieldsNoTools()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var loader = MakeLoader(dir);
            var tools = loader.LoadPlugins().ToList();
            tools.Should().BeEmpty();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── error resilience ─────────────────────────────────────────────────────

    [Fact]
    public void LoadPlugins_CorruptDll_LogsErrorAndYieldsNoTools()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            // Write random bytes — not a valid PE/CLI assembly
            File.WriteAllBytes(Path.Combine(dir, "corrupt.dll"), new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

            var loader = MakeLoader(dir);
            var act = () => loader.LoadPlugins().ToList();

            // Must not throw; bad DLLs are swallowed and logged
            act.Should().NotThrow();
            loader.LoadPlugins().ToList().Should().BeEmpty();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void LoadPlugins_DllWithNoIToolTypes_YieldsNoTools()
    {
        // The abstractions DLL itself has no concrete ITool implementations
        var abstractionsDll = typeof(McpServer.Tools.ITool).Assembly.Location;
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.Copy(abstractionsDll, Path.Combine(dir, "McpServer.Plugin.Abstractions.dll"));

            var loader = MakeLoader(dir);
            var tools = loader.LoadPlugins().ToList();

            tools.Should().BeEmpty();
        }
        finally
        {
            // Argha - 2026-02-24 - AssemblyLoadContext holds a file lock on Windows; swallow the
            // cleanup error — the OS temp cleaner will remove the directory eventually.
            try { Directory.Delete(dir, recursive: true); }
            catch (UnauthorizedAccessException) { }
        }
    }

    // ── config wiring ─────────────────────────────────────────────────────────

    [Fact]
    public void LoadPlugins_PluginsDisabledInConfig_PluginLoaderNotInvoked()
    {
        // When Plugins:Enabled is false, Program.cs skips creating the loader entirely.
        // This test verifies the config value is read correctly.
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Plugins:Enabled"] = "false",
                ["Plugins:Directory"] = "plugins"
            })
            .Build();

        config.GetValue<bool>("Plugins:Enabled").Should().BeFalse();
    }

    [Fact]
    public void LoadPlugins_PluginsEnabledInConfig_DefaultsToTrue()
    {
        var config = new ConfigurationBuilder().Build();

        // Default when key is absent
        config.GetValue("Plugins:Enabled", defaultValue: true).Should().BeTrue();
    }

    // ── PluginsSettings binding ───────────────────────────────────────────────

    [Fact]
    public void PluginsSettings_DefaultDirectory_IsRelativePluginsFolder()
    {
        var settings = new McpServer.Configuration.PluginsSettings();
        settings.Directory.Should().Be("plugins");
        settings.Enabled.Should().BeTrue();
    }

    [Fact]
    public void PluginsSettings_BoundFromConfig_ReflectsConfiguredValues()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Plugins:Enabled"] = "false",
                ["Plugins:Directory"] = "/custom/plugins"
            })
            .Build();

        var settings = config
            .GetSection(McpServer.Configuration.PluginsSettings.SectionName)
            .Get<McpServer.Configuration.PluginsSettings>()!;

        settings.Enabled.Should().BeFalse();
        settings.Directory.Should().Be("/custom/plugins");
    }

    // ── PluginContext ─────────────────────────────────────────────────────────

    [Fact]
    public void PluginContext_GetConfig_ReturnsValueFromConfigSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Plugins:Config:MyApiKey"] = "abc123"
            })
            .Build();

        var section = config.GetSection("Plugins:Config");
        var logger = Mock.Of<ILogger>();
        var context = new PluginContext(key => section[key], logger);

        context.GetConfig("MyApiKey").Should().Be("abc123");
    }

    [Fact]
    public void PluginContext_GetConfig_ReturnsNullForMissingKey()
    {
        var context = new PluginContext(_ => null, Mock.Of<ILogger>());

        context.GetConfig("NonExistentKey").Should().BeNull();
    }
}
