using Jason.Cli;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Contracts.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;
using Jason.Runtime.Tests;

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
    /// A file the operator edited is reported and kept, and everything else is written, as the help says. An installer
    /// that quietly reverts somebody's edit is a data-loss bug wearing a convenience label; one that refuses every other
    /// file over it left the person a choice between their edit and their skills, which an uninstall that keeps an edited
    /// file on purpose put to them on the next install. The run succeeds and names each file it kept.
    /// </summary>
    [Fact]
    [Critical(Family.I3, "an operator's edit is never overwritten")]
    public async Task A_file_the_operator_edited_is_reported_and_kept_and_everything_else_is_written()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await InstallAsync(dir, source);

        var edited = Path.Combine(Harness(dir), "operating", "SKILL.md");
        await File.AppendAllTextAsync(edited, "\nmy own note\n", Ct);
        var mine = await File.ReadAllTextAsync(edited, Ct);
        Change(source, "campaign-planning", "a newer body");
        Change(source, "operating", "a newer body");

        var again = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.Success, again.Exit);
        Assert.Equal(mine, await File.ReadAllTextAsync(edited, Ct));
        Assert.Contains("a newer body", await File.ReadAllTextAsync(Path.Combine(Harness(dir), "campaign-planning", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.Contains($"Kept 1 file(s) you have edited: {edited}.", again.Output, StringComparison.Ordinal);
        Assert.Contains("--force", again.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, again.Error);

        // And the next run knows it for an edit still: kept again, never taken for a file that is merely out of date.
        var third = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.Success, third.Exit);
        Assert.Equal(mine, await File.ReadAllTextAsync(edited, Ct));
        Assert.Contains("Kept 1 file(s) you have edited", third.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reinstall after an uninstall that kept an edited file and its record: every other skill is written back beside
    /// it, the edit stays, and the run succeeds. It used to write nothing and exit 1.
    /// </summary>
    [Fact]
    public async Task A_reinstall_beside_an_edited_file_an_uninstall_kept_writes_every_other_skill()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await InstallAsync(dir, source);
        var edited = Path.Combine(Harness(dir), "operating", "SKILL.md");
        await File.AppendAllTextAsync(edited, "\nmy own note\n", Ct);
        var mine = await File.ReadAllTextAsync(edited, Ct);
        File.Delete(Path.Combine(Harness(dir), "operating", "reference.md"));
        Directory.Delete(Path.Combine(Harness(dir), "campaign-planning"), recursive: true);

        var again = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.Success, again.Exit);
        Assert.Equal(mine, await File.ReadAllTextAsync(edited, Ct));
        Assert.True(File.Exists(Path.Combine(Harness(dir), "operating", "reference.md")), "The other file of the edited skill was not written back.");
        Assert.True(File.Exists(Path.Combine(Harness(dir), "campaign-planning", "SKILL.md")), "A skill of the other pack was not written back.");
        Assert.Contains($"Kept 1 file(s) you have edited: {edited}.", again.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A skill holding the person's edit is written file by file, and a write that fails part-way recorded the old digests
    /// for every file of it: the ones already moved into place read as the person's edits from then on, and a file new in
    /// this release was in no record and removed by no uninstall. The record states what is on disk: each file written at
    /// its new digest, each never reached at its old one, the edit at what Jason wrote there.
    /// </summary>
    [Fact]
    public async Task A_write_beside_an_edit_that_fails_part_way_records_what_is_on_disk()
    {
        // A file another program holds without sharing its deletion refuses a move onto it on Windows only.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses a move onto a file another program holds.");

        using var dir = new TempPaths();
        var source = Source(dir);
        var operating = Path.Combine(source, "skills", "runtime", "operating");
        Directory.CreateDirectory(Path.Combine(operating, "more"));
        File.WriteAllText(Path.Combine(operating, "more", "notes.md"), "first notes");
        await InstallAsync(dir, source);

        var edited = Path.Combine(Harness(dir), "operating", "SKILL.md");
        await File.AppendAllTextAsync(edited, "\nmy own note\n", Ct);
        File.WriteAllText(Path.Combine(operating, "a-guide.md"), "new in this release");
        File.WriteAllText(Path.Combine(operating, "reference.md"), "where to look now");
        File.WriteAllText(Path.Combine(operating, "more", "notes.md"), "second notes");

        // A subdirectory's files come after the skill's own, so the held one is reached last.
        var held = Path.Combine(Harness(dir), "operating", "more", "notes.md");
        (int Exit, string Output, string Error) again;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            again = await InstallAsync(dir, source);
        }

        Assert.Equal(ExitCodes.ApiError, again.Exit);
        var guide = Path.Combine(Harness(dir), "operating", "a-guide.md");
        Assert.True(File.Exists(guide), "The files before the held one were not written.");

        var record = SkillsRecord.Read(Harness(dir));
        Assert.NotNull(record);
        var files = record.Packs.SelectMany(pack => pack.Files)
            .Where(file => file.Path.StartsWith("operating/", StringComparison.Ordinal))
            .ToDictionary(file => file.Path, file => file.Sha256, StringComparer.Ordinal);
        Assert.Contains("operating/a-guide.md", files.Keys);
        foreach (var (path, digest) in files.Where(entry => entry.Key != "operating/SKILL.md"))
        {
            Assert.Equal(SkillsRecord.Digest(Path.Combine(Harness(dir), path.Replace('/', Path.DirectorySeparatorChar))), digest);
        }

        Assert.NotEqual(SkillsRecord.Digest(edited), files["operating/SKILL.md"]);

        // And an uninstall removes the file this release wrote, and keeps the edit.
        var output = new StringWriter();
        await CliApp.RunAsync(
            ["uninstall", "--dry-run"],
            new CliEnvironment(
                output,
                new StringWriter(),
                dir.Paths,
                Harnesses: HarnessLocators.At(Harness(dir)),
                Removes: new Uninstall.RecordingRemover(),
                InstallPath: Path.Combine(dir.Paths.Root, "bin", "jason"),
                SearchPath: Path.Combine(dir.Paths.Root, "bin")),
            Ct);
        var plan = System.Text.Json.JsonSerializer.Deserialize<Jason.Cli.Uninstall.UninstallReport>(output.ToString(), Jason.Contracts.Json.JasonJson.Options)!.Plan;
        var removals = plan.Roots.SelectMany(root => root.Files).ToDictionary(file => Path.GetFullPath(file.Path), StringComparer.OrdinalIgnoreCase);
        Assert.False(removals[Path.GetFullPath(guide)].Edited, "The file this release wrote is kept by an uninstall as the person's.");
        Assert.True(removals[Path.GetFullPath(edited)].Edited);
    }

    /// <summary>
    /// A file a later release drops from a skill is Jason's own, and the record says so: on disk at the digest the record
    /// names. It was taken for the person's file, kept for good and named "you have edited" on every run, and left the
    /// record, so no uninstall removed it. It goes with the skill now, swapped whole or, beside an edit, deleted.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_file_a_later_release_drops_goes_with_the_skill(bool besideAnEdit)
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var operating = Path.Combine(source, "skills", "runtime", "operating");
        File.WriteAllText(Path.Combine(operating, "old.md"), "dropped later");
        await InstallAsync(dir, source);
        var edited = Path.Combine(Harness(dir), "operating", "SKILL.md");
        if (besideAnEdit)
        {
            await File.AppendAllTextAsync(edited, "\nmy own note\n", Ct);
        }

        File.Delete(Path.Combine(operating, "old.md"));
        File.WriteAllText(Path.Combine(operating, "reference.md"), "where to look now");

        var again = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.Success, again.Exit);
        Assert.False(File.Exists(Path.Combine(Harness(dir), "operating", "old.md")), "A file the release dropped was kept.");
        Assert.DoesNotContain("old.md", again.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            SkillsRecord.Read(Harness(dir))!.Packs.SelectMany(pack => pack.Files),
            file => file.Path == "operating/old.md");
        Assert.Equal("where to look now", await File.ReadAllTextAsync(Path.Combine(Harness(dir), "operating", "reference.md"), Ct));
        if (besideAnEdit)
        {
            Assert.Contains($"Kept 1 file(s) you have edited: {edited}.", again.Output, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A file is written beside an edit by landing it in the root's staging directory and moving it into place. A kill
    /// between the two leaves the landing there, never inside the skill where the next run took it for the person's file,
    /// and the next run collects it.
    /// </summary>
    [Fact]
    public async Task A_landing_file_a_kill_left_in_the_staging_directory_is_collected()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await InstallAsync(dir, source);
        var staging = Path.Combine(Harness(dir), ".jason-staging");
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "SKILL.md.landing-0123456789abcdef"), "half a run");
        Change(source, "operating", "a newer body");

        var again = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.Success, again.Exit);
        Assert.False(Directory.Exists(staging), "A landing file a kill left was not collected.");
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
        Change(source, "operating", "a newer body");

        var again = await InstallAsync(dir, source);

        Assert.Equal(ExitCodes.Success, again.Exit);
        Assert.Equal("what I worked out", await File.ReadAllTextAsync(mine, Ct));
        Assert.Contains("a newer body", await File.ReadAllTextAsync(Path.Combine(Harness(dir), "operating", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.Contains("my-notes.md", again.Output, StringComparison.Ordinal);
        Assert.Contains("--force", again.Output, StringComparison.Ordinal);
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
