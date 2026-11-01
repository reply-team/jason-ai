using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// The command line of a child says what is running and nothing more. Argv is readable by every process on the
/// machine, so the input, the binding and the grants travel on stdin instead — this is the whole of what a
/// process listing may reveal.
/// </summary>
public class PluginHostCommandTests
{
    [Fact]
    public void The_command_is_the_locator_then_the_mode_and_the_four_values()
    {
        var command = PluginHostCommand.Build(new CommandLocator("dotnet", "x/jason.dll"), "fake-provider", "echo.run", "att_1");

        Assert.Equal(
            ["dotnet", "x/jason.dll", "plugin-host", "--protocol", "1", "--plugin", "fake-provider", "--operation", "echo.run", "--correlation", "att_1"],
            command);
    }

    [Fact]
    public void Whatever_starts_the_host_keeps_its_own_leading_arguments()
    {
        var command = PluginHostCommand.Build(new CommandLocator("/usr/local/bin/jason"), "notifier", "message.send", "wi_9");

        Assert.Equal("/usr/local/bin/jason", command[0]);
        Assert.Equal("plugin-host", command[1]);
        Assert.Equal(9, command.Count - 1);
    }

    [Theory]
    [InlineData("", "echo.run", "att_1")]
    [InlineData("fake-provider", " ", "att_1")]
    [InlineData("fake-provider", "echo.run", "")]
    public void Every_one_of_the_four_values_is_required(string pluginId, string operation, string correlationId) =>
        Assert.Throws<ArgumentException>(() => PluginHostCommand.Build(new CommandLocator("jason"), pluginId, operation, correlationId));
}
