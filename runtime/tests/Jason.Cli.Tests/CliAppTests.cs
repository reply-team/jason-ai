using Jason.Cli;

namespace Jason.Cli.Tests;

public class CliAppTests
{
    [Fact]
    public async Task Unknown_command_is_a_usage_error()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(["bogus"], new CliEnvironment(output, error, dir.Paths), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("bogus", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task No_arguments_is_a_usage_error()
    {
        using var dir = new TempPaths();
        var exit = await CliApp.RunAsync([], new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);
        Assert.Equal(ExitCodes.Usage, exit);
    }

    [Fact]
    public async Task Help_succeeds_and_lists_the_runtime_command()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("runtime", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runtime_status_is_routed_to_the_command()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["runtime", "status"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains("\"code\":\"no_descriptor\"", output.ToString(), StringComparison.Ordinal);
    }
}
