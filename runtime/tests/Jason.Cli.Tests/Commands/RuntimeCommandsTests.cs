using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Commands;

/// <summary>The <c>runtime</c> group as the root command exposes it: the verbs exist and refuse a system actor.</summary>
public class RuntimeCommandsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("status")]
    [InlineData("stop")]
    [InlineData("start")]
    [InlineData("restart")]
    public async Task Every_runtime_verb_documents_itself(string verb)
    {
        using var dir = new TempPaths();
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["runtime", verb, "--help"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(verb, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The autostart verbs are runtime verbs too: they document themselves, and they refuse the runtime's own
    /// actor like every other. The machine behind them here is a recorder, so nothing is registered by a test
    /// about a help page.
    /// </summary>
    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("status")]
    public async Task Every_autostart_verb_documents_itself(string verb)
    {
        using var dir = new TempPaths();
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["runtime", "autostart", verb, "--help"], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(verb, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("logon", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    [InlineData("status")]
    public async Task The_runtime_s_own_actor_cannot_be_claimed_on_an_autostart_verb(string verb)
    {
        using var dir = new TempPaths();
        var (env, _, error) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await CliApp.RunAsync(["--actor", "system", "runtime", "autostart", verb], env, Ct);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--actor", error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("start")]
    [InlineData("restart")]
    public async Task The_runtime_s_own_actor_cannot_be_claimed_on_any_verb(string verb)
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("a refused claim must never reach the runtime") };
        var (env, _, error) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), processes);

        var exit = await CliApp.RunAsync(["--actor", "system", "runtime", verb], env, Ct);

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--actor", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(processes.Launches);
    }
}
