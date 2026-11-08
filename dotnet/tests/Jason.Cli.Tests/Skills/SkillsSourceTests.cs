using Jason.Cli;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Process;
using Jason.Runtime.Tests;

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
    /// development build is older than the release it is on the way to. A candidate is published as a release of its
    /// own, under its own tag, so it keeps its pre-release; a development build is the one kind no release publishes, so
    /// it points at the release it is on the way to. Build metadata is never part of a tag.
    /// </summary>
    [Theory]
    [InlineData("0.1.0", "v0.1.0")]
    [InlineData("0.1.0-dev", "v0.1.0")]
    [InlineData("1.4.2", "v1.4.2")]
    [InlineData("2.0.0-rc.1+build.7", "v2.0.0-rc.1")]
    [InlineData("2.0.0-dev+build.7", "v2.0.0")]
    public void The_default_ref_is_this_builds_own_release_version(string version, string expected) =>
        Assert.Equal(expected, SkillsSource.DefaultRef(version));

    /// <summary>
    /// Every build but a development one is a release: a candidate's tag is published with its package, so a deployment
    /// moves to it and is compared with it; a development build's is not, and a version that does not parse is no build.
    /// </summary>
    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.3-rc.1", true)]
    [InlineData("1.2.3-rc.1+build.7", true)]
    [InlineData("1.2.3-dev", false)]
    [InlineData("1.2.3-dev+build.7", false)]
    [InlineData("not-a-version", false)]
    public void Every_build_but_a_development_one_is_a_release(string version, bool release) =>
        Assert.Equal(release, SkillsSource.IsRelease(version));

    /// <summary>
    /// The environment this program really runs with names no version of its own, so a release compares its skills
    /// with itself and with nothing a caller handed in. The seam is for tests, which run at a development stamp and
    /// still have to say what a release does.
    /// </summary>
    [Fact]
    public void The_environment_this_program_really_runs_with_takes_its_version_from_the_build()
    {
        Assert.Null(CliEnvironment.Default().BuildVersion);

        using var dir = new TempPaths();
        Assert.Null(new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths).BuildVersion);
    }

    /// <summary>
    /// With no ref named, the pin is the version the environment names, which is this build's own everywhere but a test.
    /// </summary>
    [Fact]
    public async Task With_no_ref_a_git_source_is_cloned_at_the_pin_of_the_build_the_environment_names()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v1.2.3");

        var staged = await SkillsSource.StageAsync(Machine(dir) with { BuildVersion = "1.2.3" }, new Uri(origin).AbsoluteUri, null, overriddenByRecord: null, Ct);

        Assert.Equal("v1.2.3", staged.Ref);
        Assert.False(staged.RefOverridden);
        Assert.True(File.Exists(Path.Combine(staged.Directory, "skills", "README.md")));
    }

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

        var staged = await SkillsSource.StageAsync(Machine(dir), clone, null, overriddenByRecord: null, Ct);

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
            () => SkillsSource.StageAsync(Machine(dir), empty, null, overriddenByRecord: null, Ct));

        Assert.Contains("skills", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A ref is fetched, at the ref it was asked for, from a repository with no network behind it.</summary>
    [Fact]
    public async Task A_git_source_is_cloned_at_the_ref_that_was_asked_for()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v0.1.0");

        var staged = await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", overriddenByRecord: null, Ct);

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
            () => SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v9.9.9", overriddenByRecord: null, Ct));

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
    [Critical(Family.X2, "a ref cannot climb")]
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
            () => SkillsSource.StageAsync(Machine(dir), "https://example.test/x.git", reference, overriddenByRecord: null, Ct));

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
    /// A dry run with nothing staged fetches into staging, as a real run would, and deploys nothing.
    /// </summary>
    /// <remarks>
    /// It refused instead, so the look before the act — the first skills command the install page prints — exited 1
    /// on every new installation and showed nothing at all. A clone into Jason's own staging directory is a write,
    /// and the help says so; what a dry run promises is that nothing is deployed, and that holds.
    /// </remarks>
    [Fact]
    public async Task A_dry_run_with_nothing_staged_fetches_into_staging_and_deploys_nothing()
    {
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, "v1.2.3");
        var harness = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;
        var error = new StringWriter();

        var exit = await CliApp.RunAsync(
            ["skills", "install", "--source", origin, "--ref", "v1.2.3", "--root", harness, "--dry-run"],
            new CliEnvironment(new StringWriter(), error, dir.Paths, Programs: ProgramRunners.ForThisMachine()),
            Ct);

        Assert.True(exit == ExitCodes.Success, error.ToString());
        Assert.True(
            File.Exists(Path.Combine(dir.Paths.SkillsStagedSourceDirectory("v1.2.3"), "skills", "runtime", "roles", "researcher", "SKILL.md")),
            "The dry run did not fetch the source into staging.");
        Assert.False(Directory.Exists(dir.Paths.RoleSkillsDirectory), "The dry run deployed into the role root.");
        Assert.Empty(Directory.EnumerateFileSystemEntries(harness));
    }

    /// <summary>And a tree already fetched at that ref is used as it is, rather than fetched again.</summary>
    [Fact]
    public async Task A_ref_already_fetched_is_used_as_it_is_rather_than_fetched_again()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v0.1.0");
        var first = await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", overriddenByRecord: null, Ct);
        var marker = Path.Combine(first.Directory, "skills", "README.md");
        await File.WriteAllTextAsync(marker, "the tree that was already here", Ct);

        var again = await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", overriddenByRecord: null, Ct);

        Assert.Equal(first.Directory, again.Directory);
        Assert.Equal("the tree that was already here", await File.ReadAllTextAsync(marker, Ct));
    }

    /// <summary>
    /// A fetch that was cut off leaves a directory that has a skills tree in it and is not the release. The next run
    /// must fetch again rather than take it as it is: a half-populated tree is deployed as the pack, and the record
    /// then says a release's skills are in place when files of it are missing.
    /// </summary>
    [Fact]
    public async Task A_fetch_that_was_cancelled_part_way_is_fetched_again_rather_than_used_as_it_is()
    {
        using var dir = new TempPaths();
        const string Source = "https://example.test/x.git";
        var cutOff = new FakeProgramRunner(request =>
        {
            if (request.Arguments[0] == "clone")
            {
                WritePack(request.Arguments[^1], "half of the pack");
                throw new OperationCanceledException();
            }

            return new ProgramResult(0, "abc123", string.Empty, false);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SkillsSource.StageAsync(Machine(dir, cutOff), Source, "v0.1.0", overriddenByRecord: null, Ct));

        var whole = new FakeProgramRunner(request =>
        {
            if (request.Arguments[0] == "clone")
            {
                WritePack(request.Arguments[^1], "the whole pack");
            }

            return new ProgramResult(0, "abc123", string.Empty, false);
        });

        var staged = await SkillsSource.StageAsync(Machine(dir, whole), Source, "v0.1.0", overriddenByRecord: null, Ct);

        var text = await File.ReadAllTextAsync(Path.Combine(staged.Directory, "skills", "runtime", "operating", "SKILL.md"), Ct);
        Assert.True(
            text == "the whole pack",
            $"The directory a cancelled fetch left was used as it was: it holds '{text}' where a fetch of the ref holds 'the whole pack'.");
        Assert.Contains(whole.Requested, request => request.Arguments[0] == "clone");
    }

    /// <summary>
    /// A directory staged before a completion mark existed cannot be told from a cut-off one, so it is fetched again
    /// once; and what that fetch completes is trusted from then on, as the test above this one holds.
    /// </summary>
    [Fact]
    public async Task A_staged_directory_that_nothing_completed_is_fetched_again()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v0.1.0");
        var stale = dir.Paths.SkillsStagedSourceDirectory("v0.1.0");
        WritePack(stale, "a tree nothing completed");

        var staged = await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", overriddenByRecord: null, Ct);

        var text = await File.ReadAllTextAsync(Path.Combine(staged.Directory, "skills", "runtime", "operating", "SKILL.md"), Ct);
        Assert.True(
            text != "a tree nothing completed",
            "The directory nothing had completed was used as it was, and the source was never fetched.");
    }

    /// <summary>
    /// A real clone at the ref that nothing marked complete — one an earlier build staged — is staged again, on Windows
    /// too. git writes a clone's pack files read-only and Windows deletes no read-only file, so a bare recursive delete
    /// threw there, and every later install, update and dry run failed the same way until somebody deleted the directory
    /// by hand.
    /// </summary>
    [Fact]
    public async Task A_real_clone_nothing_marked_complete_is_staged_again_rather_than_refused()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v0.1.0");
        var unmarked = dir.Paths.SkillsStagedSourceDirectory("v0.1.0");
        Directory.CreateDirectory(dir.Paths.SkillsStagedSourcesDirectory);
        await RunAsync(ProgramRunners.ForThisMachine(), dir.Paths.SkillsStagedSourcesDirectory, ["clone", "--depth", "1", "--branch", "v0.1.0", "--", new Uri(origin).AbsoluteUri, unmarked]);
        var packFiles = Directory.GetFiles(Path.Combine(unmarked, ".git", "objects", "pack"));
        Assert.True(packFiles.Length > 0, "The clone this test made has no pack files, so it does not stand for a real one.");
        await File.WriteAllTextAsync(Path.Combine(unmarked, "skills", "README.md"), "a clone nothing marked complete", Ct);

        var staged = await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", overriddenByRecord: null, Ct);

        var text = await File.ReadAllTextAsync(Path.Combine(staged.Directory, "skills", "README.md"), Ct);
        Assert.True(text != "a clone nothing marked complete", "The clone nothing had marked complete was used as it was.");
        Assert.NotNull(staged.Commit);
    }

    /// <summary>
    /// What a fetch that never finished left beside the ref, read-only pack files and all, is removed by the next fetch of
    /// that ref; nothing of it is ever at the ref's own path.
    /// </summary>
    [Fact]
    public async Task A_fetch_left_unfinished_beside_the_ref_is_removed_by_the_next_fetch()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v0.1.0");
        var leftover = Partial(dir, "v0.1.0");
        await File.WriteAllTextAsync(Path.Combine(leftover, "claim"), string.Empty, Ct);

        await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", overriddenByRecord: null, Ct);

        Assert.False(Directory.Exists(leftover), $"'{leftover}', a fetch nobody was making any more, was left in staging.");
    }

    /// <summary>
    /// A leftover whose tree cannot be removed yet, because a file in it is held, keeps its claim file: the tree goes before
    /// the claim, so the next fetch finds the claim free and takes the leftover once the hold is gone.
    /// </summary>
    [Fact]
    public async Task A_leftover_whose_tree_could_not_be_removed_keeps_its_claim_and_is_removed_by_a_later_fetch()
    {
        using var dir = new TempPaths();
        var leftover = Partial(dir, "v0.1.0");
        var claim = Path.Combine(leftover, "claim");
        await File.WriteAllTextAsync(claim, string.Empty, Ct);
        var held = Path.Combine(leftover, "tree", "held.txt");
        await File.WriteAllTextAsync(held, "a file something else has open", Ct);
        var empty = new FakeProgramRunner(request =>
        {
            if (request.Arguments[0] == "clone")
            {
                Directory.CreateDirectory(Path.Combine(request.Arguments[^1], ".git"));
            }

            return new ProgramResult(0, "abc123", string.Empty, false);
        });

        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAsync<SkillsSourceUnavailable>(
                () => SkillsSource.StageAsync(Machine(dir, empty), "https://example.test/x.git", "v0.1.0", overriddenByRecord: null, Ct));

            if (OperatingSystem.IsWindows())
            {
                Assert.True(Directory.Exists(leftover), $"'{leftover}' was removed while a file in its tree was held.");
            }

            if (Directory.Exists(leftover))
            {
                Assert.True(File.Exists(claim), $"'{leftover}' was left without its claim file, so no later fetch can tell it is nobody's.");
            }
        }

        await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir, empty), "https://example.test/x.git", "v0.1.0", overriddenByRecord: null, Ct));

        Assert.False(Directory.Exists(leftover), $"'{leftover}', whose hold was gone, was left in staging.");
    }

    /// <summary>
    /// And a fetch of the same ref that another run is still making is left alone: deleting it under that run is how one
    /// command broke another's install.
    /// </summary>
    [Fact]
    public async Task A_fetch_another_run_is_still_making_is_left_alone()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v0.1.0");
        var inFlight = Partial(dir, "v0.1.0");

        using (new FileStream(Path.Combine(inFlight, "claim"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", overriddenByRecord: null, Ct);

            Assert.True(
                File.Exists(Path.Combine(inFlight, "tree", ".git", "objects", "pack", "x.idx")),
                $"'{inFlight}', a fetch another run was still making, was deleted under it.");
        }
    }

    /// <summary>
    /// A clone whose tree holds no packs is refused and is never at the ref's path, and what it left is not marked
    /// complete: the mark is written last, after the packs are checked.
    /// </summary>
    [Fact]
    public async Task A_clone_with_no_packs_is_refused_and_nothing_is_marked_complete()
    {
        using var dir = new TempPaths();
        var empty = new FakeProgramRunner(request =>
        {
            if (request.Arguments[0] == "clone")
            {
                Directory.CreateDirectory(Path.Combine(request.Arguments[^1], ".git"));
            }

            return new ProgramResult(0, "abc123", string.Empty, false);
        });

        await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir, empty), "https://example.test/x.git", "v0.1.0", overriddenByRecord: null, Ct));

        AssertNothingCompleted(dir, "v0.1.0");
    }

    /// <summary>
    /// A fetch cancelled while its commit is read is not complete either: the commit is read before the mark is written,
    /// and nothing is at the ref's path.
    /// </summary>
    [Fact]
    public async Task A_fetch_cancelled_while_its_commit_is_read_is_not_marked_complete()
    {
        using var dir = new TempPaths();
        var cancelled = new FakeProgramRunner(request =>
        {
            if (request.Arguments[0] == "clone")
            {
                WritePack(request.Arguments[^1], "the whole pack");
                Directory.CreateDirectory(Path.Combine(request.Arguments[^1], ".git"));
                return new ProgramResult(0, string.Empty, string.Empty, false);
            }

            throw new OperationCanceledException();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => SkillsSource.StageAsync(Machine(dir, cancelled), "https://example.test/x.git", "v0.1.0", overriddenByRecord: null, Ct));

        AssertNothingCompleted(dir, "v0.1.0");
    }

    /// <summary>
    /// The mark lives where no checkout writes: a source that tracks a file of the mark's name, or of its old name, at its
    /// root does not make a tree nothing completed read as complete.
    /// </summary>
    [Fact]
    public async Task A_tracked_file_named_like_the_mark_does_not_make_a_tree_complete()
    {
        using var dir = new TempPaths();
        var origin = await RepositoryAsync(dir, "v0.1.0");
        var stale = dir.Paths.SkillsStagedSourceDirectory("v0.1.0");
        WritePack(stale, "a tree nothing completed");
        await File.WriteAllTextAsync(Path.Combine(stale, "jason-staged"), "tracked by the source", Ct);
        await File.WriteAllTextAsync(Path.Combine(stale, ".jason-staged"), "tracked by the source", Ct);

        var staged = await SkillsSource.StageAsync(Machine(dir), new Uri(origin).AbsoluteUri, "v0.1.0", overriddenByRecord: null, Ct);

        var text = await File.ReadAllTextAsync(Path.Combine(staged.Directory, "skills", "runtime", "operating", "SKILL.md"), Ct);
        Assert.True(text != "a tree nothing completed", "A tree whose only mark was a file the source could track was used as it was.");
    }

    /// <summary>
    /// Two runs fetching the same ref: the one that finishes second finds the first one's tree at the ref's path, uses
    /// it — the same commit, since a pinned ref does not move — and leaves nothing of its own behind.
    /// </summary>
    [Fact]
    public async Task A_fetch_that_finishes_second_uses_the_tree_the_first_one_put_in_place()
    {
        using var dir = new TempPaths();
        var final = dir.Paths.SkillsStagedSourceDirectory("v0.1.0");
        var second = new FakeProgramRunner(request =>
        {
            if (request.Arguments[0] == "clone")
            {
                // The other run's finished tree arrives while this one is still fetching.
                WritePack(final, "the first run's tree");
                Directory.CreateDirectory(Path.Combine(final, ".git"));
                File.WriteAllText(Path.Combine(final, ".git", "jason-staged"), "abc123");

                WritePack(request.Arguments[^1], "the second run's tree");
                Directory.CreateDirectory(Path.Combine(request.Arguments[^1], ".git"));
            }

            return new ProgramResult(0, "abc123", string.Empty, false);
        });

        var staged = await SkillsSource.StageAsync(Machine(dir, second), "https://example.test/x.git", "v0.1.0", overriddenByRecord: null, Ct);

        Assert.Equal(final, staged.Directory);
        var text = await File.ReadAllTextAsync(Path.Combine(final, "skills", "runtime", "operating", "SKILL.md"), Ct);
        Assert.True(text == "the first run's tree", $"The ref's path holds '{text}' where the first run's finished tree was.");
        var left = Directory.GetDirectories(dir.Paths.SkillsStagedSourcesDirectory).Where(path => path != final).ToList();
        Assert.True(left.Count == 0, $"The second fetch left {string.Join(", ", left)} in staging.");
    }

    /// <summary>Nothing at the ref's path, and no mark anywhere in staging.</summary>
    private static void AssertNothingCompleted(TempPaths dir, string reference)
    {
        var final = dir.Paths.SkillsStagedSourceDirectory(reference);
        Assert.False(Directory.Exists(final), $"'{final}' exists after a fetch that did not finish.");
        var marks = Directory.EnumerateFiles(dir.Paths.SkillsStagedSourcesDirectory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is "jason-staged" or ".jason-staged")
            .ToList();
        Assert.True(marks.Count == 0, $"A fetch that did not finish is marked complete at {string.Join(", ", marks)}.");
    }

    /// <summary>
    /// What an unfinished fetch of a ref leaves beside it: a directory of its own with the clone's tree in it, a pack file
    /// in that tree marked read-only as git marks them.
    /// </summary>
    private static string Partial(TempPaths dir, string reference)
    {
        var partial = $"{dir.Paths.SkillsStagedSourceDirectory(reference)}.partial-{Guid.NewGuid():N}";
        var tree = Path.Combine(partial, "tree");
        WritePack(tree, "half of the pack");
        var pack = Directory.CreateDirectory(Path.Combine(tree, ".git", "objects", "pack")).FullName;
        var index = Path.Combine(pack, "x.idx");
        File.WriteAllText(index, "index");
        File.SetAttributes(index, FileAttributes.ReadOnly);
        return partial;
    }

    /// <summary>An environment with no program runner refuses rather than reaching for the network.</summary>
    [Fact]
    public async Task Without_a_program_runner_a_git_source_is_refused()
    {
        using var dir = new TempPaths();
        var machine = new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths);

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(machine, "https://example.test/x.git", "v0.1.0", overriddenByRecord: null, Ct));

        Assert.Contains("cannot run git", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>And a runner that runs nothing is the same answer: it is refused, never silently skipped.</summary>
    [Fact]
    public async Task A_clone_that_fails_is_refused_rather_than_reported_as_staged()
    {
        using var dir = new TempPaths();
        var machine = Machine(dir, new FakeProgramRunner(_ => new ProgramResult(128, string.Empty, "fatal: could not read from remote", false)));

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(machine, "https://example.test/x.git", "v0.1.0", overriddenByRecord: null, Ct));

        Assert.Contains("could not read from remote", refusal.Message, StringComparison.Ordinal);
    }

    private static CliEnvironment Machine(TempPaths dir, IProgramRunner? runner = null) =>
        new(new StringWriter(), new StringWriter(), dir.Paths, Programs: runner ?? ProgramRunners.ForThisMachine());

    /// <summary>A skills tree with one pack in it, whose text says how much of the release it is.</summary>
    private static void WritePack(string directory, string text)
    {
        var pack = Directory.CreateDirectory(Path.Combine(directory, "skills", "runtime", "operating")).FullName;
        File.WriteAllText(Path.Combine(pack, "SKILL.md"), text);
    }

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
