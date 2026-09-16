using Jason.App;

namespace Jason.App.Tests;

public class ModeRouterTests
{
    [Theory]
    [InlineData(new[] { "--version" }, Mode.Version)]
    [InlineData(new[] { "runtime", "run" }, Mode.RuntimeService)]
    [InlineData(new[] { "runtime", "run", "--whatever" }, Mode.RuntimeService)]
    [InlineData(new[] { "runtime", "run", "--detached" }, Mode.RuntimeService)]
    [InlineData(new[] { "plugin-host" }, Mode.PluginHost)]
    [InlineData(new[] { "plugin-host", "fake-provider" }, Mode.PluginHost)]
    [InlineData(new[] { "runtime", "status" }, Mode.Cli)]
    [InlineData(new[] { "--help" }, Mode.Cli)]
    [InlineData(new string[0], Mode.Cli)]
    [InlineData(new[] { "campaign", "create" }, Mode.Cli)]
    public void Selects_the_mode_from_the_leading_arguments(string[] args, Mode expected) =>
        Assert.Equal(expected, ModeRouter.Select(args));

    [Fact]
    public async Task Version_prints_the_build_version_and_exits_0()
    {
        var original = Console.Out;
        var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var exit = await ModeRouter.RunAsync(["--version"], TestContext.Current.CancellationToken);
            Assert.Equal(0, exit);
            Assert.StartsWith("0.1.0", output.ToString().Trim(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task Plugin_host_without_arguments_is_a_usage_error()
    {
        var original = Console.Error;
        var error = new StringWriter();
        Console.SetError(error);
        try
        {
            var exit = await ModeRouter.RunAsync(["plugin-host"], TestContext.Current.CancellationToken);
            Assert.Equal(2, exit);
            Assert.Contains("usage", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
