using Jason.Cli;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Contracts.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// What installing twice means, and what it does to what somebody has done to the files since.
/// </summary>
/// <remarks>
/// Defined before it was implemented, because "idempotent" is a word that hides three different promises: that
/// a repeat costs nothing, that a repeat repairs, and that a repeat does not undo somebody's work. The third
/// is the one that becomes a data-loss bug if it is assumed rather than decided.
/// </remarks>
public class SkillsIdempotencyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Twice over an unchanged source: the same bytes, and the second run says it wrote nothing.</summary>
    [Fact]
    public async Task Installing_twice_leaves_the_same_bytes_and_reports_that_it_wrote_nothing()
    {
        using var dir = new TempPaths();
        var source = Source(dir);

        var first = await InstallAsync(dir, source);
        var before = Snapshot(Harness(dir));

        var second = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.Success, first.Exit);
        Assert.Equal(ExitCodes.Success, second.Exit);
        Assert.Equal(before, Snapshot(Harness(dir)));
        Assert.Contains("Already current: nothing to write", second.Output, StringComparison.Ordinal);
    }

    /// <summary>A file the operator deleted is put back. That is what a deployment being current means.</summary>
    [Fact]
    public async Task A_file_the_operator_deleted_is_restored()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await InstallAsync(dir, source);
        var deleted = Path.Combine(Harness(dir), "operating", "reference.md");
        File.Delete(deleted);

        var again = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.Success, again.Exit);
        Assert.True(File.Exists(deleted), "A file the operator deleted was not put back.");
    }

    /// <summary>
    /// A file the operator edited is reported and kept, and <b>nothing at all is written</b>. An installer that
    /// quietly reverts somebody's edit is a data-loss bug wearing a convenience label, and one that reverts
    /// half of them is worse: the operator would have to work out which half.
    /// </summary>
    [Fact]
    public async Task A_file_the_operator_edited_is_reported_and_kept_and_nothing_else_is_written()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await InstallAsync(dir, source);

        var edited = Path.Combine(Harness(dir), "operating", "SKILL.md");
        await File.AppendAllTextAsync(edited, "\nmy own note\n", Ct);
        var mine = await File.ReadAllTextAsync(edited, Ct);
        Change(source, "campaign-planning", "a newer body");
        var untouched = Path.Combine(Harness(dir), "campaign-planning", "SKILL.md");
        var before = await File.ReadAllTextAsync(untouched, Ct);

        var again = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.ApiError, again.Exit);
        Assert.Equal(mine, await File.ReadAllTextAsync(edited, Ct));
        Assert.Equal(before, await File.ReadAllTextAsync(untouched, Ct));
        Assert.Contains("you have edited", again.Error, StringComparison.Ordinal);
        Assert.Contains("--force", again.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file the operator puts <em>inside</em> a deployed skill is theirs too, and it is the one the
    /// "reported and kept" promise could not see.
    /// </summary>
    /// <remarks>
    /// A skill is replaced whole — the old tree moved aside and removed — so an extra file in one is
    /// destroyed by an ordinary re-run against an unchanged source: no <c>--force</c> asked for, the run
    /// reporting a write and never mentioning the deletion. Walking the plan's own files could never catch
    /// it, because the plan does not know the file exists.
    /// </remarks>
    [Fact]
    public async Task A_file_the_operator_put_inside_a_deployed_skill_is_reported_and_kept()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await InstallAsync(dir, source);

        var mine = Path.Combine(Harness(dir), "operating", "my-notes.md");
        await File.WriteAllTextAsync(mine, "what I worked out", Ct);

        var again = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.ApiError, again.Exit);
        Assert.True(File.Exists(mine), "A file the operator put inside a deployed skill was deleted by a re-run.");
        Assert.Contains("my-notes.md", again.Error, StringComparison.Ordinal);
        Assert.Contains("--force", again.Error, StringComparison.Ordinal);
    }

    /// <summary>And --force is the word that says otherwise.</summary>
    [Fact]
    public async Task Force_overwrites_the_edit()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await InstallAsync(dir, source);
        var edited = Path.Combine(Harness(dir), "operating", "SKILL.md");
        await File.AppendAllTextAsync(edited, "\nmy own note\n", Ct);

        var forced = await InstallAsync(dir, source, "--force");

        Assert.Equal(ExitCodes.Success, forced.Exit);
        Assert.DoesNotContain("my own note", await File.ReadAllTextAsync(edited, Ct), StringComparison.Ordinal);
    }

    /// <summary>
    /// A file that is out of date rather than edited — it matches the record and differs from the source — is
    /// simply written. The distinction is the whole reason the record keeps a digest per file.
    /// </summary>
    [Fact]
    public async Task A_file_that_is_merely_out_of_date_is_written_without_being_called_an_edit()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await InstallAsync(dir, source);
        Change(source, "operating", "a newer body");

        var again = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.Success, again.Exit);
        Assert.Contains("a newer body", await File.ReadAllTextAsync(Path.Combine(Harness(dir), "operating", "SKILL.md"), Ct), StringComparison.Ordinal);
    }

    /// <summary>
    /// A deployment whose record never landed. The record is written last, so a crash half-way through always
    /// reads as incomplete next time rather than as a deployment that is current — and the operator is told,
    /// because a root that silently completed itself hides that something went wrong once.
    /// </summary>
    [Fact]
    public async Task A_deployment_whose_record_never_landed_is_completed_and_said_to_have_been()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await InstallAsync(dir, source);
        File.Delete(Path.Combine(Harness(dir), SkillsRecord.FileName));
        File.Delete(Path.Combine(Harness(dir), "campaign-planning", "SKILL.md"));

        var again = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.Success, again.Exit);
        Assert.NotNull(SkillsRecord.Read(Harness(dir)));
        Assert.True(File.Exists(Path.Combine(Harness(dir), "campaign-planning", "SKILL.md")));
        Assert.Contains("no record of what is there", again.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a root whose record cannot be read is not guessed at. Absent and unreadable lead to opposite acts,
    /// and treating the second as the first is how an uninstall removes nothing and reports success.
    /// </summary>
    [Fact]
    public async Task A_root_whose_record_cannot_be_read_is_refused_rather_than_guessed_at()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await File.WriteAllTextAsync(Path.Combine(Harness(dir), SkillsRecord.FileName), "{ not json", Ct);

        var install = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.ApiError, install.Exit);
        Assert.Contains("could not be read", install.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Harness(dir), "operating")));
    }

    private static string Harness(TempPaths dir) =>
        Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;

    private static async Task<(int Exit, string Output, string Error)> InstallAsync(TempPaths dir, string source, params string[] extra)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(
            [.. new[] { "skills", "install", "--source", source, "--root", Harness(dir) }, .. extra],
            new CliEnvironment(
                output,
                error,
                dir.Paths,
                Processes: new FakeProcessControl(),
                Autostart: new RecordingRegistrar(),
                Programs: new FakeProgramRunner()),
            Ct);

        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>Every file under a root with its digest, so "the same bytes" is an assertion and not a hope.</summary>
    private static IReadOnlyList<string> Snapshot(string root) =>
        [.. Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetRelativePath(root, file)} {SkillsRecord.Digest(file)}")];

    private static string Source(TempPaths dir)
    {
        var root = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "source")).FullName;
        Skill(Path.Combine(root, "skills", "runtime", "operating"), "operating", "body");
        Skill(Path.Combine(root, "skills", "runtime", "roles", "researcher"), "researcher", "body");
        Skill(Path.Combine(root, "skills", "business", "campaign-planning"), "campaign-planning", "body");
        return root;
    }

    private static void Change(string source, string skill, string body)
    {
        var directory = skill == "campaign-planning"
            ? Path.Combine(source, "skills", "business", skill)
            : Path.Combine(source, "skills", "runtime", skill);
        Skill(directory, skill, body);
    }

    private static void Skill(string directory, string name, string body)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: one line\n---\n\n{body}\n");
        File.WriteAllText(Path.Combine(directory, "reference.md"), "where to look");
    }
}
