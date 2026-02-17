using FluentAssertions;
using McpServer.Tools;
using Xunit;

namespace McpServer.Tests;

public class DateTimeToolTests
{
    private readonly DateTimeTool _tool;

    public DateTimeToolTests()
    {
        _tool = new DateTimeTool();
    }

    [Fact]
    public void Name_ShouldBeDatetime()
    {
        _tool.Name.Should().Be("datetime");
    }

    [Fact]
    public void Description_ShouldNotBeEmpty()
    {
        _tool.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void InputSchema_ShouldHaveActionProperty()
    {
        _tool.InputSchema.Properties.Should().ContainKey("action");
    }

    [Fact]
    public async Task ExecuteAsync_Now_ShouldReturnCurrentTime()
    {
        // Arrange
        var arguments = new Dictionary<string, object>
        {
            ["action"] = "now"
        };

        // Act
        var result = await _tool.ExecuteAsync(arguments);

        // Assert
        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCount(1);
        result.Content[0].Text.Should().Contain("UTC");
    }

    [Fact]
    public async Task ExecuteAsync_NowWithTimezone_ShouldReturnLocalTime()
    {
        // Arrange
        var arguments = new Dictionary<string, object>
        {
            ["action"] = "now",
            ["timezone"] = "UTC"
        };

        // Act
        var result = await _tool.ExecuteAsync(arguments);

        // Assert
        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("UTC");
    }

    [Fact]
    public async Task ExecuteAsync_ListTimezones_ShouldReturnList()
    {
        // Arrange
        var arguments = new Dictionary<string, object>
        {
            ["action"] = "list_timezones"
        };

        // Act
        var result = await _tool.ExecuteAsync(arguments);

        // Assert
        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("America/New_York");
        result.Content[0].Text.Should().Contain("Asia/Kolkata");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidAction_ShouldReturnError()
    {
        // Arrange
        var arguments = new Dictionary<string, object>
        {
            ["action"] = "invalid_action"
        };

        // Act
        var result = await _tool.ExecuteAsync(arguments);

        // Assert
        result.Content[0].Text.Should().Contain("Unknown action");
    }
}

public class ToolInterfaceTests
{
    [Fact]
    public void AllTools_ShouldHaveRequiredProperties()
    {
        // Arrange
        var tools = new ITool[]
        {
            new DateTimeTool()
        };

        // Assert
        foreach (var tool in tools)
        {
            tool.Name.Should().NotBeNullOrWhiteSpace();
            tool.Description.Should().NotBeNullOrWhiteSpace();
            tool.InputSchema.Should().NotBeNull();
            tool.InputSchema.Type.Should().Be("object");
        }
    }
}
