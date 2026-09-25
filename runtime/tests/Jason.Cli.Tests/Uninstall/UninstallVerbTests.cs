using Jason.Cli;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// The verb exists, it is declared where the rule is written, and — with no remover named — it refuses before
/// it removes anything at all.
/// </summary>
/// <remarks>
/// The refusal is not a nicety. Three guards in this repository type documented command lines <em>for real</em>,
/// and <c>CliEnvironment.InstallPath</c> left null means "ask the operating system", which under a test is the
/// test host executable. A verb that worked out for itself which file to delete would delete the test runner
/// the day a page printing it joined one of those guards.
/// </remarks>
public class UninstallVerbTests
{
    [Fact]
    public async Task With_no_remover_named_the_verb_refuses_and_removes_nothing()
    {
        using var dir = new TempPaths();
        var canary = Path.Combine(dir.Paths.Root, "canary.txt");
        File.WriteAllText(canary, "mine");

        var output = new StringWriter();

        var exit = await CliApp.RunAsync(
            ["uninstall"],
            new CliEnvironment(output, new StringWriter(), dir.Paths),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.RemoverUnsupported, output.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(canary), "the verb refused and something was removed anyway.");
    }

    /// <summary>
    /// And the refusal says what it left alone. "Nothing was removed" is only reassuring if the sentence names
    /// the thing a person is most afraid of losing.
    /// </summary>
    /// <remarks>
    /// On stderr for a person, where every other refusal and problem of this verb goes: the refusals that come
    /// before anything is read were the one kind printed on stdout.
    /// </remarks>
    [Fact]
    public async Task The_refusal_names_the_data_directory_it_did_not_touch()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(
            ["uninstall", "--human"],
            new CliEnvironment(output, error, dir.Paths),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("data directory", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(dir.Paths.Root, error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    /// <summary>
    /// The boundaries are in the help page, not only in a design note. A destructive verb whose reach a person
    /// has to read the source to learn is one they run without knowing it.
    /// </summary>
    [Theory]
    [InlineData("by receipt")]
    [InlineData("--purge-data")]
    [InlineData("Never touched")]
    public async Task The_help_page_publishes_what_it_removes_and_what_it_never_touches(string published)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(
            ["uninstall", "--help"],
            new CliEnvironment(output, new StringWriter(), dir.Paths),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains(published, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// And the real environment names a remover. Without this, the seam's fail-closed default would be the
    /// answer every operator got, and the verb would refuse on every machine while every test passed.
    /// </summary>
    [Fact]
    public void The_environment_this_program_really_runs_with_names_a_remover() =>
        Assert.NotNull(CliEnvironment.Default().Removes);
}
