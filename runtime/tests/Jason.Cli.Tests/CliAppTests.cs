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

    [Theory]
    [InlineData("runtime")]
    [InlineData("campaign")]
    [InlineData("contact")]
    [InlineData("journal")]
    [InlineData("suppression")]
    [InlineData("plugin")]
    [InlineData("update")]
    public async Task Help_lists_every_command_group(string group)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(group, output.ToString(), StringComparison.Ordinal);
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

    [Fact]
    public async Task A_reserved_actor_is_a_usage_error()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(["runtime", "status", "--actor", "system"], new CliEnvironment(output, error, dir.Paths), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--actor", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("Run 'jason --help' for usage.", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task The_actor_option_is_accepted_before_and_after_the_subcommand()
    {
        using var dir = new TempPaths();

        var before = await CliApp.RunAsync(["--actor", "role:planner", "runtime", "status"], new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);
        var after = await CliApp.RunAsync(["runtime", "status", "--actor", "human:ada"], new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.RuntimeUnavailable, before);
        Assert.Equal(ExitCodes.RuntimeUnavailable, after);
    }

    [Fact]
    public async Task An_unexpected_failure_prints_every_cause_rather_than_the_outermost_one()
    {
        // A type initializer that fails says only that it failed; what failed is in the exception it wraps. A
        // published build once answered an uninstall with exactly that one sentence after the uninstall had
        // already deleted things, and nothing on the screen said why.
        using var dir = new TempPaths();
        dir.WriteDescriptor(new("v1", "0.1.0-dev", "rt_LIVE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch));
        var error = new StringWriter();
        var handler = new FakeHandler(_ => throw new TypeInitializationException(
            "Some.Serializer",
            new InvalidOperationException("the outer cause", new FileNotFoundException("Could not load file or assembly 'Some.Assembly'."))));

        var exit = await CliApp.RunAsync(["campaign", "list"], new CliEnvironment(new StringWriter(), error, dir.Paths, handler), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.ApiError, exit);
        var said = error.ToString();
        Assert.Contains("The type initializer for 'Some.Serializer' threw an exception.", said, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException: the outer cause", said, StringComparison.Ordinal);
        Assert.Contains("FileNotFoundException: Could not load file or assembly 'Some.Assembly'.", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Help_describes_the_global_actor_option()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("--actor", output.ToString(), StringComparison.Ordinal);
    }
}
