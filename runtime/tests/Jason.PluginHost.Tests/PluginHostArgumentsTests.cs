namespace Jason.PluginHost.Tests;

public class PluginHostArgumentsTests
{
    private static readonly string[] Valid = ["--protocol", "1", "--plugin", "fake-provider", "--operation", "echo.run", "--correlation", "att_1"];

    [Fact]
    public void The_four_values_are_read_whatever_their_order()
    {
        var arguments = PluginHostArguments.Parse(["--operation", "echo.run", "--correlation", "att_1", "--plugin", "fake-provider", "--protocol", "1"]);

        Assert.Equal(1, arguments.Protocol);
        Assert.Equal("fake-provider", arguments.PluginId);
        Assert.Equal("echo.run", arguments.Operation);
        Assert.Equal("att_1", arguments.CorrelationId);
        Assert.Equal(arguments, PluginHostArguments.Parse(Valid));
    }

    [Theory]
    [InlineData("--protocol")]
    [InlineData("--plugin")]
    [InlineData("--operation")]
    [InlineData("--correlation")]
    public void Every_one_of_them_is_required(string missing)
    {
        var remaining = new List<string>();
        for (var index = 0; index < Valid.Length; index += 2)
        {
            if (Valid[index] != missing)
            {
                remaining.Add(Valid[index]);
                remaining.Add(Valid[index + 1]);
            }
        }

        Assert.Throws<HostUsageException>(() => PluginHostArguments.Parse(remaining));
    }

    [Fact]
    public void Nothing_else_is_accepted()
    {
        Assert.Throws<HostUsageException>(() => PluginHostArguments.Parse([]));
        Assert.Throws<HostUsageException>(() => PluginHostArguments.Parse([.. Valid, "--verbose"]));
        Assert.Throws<HostUsageException>(() => PluginHostArguments.Parse([.. Valid, "--plugin", "other"]));
        Assert.Throws<HostUsageException>(() => PluginHostArguments.Parse([.. Valid, "extra"]));
        Assert.Throws<HostUsageException>(() => PluginHostArguments.Parse(["--protocol", "one", "--plugin", "p", "--operation", "o", "--correlation", "c"]));
        Assert.Throws<HostUsageException>(() => PluginHostArguments.Parse(["--protocol", "1", "--plugin", "  ", "--operation", "o", "--correlation", "c"]));
        Assert.Throws<HostUsageException>(() => PluginHostArguments.Parse(["--protocol"]));
    }
}
