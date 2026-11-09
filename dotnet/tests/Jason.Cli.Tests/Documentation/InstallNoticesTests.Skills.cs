using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// The scripts' last act before the licence: the installed jason deploys the role skills, the half of the skills a
/// runtime cannot work without, with <c>jason skills install --roles-only</c>. The verb is sent only to a feed the script
/// read itself: none of its own, and the verb reads its default; a feed at an address, and the verb is given that
/// feed's manifest; a feed in a directory, which the verb does not read, and the verb is not run at all, and one line
/// says what to run. What the verb prints is shown, and a deployment that fails leaves Jason installed and says so in
/// one line, so that the licence and the command to run next stay the last two things said.
/// </summary>
/// <remarks>
/// The stand-in executable writes what it was asked, and the data directory it was handed, into the file
/// <see cref="StandInLog"/> names, and refuses with the exit code <see cref="StandInExit"/> names; it answers
/// <c>--version</c> as before.
/// </remarks>
public sealed partial class InstallNoticesTests
{
    /// <summary>The variable that names the file the stand-in writes each call into, one line per call.</summary>
    private const string StandInLog = "STANDIN_LOG";

    /// <summary>The variable that makes the stand-in refuse every call but <c>--version</c>, with that exit code.</summary>
    private const string StandInExit = "STANDIN_EXIT";

    /// <summary>
    /// The variable that makes the Windows stand-in, once it has answered <c>--version</c>, move its own file aside, as a
    /// scanner that quarantines a program does, so the next call cannot start it.
    /// </summary>
    private const string StandInVanish = "STANDIN_VANISH";

    /// <summary>What the stand-in says on stdout for a call it does not refuse.</summary>
    private const string StandInDeployed = "The stand-in deployed the role skills.";

    private const string DirectoryLine =
        "The role skills were not deployed: the feed is a directory, which `jason skills install` does not read. Run `jason skills install --roles-only` now that Jason is installed, offline with `--source <checkout>`.";

    private const string FailedLine =
        "The role skills were not deployed; the lines above say why. Run `jason skills install --roles-only` once that is dealt with; `jason status` names it as well.";

    /// <summary>A feed in a directory is never handed to the verb: the verb is not run, and the line says what to run.</summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    [Critical(Family.I1, "the installer runs the skills verb only on a feed it read itself")]
    public void A_feed_in_a_directory_runs_no_skills_verb_and_says_what_to_run(string script)
    {
        var installer = Installer.For(script, _tree);
        var log = Path.Combine(_tree.NewDirectory("standin"), "calls.txt");
        installer.Variables[StandInLog] = log;

        var run = installer.Run(installer.Feed("0.33.0"));

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.False(File.Exists(log), $"{script} ran the installed jason with a directory feed: {(File.Exists(log) ? File.ReadAllText(log) : string.Empty)}");
        var lines = Lines(run.Stdout);
        Assert.Equal(DirectoryLine, lines[^3]);
        AssertEndsWith(MitLine("0.33.0"), lines);
    }

    /// <summary>
    /// A feed at an address is handed to the verb as its manifest's address, with the data directory the script was
    /// given, and what the verb says is shown before the licence.
    /// </summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void A_feed_at_an_address_is_handed_to_the_skills_verb_as_its_manifest(string script)
    {
        var installer = Installer.For(script, _tree);
        using var address = new LoopbackReleases(installer.Feed("0.33.0"));
        var log = Path.Combine(_tree.NewDirectory("standin"), "calls.txt");
        var data = _tree.NewDirectory("data");
        installer.Variables[StandInLog] = log;
        installer.Variables["JASON_DATA_DIR"] = data;
        var feed = $"{address.Releases}/latest/download";

        var run = installer.Run(feed + "/");

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.True(File.Exists(log), $"{script} did not run the installed jason. It said: {run.Stdout}");
        Assert.Equal([$"skills install --roles-only --feed {feed}/{ReleaseAssets.Manifest}|{data}"], File.ReadAllLines(log));
        var lines = Lines(run.Stdout);
        Assert.Equal(StandInDeployed, lines[^3]);
        Assert.DoesNotContain(FailedLine, lines);
        AssertEndsWith(MitLine("0.33.0"), lines);
    }

