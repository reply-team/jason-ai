using Jason.Cli;
using Jason.Cli.Uninstall;

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
    [InlineData("another account's logon registration")]
    [InlineData("including the task an earlier version registered as Jason")]
    [InlineData("A file another program holds open is tried again for up to 10 seconds before it is reported;")]
    [InlineData("elsewhere than Windows an open file does not stop the removal.")]
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
    /// The steps name the notice files the installer keeps beside the executable: step 5's rule counts them as Jason's,
    /// and step 6 removes them. Read with the help page's line breaks made spaces, since a step wraps.
    /// </summary>
    [Theory]
    [InlineData("5. the PATH entry, only where the installer wrote it and its directory holds nothing but Jason — the executable, its two notice files and what an upgrade left beside it:")]
    [InlineData("6. the executable, its notice files (jason-LICENSE, jason-THIRD-PARTY-NOTICES), its install directory, the native libraries it unpacked on first run, and on Windows those every other build of this program unpacked beside them")]
    public async Task The_help_page_names_the_notice_files_in_the_steps_that_remove_them(string published)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["uninstall", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        Assert.Contains(published, System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " "), StringComparison.Ordinal);
    }

    /// <summary>
    /// The harness root is the person's — their memory files and every project's and campaign's folder — so the help page
    /// names it among what is never touched, and says that the purge is no exception. Read with the line breaks made
    /// spaces, since the sentence wraps.
    /// </summary>
    [Fact]
    public async Task The_help_page_says_the_harness_root_is_never_touched_the_purge_included()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["uninstall", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        var help = System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " ");
        var never = help.IndexOf("Never touched:", StringComparison.Ordinal);
        var root = help.IndexOf(
            "And the harness root (Harness:Root, ~/Jason by default) with every folder and file in it, --purge-data included: the purge empties the data directory, never the root.",
            StringComparison.Ordinal);
        Assert.True(never >= 0 && root > never, $"The help page does not name the harness root among what is never touched:{Environment.NewLine}{output}");

        // And a data directory that is one of its folders is refused, as the purge's belt refuses it.
        Assert.Contains(
            "is refused, and so is a folder of the harness root or one inside it, and one holding such a folder directly in it or anywhere in a folder the purge would delete, each known by its .jason/marker.json, or holding a folder there that cannot be looked into.",
            help,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A link in the data directory goes as the link it is, the purge included, and what it points to is never touched:
    /// the help page says so among what is never touched. Read with the line breaks made spaces, since the sentence wraps.
    /// </summary>
    [Fact]
    public async Task The_help_page_says_a_link_in_the_data_directory_goes_as_itself()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["uninstall", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        var help = System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " ");
        var never = help.IndexOf("Never touched:", StringComparison.Ordinal);
        var link = help.IndexOf("A link in the data directory is removed as itself: what it points to is never touched.", StringComparison.Ordinal);
        Assert.True(never >= 0 && link > never, $"The help page does not say a link in the data directory goes as itself among what is never touched:{Environment.NewLine}{output}");
    }

    /// <summary>
    /// The agent hosts keep their own transcripts of the desktop's chat, in folders of their own: the help page names
    /// them among what is never touched, the purge included. Read with the line breaks made spaces.
    /// </summary>
    [Fact]
    public async Task The_help_page_says_the_hosts_transcripts_of_the_desktop_s_chat_are_never_touched()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["uninstall", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        var help = System.Text.RegularExpressions.Regex.Replace(output.ToString(), @"\s+", " ");
        var never = help.IndexOf("Never touched:", StringComparison.Ordinal);
        var root = help.IndexOf("And the harness root", StringComparison.Ordinal);
        var transcripts = help.IndexOf(
            "the transcripts the agent hosts keep of the desktop's chat, which are the hosts' own and stay where each host keeps them,",
            StringComparison.Ordinal);
        Assert.True(never >= 0 && transcripts > never && transcripts < root, $"The help page does not name the hosts' transcripts among what is never touched:{Environment.NewLine}{output}");
    }

    /// <summary>The wait the help page publishes is the remover's own, not a number written down beside it.</summary>
    [Fact]
    public async Task The_wait_the_help_page_publishes_is_the_removers_rule()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        await CliApp.RunAsync(["uninstall", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        Assert.Contains($"tried again for up to {InstallationRemovers.HeldFiles.WaitInWords} before it is reported", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// And the real environment names a remover. Without this, the seam's fail-closed default would be the
    /// answer every operator got, and the verb would refuse on every machine while every test passed.
    /// </summary>
    [Fact]
    public void The_environment_this_program_really_runs_with_names_a_remover() =>
        Assert.NotNull(CliEnvironment.Default().Removes);

    /// <summary>
    /// And a terminal starter, and on Windows a clipboard: without them a session link and the toast's Copy prompt button
    /// would refuse on every machine while every test passed. Naming them starts nothing and writes nothing.
    /// </summary>
    [Fact]
    public void The_environment_this_program_really_runs_with_names_a_terminal_starter_and_on_windows_a_clipboard()
    {
        var real = CliEnvironment.Default();

        Assert.NotNull(real.Terminals);
        if (OperatingSystem.IsWindows())
        {
            Assert.NotNull(real.Clipboard);
        }
    }
}
