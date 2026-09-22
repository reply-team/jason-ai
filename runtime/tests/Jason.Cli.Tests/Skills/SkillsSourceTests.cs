using Jason.Cli;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// Where the packs come from: a directory on this machine, or a git ref fetched into the data directory.
/// </summary>
/// <remarks>
/// The git half is proved against a repository this test makes and clones over <c>file://</c>. What is held is
/// that the clone runs at the ref it was asked for and that the commit is read back — not that github.com is
/// reachable, which no test can prove and none here tries to.
/// </remarks>
public class SkillsSourceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The pin is derived from this build rather than written down, through the same type that decides a
    /// development build is older than the release it is on the way to.
    /// </summary>
    [Theory]
    [InlineData("0.1.0", "v0.1.0")]
    [InlineData("0.1.0-dev", "v0.1.0")]
    [InlineData("1.4.2", "v1.4.2")]
    [InlineData("2.0.0-rc.1+build.7", "v2.0.0")]
    public void The_default_ref_is_this_builds_own_release_version(string version, string expected) =>
        Assert.Equal(expected, SkillsSource.DefaultRef(version));

    /// <summary>A build that cannot read its own version has no pin, and says so rather than guessing one.</summary>
    [Fact]
    public void A_build_whose_version_will_not_parse_carries_no_pin()
    {
        var refusal = Assert.Throws<SkillsSourceUnavailable>(() => SkillsSource.DefaultRef("not-a-version"));

        Assert.Contains("--ref", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--source", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A directory on this machine is read where it stands: nothing is copied and nothing is fetched.</summary>
    [Fact]
    public async Task A_local_directory_is_staged_where_it_stands()
    {
        using var dir = new TempPaths();
        var clone = Pack(dir, "somewhere");

        var staged = await SkillsSource.StageAsync(Machine(dir), clone, null, dryRun: false, Ct);

        Assert.Equal(clone, staged.Directory);
        Assert.Null(staged.Commit);
        Assert.False(staged.RefOverridden);
    }

    /// <summary>And a source that holds no packs is refused before anything downstream looks at it.</summary>
    [Fact]
    public async Task A_directory_that_holds_no_packs_is_refused()
    {
        using var dir = new TempPaths();
        var empty = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "empty")).FullName;

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir), empty, null, dryRun: false, Ct));

        Assert.Contains("skills", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A ref is fetched, at the ref it was asked for, from a repository with no network behind it.</summary>
    [Fact]
    public async Task A_git_source_is_cloned_at_the_ref_that_was_asked_for()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v0.1.0");

        var staged = await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", dryRun: false, Ct);

        Assert.True(File.Exists(Path.Combine(staged.Directory, "skills", "README.md")));
        Assert.Equal("v0.1.0", staged.Ref);
        Assert.True(staged.RefOverridden);
        Assert.NotNull(staged.Commit);
        Assert.Equal(40, staged.Commit.Length);
        Assert.StartsWith(dir.Paths.Root, staged.Directory, StringComparison.Ordinal);
    }

    /// <summary>A ref that is not in the source is refused in words an operator can act on.</summary>
    [Fact]
    public async Task A_ref_that_is_not_there_is_refused_and_names_it()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v0.1.0");

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v9.9.9", dryRun: false, Ct));

        Assert.Contains("v9.9.9", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ref that is a way out of the staging directory is refused, and nothing is deleted on the way to
    /// finding that out.
    /// </summary>
    /// <remarks>
    /// This staged into a directory named after the ref and deleted that directory before unpacking. The
    /// filter that made the name kept dots, so <c>..</c> survived it whole, the path resolved to the
    /// directory holding every deployed skill, and one command removed all of them — before any validation,
    /// in the verb whose contract is that nothing is written until the whole tree is deliverable.
    /// </remarks>
    [Theory]
    [InlineData("..")]
    [InlineData("../..")]
    [InlineData(".")]
    public async Task A_ref_that_climbs_out_of_the_staging_directory_is_refused_and_deletes_nothing(string reference)
    {
        using var dir = new TempPaths();
        var deployed = Directory.CreateDirectory(Path.Combine(dir.Paths.RoleSkillsDirectory, "researcher")).FullName;
        await File.WriteAllTextAsync(Path.Combine(deployed, "SKILL.md"), "---\nname: researcher\n---\n\nbody\n", Ct);
        var record = Path.Combine(dir.Paths.Root, "skills", ".jason-skills.json");
        await File.WriteAllTextAsync(record, """{"version":1,"packs":[]}""", Ct);

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir), "https://example.test/x.git", reference, dryRun: false, Ct));

        Assert.Contains(reference, refusal.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(deployed, "SKILL.md")), "The deployed skill was deleted by a refused ref.");
        Assert.True(File.Exists(record), "The deployment record was deleted by a refused ref.");
    }

    /// <summary>And the whole verb answers the same way, since that is where a person meets it.</summary>
    [Fact]
    public async Task The_install_verb_refuses_a_climbing_ref_and_leaves_every_deployed_skill_where_it_is()
    {
        using var dir = new TempPaths();
        var deployed = Directory.CreateDirectory(Path.Combine(dir.Paths.RoleSkillsDirectory, "researcher")).FullName;
        await File.WriteAllTextAsync(Path.Combine(deployed, "SKILL.md"), "---\nname: researcher\n---\n\nbody\n", Ct);

        var error = new StringWriter();
        var exit = await CliApp.RunAsync(
            ["skills", "install", "--ref", "..", "--root", Path.Combine(dir.Paths.Root, "harness")],
            new CliEnvironment(new StringWriter(), error, dir.Paths, Programs: new FakeProgramRunner()),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(Path.Combine(deployed, "SKILL.md")), "The deployed skill was deleted by 'jason skills install --ref ..'.");
    }

    /// <summary>
    /// A dry run does not fetch. The flag's own help says it changes nothing, and a clone into the data
    /// directory is a change — this deleted whatever was staged at that ref first, so the safe-looking first
    /// command the README prints was a network fetch that also destroyed a cache.
    /// </summary>
    [Fact]
    public async Task A_dry_run_against_a_source_that_is_not_here_yet_refuses_rather_than_fetching()
    {
        using var dir = new TempPaths();
        var runner = new FakeProgramRunner();

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir, runner), "https://example.test/x.git", "v0.1.0", dryRun: true, Ct));

        Assert.Contains("--dry-run", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--source", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(runner.Requested);
    }

    /// <summary>And a tree already fetched at that ref is used as it is, by a dry run and by an install.</summary>
    [Fact]
    public async Task A_ref_already_fetched_is_used_as_it_is_rather_than_fetched_again()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v0.1.0");
        var first = await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", dryRun: false, Ct);
        var marker = Path.Combine(first.Directory, "skills", "README.md");
        await File.WriteAllTextAsync(marker, "the tree that was already here", Ct);

        var again = await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", dryRun: true, Ct);

        Assert.Equal(first.Directory, again.Directory);
        Assert.Equal("the tree that was already here", await File.ReadAllTextAsync(marker, Ct));
    }

    /// <summary>An environment with no program runner refuses rather than reaching for the network.</summary>
    [Fact]
    public async Task Without_a_program_runner_a_git_source_is_refused()
    {
        using var dir = new TempPaths();
        var machine = new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths);

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(machine, "https://example.test/x.git", "v0.1.0", dryRun: false, Ct));

        Assert.Contains("cannot run git", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>And a runner that runs nothing is the same answer: it is refused, never silently skipped.</summary>
    [Fact]
    public async Task A_clone_that_fails_is_refused_rather_than_reported_as_staged()
    {
        using var dir = new TempPaths();
        var machine = Machine(dir, new FakeProgramRunner(_ => new ProgramResult(128, string.Empty, "fatal: could not read from remote", false)));

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(machine, "https://example.test/x.git", "v0.1.0", dryRun: false, Ct));

        Assert.Contains("could not read from remote", refusal.Message, StringComparison.Ordinal);
    }

    private static CliEnvironment Machine(TempPaths dir, IProgramRunner? runner = null) =>
        new(new StringWriter(), new StringWriter(), dir.Paths, Programs: runner ?? ProgramRunners.ForThisMachine());

    /// <summary>A directory shaped the way this repository is: a skills tree with a pack in it.</summary>
    private static string Pack(TempPaths dir, string name)
    {
        var root = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, name)).FullName;
        var runtime = Directory.CreateDirectory(Path.Combine(root, "skills", "runtime", "operating")).FullName;
        File.WriteAllText(Path.Combine(root, "skills", "README.md"), "# Skills\n");
        File.WriteAllText(Path.Combine(runtime, "SKILL.md"), "---\nname: operating\ndescription: one line\n---\n\nbody\n");
        return root;
    }

    /// <summary>The same tree, in a real git repository with a real tag, and no network anywhere near it.</summary>
    private static async Task<string> RepositoryAsync(TempPaths dir, string tag)
    {
        var root = Pack(dir, "origin");
        var git = ProgramRunners.ForThisMachine();

        await RunAsync(git, root, ["init", "-b", "main"]);
        await RunAsync(git, root, ["add", "-A"]);
        await RunAsync(git, root, ["-c", "user.name=Test", "-c", "user.email=test@example.test", "commit", "-m", "the pack"]);
        await RunAsync(git, root, ["tag", tag]);
        return root;
    }

    private static async Task RunAsync(IProgramRunner git, string root, string[] arguments)
    {
        var result = await git.RunAsync("git", arguments, root, TimeSpan.FromSeconds(60), Ct);
        Assert.True(
            result.ExitCode == 0,
            $"'git {string.Join(' ', arguments)}' failed while building this test's repository: {result.StandardError}");
    }
}