    /// <summary>A deployment that fails leaves Jason installed and the run successful, and the one line says what to do.</summary>
    [Theory]
    [InlineData(Sh)]
    [InlineData(Ps1)]
    public void A_skills_deployment_that_fails_leaves_jason_installed_and_says_so(string script)
    {
        var installer = Installer.For(script, _tree);
        using var address = new LoopbackReleases(installer.Feed("0.33.0"));
        installer.Variables[StandInLog] = Path.Combine(_tree.NewDirectory("standin"), "calls.txt");
        installer.Variables[StandInExit] = "1";

        var run = installer.Run($"{address.Releases}/latest/download");

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.True(File.Exists(installer.Target));
        Assert.Contains("The stand-in refused.", run.Stderr, StringComparison.Ordinal);
        var lines = Lines(run.Stdout);
        Assert.Equal(FailedLine, lines[^3]);
        AssertEndsWith(MitLine("0.33.0"), lines);
    }

    /// <summary>
    /// The same under the Windows PowerShell a fresh Windows has, which stops on a native command's stderr read through
    /// its error stream: the verb's refusal goes straight through, and the run still ends with the licence.
    /// </summary>
    [Fact]
    public void A_skills_deployment_that_fails_under_windows_powershell_is_said_the_same_way()
    {
        var installer = Installer.For(Ps1, _tree);
        using var address = new LoopbackReleases(installer.Feed("0.33.0"));
        installer.Variables[StandInLog] = Path.Combine(_tree.NewDirectory("standin"), "calls.txt");
        installer.Variables[StandInExit] = "1";

        var run = installer.RunUnderWindowsPowerShell($"{address.Releases}/latest/download");

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.Contains("The stand-in refused.", run.Stderr, StringComparison.Ordinal);
        var lines = Lines(run.Stdout);
        Assert.Equal(FailedLine, lines[^3]);
        AssertEndsWith(MitLine("0.33.0"), lines);
    }

    /// <summary>
    /// An executable that cannot be started when the verb is run — moved aside since the script asked its version, as a
    /// scanner moves one — has no line of its own to show: what PowerShell answered is said in its place, then the line,
    /// and the run still ends with the licence and exits 0.
    /// </summary>
    [Fact]
    public void A_skills_verb_that_cannot_start_is_said_and_the_run_still_ends_with_the_licence()
    {
        var installer = Installer.For(Ps1, _tree);
        using var address = new LoopbackReleases(installer.Feed("0.33.0"));
        var feed = $"{address.Releases}/latest/download";
        var first = installer.Run(feed);
        Assert.True(first.Exit == 0, first.Stderr);
        installer.Variables[StandInVanish] = "1";

        var run = installer.Run(feed);

        Assert.True(run.Exit == 0, run.Stderr);
        Assert.False(File.Exists(installer.Target));
        var lines = Lines(run.Stdout);
        var installed = lines.FindIndex(line => line.StartsWith("Jason 0.33.0 is already installed at ", StringComparison.Ordinal));
        Assert.True(installed >= 0, run.Stdout);
        Assert.Contains(lines.Skip(installed + 1).SkipLast(3), line => line.Contains(installer.Target, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(FailedLine, lines[^3]);
        AssertEndsWith(MitLine("0.33.0"), lines);
    }

    /// <summary>
    /// With no feed of its own, the verb is run with no <c>--feed</c>, so it reads its default. A run with no feed reaches
    /// the published releases, which no test does, so this row reads the text: the no-feed case hands the verb nothing.
    /// </summary>
    [Fact]
    public void With_no_feed_the_skills_verb_is_run_with_none()
    {
        var sh = Code(Sh);
        Assert.Contains("    '') deploy_role_skills ;;", sh, StringComparison.Ordinal);
        Assert.Contains("deploy_role_skills --feed \"${FEED%/}/manifest.json\"", sh, StringComparison.Ordinal);

        var ps1 = Code(Ps1);
        Assert.Contains("$arguments = @('skills', 'install', '--roles-only')", ps1, StringComparison.Ordinal);
        Assert.Contains("if ($Feed) { $arguments += @('--feed', ($Feed.TrimEnd('/') + '/manifest.json')) }", ps1, StringComparison.Ordinal);
    }

    /// <summary>
    /// Each install job of CI proves both halves on a real executable: its feed holds the skills' package as a release's
    /// does, the run from the directory says the directory line, and one more run from the same feed served on this
    /// machine deploys the role skills, whose record names that feed, and a runtime started over them says
    /// <c>role_skills</c> is ok. The rows above run a stand-in; only these legs run the jason the job built.
    /// </summary>
    [Theory]
    [InlineData("install-sh", "--feed http://127.0.0.1:8099 ")]
    [InlineData("install-ps1", "-Feed http://127.0.0.1:8099 ")]
    public void Ci_proves_the_directory_line_and_a_feed_served_on_the_machine(string job, string served)
    {
        var ci = File.ReadAllText(Path.Combine(RepositoryRoot(), ".github", "workflows", "ci.yml")).ReplaceLineEndings("\n");
        var start = ci.IndexOf($"\n  {job}:\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"ci.yml no longer has the {job} job.");
        var end = ci.IndexOf("\n  ", start + 1, StringComparison.Ordinal);
        while (end >= 0 && ci[end + 3] == ' ')
        {
            end = ci.IndexOf("\n  ", end + 1, StringComparison.Ordinal);
        }

        var text = end < 0 ? ci[start..] : ci[start..end];
        foreach (var said in new[]
        {
            $"Write-SkillsArchive.ps1 -Tree . -Archive feed/{ReleaseAssets.Skills}",
            $"-Plugins {ReleaseAssets.Skills}",
            DirectoryLine,
            "http.server 8099 --bind 127.0.0.1 --directory feed",
            served,
            "skills/roles/.jason-skills.json".Replace("/", job == "install-ps1" ? "\\" : "/", StringComparison.Ordinal),
            "http://127.0.0.1:8099/manifest.json",
            "\"name\":\"role_skills\",\"required\":true,\"state\":\"ok\"",
        })
        {
            Assert.True(
                text.Contains(said, StringComparison.Ordinal) || text.Contains(said.Replace(" ", "', '", StringComparison.Ordinal), StringComparison.Ordinal),
                $"The {job} job of ci.yml does not say: {said}");
        }
    }

    /// <summary>
    /// The step comes after the PATH step and before the licence in both scripts. Windows' PATH step is never run here, so
    /// that half is the order in the text, as the licence line's is.
    /// </summary>
    [Theory]
    [InlineData(Sh, "Added $INSTALL_DIR to the PATH in ", "skills install --roles-only")]
    [InlineData(Ps1, "is on your PATH; open a new terminal", "'skills', 'install', '--roles-only'")]
    public void The_skills_step_comes_after_the_path_step_and_before_the_licence(string script, string pathStep, string verb)
    {
        var code = Code(script);
        var path = code.IndexOf(pathStep, StringComparison.Ordinal);
        var skills = code.IndexOf(verb, StringComparison.Ordinal);
        var directory = code.IndexOf(DirectoryLine, StringComparison.Ordinal);

        Assert.True(path >= 0 && skills > path && directory > path, $"{script} does not deploy the role skills after the PATH step.");
        foreach (var said in Licences)
        {
            var licence = code.IndexOf(said, StringComparison.Ordinal);
            Assert.True(licence > skills && licence > directory, $"{script} says '{said}' before the role skills step.");
        }
    }
}
