using System.Text.Json;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;
using Jason.Cli.Tests.Uninstall;
using Jason.Cli.Uninstall;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// The one rule every door deploys under (<see cref="SkillsDeployer.Judge"/>): a folder under a name Jason ships, which the
/// root's record does not name, is the person's unless it is byte for byte the one Jason ships. Theirs is left as it is —
/// nothing of it written, nothing recorded, no flag reaching it — and named in the answer every time; an equal one is
/// adopted and recorded. The uninstall after removes what a record names and still carries Jason's digest, so the person's
/// folder outlives it and Jason's does not.
/// </summary>
/// <remarks>
/// Each row plants a real folder that differs from the bundle. "With a record" is a root Jason deployed into before, from a
/// release that did not ship the name, which a later one does; "with none" is a root Jason meets for the first time.
/// </remarks>
public sealed class SkillsLeftAloneTests
{
    /// <summary>What a person's own skill under a name Jason ships holds: not a byte of it is the bundle's.</summary>
    internal const string Theirs = "---\nname: quick-start\ndescription: mine\n---\n\na person's own skill\n";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The deployment doors of the verb `jason skills`, each with a record for the root and with none.</summary>
    public static TheoryData<string, bool> Doors => new()
    {
        { "install", false },
        { "install", true },
        { "install --force", false },
        { "install --force", true },
        { "install --dry-run", false },
        { "update", true },
        { "update", false },
    };

    [Theory]
    [MemberData(nameof(Doors))]
    [Critical(Family.I3, "a folder of the person's own under a name Jason ships is left alone by every door, and the uninstall keeps it")]
    public async Task A_folder_of_your_own_under_a_shipped_name_is_left_alone_named_and_outlives_the_uninstall(string door, bool recorded)
    {
        using var dir = new TempPaths();
        var root = Harness(dir);
        var source = Source(dir, quickStart: !recorded);
        if (recorded)
        {
            // A release that did not ship quick-start deployed here first; the next one does.
            Assert.Equal(ExitCodes.Success, (await RunAsync(dir, ["skills", "install", "--source", source, "--root", root])).Exit);
            Skill(Path.Combine(source, "skills", "runtime", "quick-start"), "quick-start", "Jason's");
        }

        var own = Plant(root, "quick-start");
        var before = Snapshot(own);
        string[] arguments = door switch
        {
            "install" => ["skills", "install", "--source", source, "--root", root],
            "install --force" => ["skills", "install", "--source", source, "--root", root, "--force"],
            "install --dry-run" => ["skills", "install", "--source", source, "--root", root, "--dry-run"],
            "update" => ["skills", "update", "--root", root],
            _ => throw new ArgumentOutOfRangeException(nameof(door)),
        };

        var run = await RunAsync(dir, arguments);

        Assert.Equal(before, Snapshot(own));
        var record = SkillsRecord.Read(root);
        if (door == "update" && !recorded)
        {
            // Nothing here was Jason's, so an update has nothing to move, and moves nothing.
            Assert.Equal(ExitCodes.ApiError, run.Exit);
            Assert.Null(record);
            return;
        }

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.Contains(SkillsDeployer.LeftAloneSentence(new UnrecordedSkill(root, SkillPacks.Runtime, "quick-start")), run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("completed rather than trusted", run.Output, StringComparison.Ordinal);
        if (door == "install --dry-run")
        {
            Assert.Null(record);
            return;
        }

        Assert.NotNull(record);
        Assert.DoesNotContain(record.Packs.SelectMany(pack => pack.Files), file => file.Path.StartsWith("quick-start/", StringComparison.Ordinal));
        Assert.Equal(["quick-start"], record.Packs.Single(pack => pack.Pack == SkillPacks.Runtime).LeftAlone);
        Assert.True(File.Exists(Path.Combine(root, "operating", "SKILL.md")));

        // And the uninstall after it: Jason's go by receipt, the person's folder stays as it was.
        var (exit, _) = await UninstallAsync(dir, root);
        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(before, Snapshot(own));
        Assert.False(File.Exists(Path.Combine(root, "operating", "SKILL.md")), "a skill Jason deployed survived the uninstall");
    }

    /// <summary>
    /// Said every time, not only the first: the second deployment over the folder names it again, and writes nothing.
    /// </summary>
    [Fact]
    public async Task A_folder_left_alone_is_named_again_by_every_later_deployment()
    {
        using var dir = new TempPaths();
        var root = Harness(dir);
        var source = Source(dir, quickStart: true);
        var own = Plant(root, "quick-start");
        var before = Snapshot(own);
        await RunAsync(dir, ["skills", "install", "--source", source, "--root", root]);

        var again = await RunAsync(dir, ["skills", "install", "--source", source, "--root", root]);
        var update = await RunAsync(dir, ["skills", "update", "--root", root]);

        var sentence = SkillsDeployer.LeftAloneSentence(new UnrecordedSkill(root, SkillPacks.Runtime, "quick-start"));
        Assert.Contains(sentence, again.Output, StringComparison.Ordinal);
        Assert.Contains(sentence, update.Output, StringComparison.Ordinal);
        Assert.Contains("Already current: nothing to write", again.Output, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(own));
    }

    /// <summary>
    /// A folder byte for byte the one Jason ships, with no record of it, is adopted: recorded as Jason's, nothing written,
    /// said; and the uninstall then removes it by receipt, as it removes everything Jason recorded.
    /// </summary>
    [Fact]
    public async Task A_folder_byte_for_byte_the_shipped_one_is_adopted_and_removed_by_receipt()
    {
        using var dir = new TempPaths();
        var root = Harness(dir);
        var source = Source(dir, quickStart: true);
        var copy = Path.Combine(root, "quick-start");
        Directory.CreateDirectory(copy);
        foreach (var file in Directory.GetFiles(Path.Combine(source, "skills", "runtime", "quick-start")))
        {
            File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
        }

        var run = await RunAsync(dir, ["skills", "install", "--source", source, "--root", root]);

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.Contains(SkillsDeployer.AdoptedSentence(new UnrecordedSkill(root, SkillPacks.Runtime, "quick-start")), run.Output, StringComparison.Ordinal);
        var record = SkillsRecord.Read(root)!;
        Assert.Contains(record.Packs.SelectMany(pack => pack.Files), file => file.Path == "quick-start/SKILL.md");
        Assert.Empty(record.Packs.SelectMany(pack => pack.LeftAlone));

        var (exit, _) = await UninstallAsync(dir, root);
        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(copy), "an adopted skill outlived the uninstall that removes by receipt");
    }

    /// <summary>
    /// An older release's skill with no record, as a crash before the record leaves it: from the disk alone it is a folder
    /// nothing can tell from the person's, so it is left alone and named, never completed in silence to this release.
    /// </summary>
    [Fact]
    public async Task An_older_jason_skill_with_no_record_is_left_alone_and_named_never_completed()
    {
        using var dir = new TempPaths();
        var root = Harness(dir);
        var source = Source(dir, quickStart: true);
        await RunAsync(dir, ["skills", "install", "--source", source, "--root", root]);
        File.Delete(Path.Combine(root, SkillsRecord.FileName));
        Skill(Path.Combine(source, "skills", "runtime", "operating"), "operating", "a newer body");
        var older = Snapshot(Path.Combine(root, "operating"));

        var run = await RunAsync(dir, ["skills", "install", "--source", source, "--root", root]);

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.Equal(older, Snapshot(Path.Combine(root, "operating")));
        Assert.Contains(SkillsDeployer.LeftAloneSentence(new UnrecordedSkill(root, SkillPacks.Runtime, "operating")), run.Output, StringComparison.Ordinal);
        Assert.Contains(SkillsDeployer.AdoptedSentence(new UnrecordedSkill(root, SkillPacks.Runtime, "quick-start")), run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("completed rather than trusted", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The roles home is a door of the rule too, with no exception: it is where a person adds a role of their own, and a
    /// name a later release ships must not cost them that role. With a record there and with none.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_role_of_your_own_under_a_name_a_release_ships_is_left_alone_in_the_roles_home(bool recorded)
    {
        using var dir = new TempPaths();
        var roles = dir.Paths.RoleSkillsDirectory;
        var source = Source(dir, quickStart: false, researcher: !recorded);
        if (recorded)
        {
            Assert.Equal(ExitCodes.Success, (await RunAsync(dir, ["skills", "install", "--source", source, "--roles-only"])).Exit);
            Skill(Path.Combine(source, "skills", "runtime", "roles", "researcher"), "researcher", "Jason's");
        }

        var own = Plant(roles, "researcher");
        var before = Snapshot(own);

        var run = await RunAsync(dir, ["skills", "install", "--source", source, "--roles-only", "--force"]);

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.Equal(before, Snapshot(own));
        Assert.Contains(SkillsDeployer.LeftAloneSentence(new UnrecordedSkill(roles, SkillPacks.Runtime, "researcher", RolesHome: true)), run.Output, StringComparison.Ordinal);
        Assert.Contains("the runtime teaches the role 'researcher' from that folder. To have the one Jason ships, move or remove that folder, then run `jason skills install --roles-only`.", run.Output, StringComparison.Ordinal);
        var record = SkillsRecord.Read(roles)!;
        Assert.DoesNotContain(record.Packs.SelectMany(pack => pack.Files), file => file.Path.StartsWith("researcher/", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(roles, "planner", "SKILL.md")));
    }

    /// <summary>
    /// A link standing under a shipped name is never adopted, even where what it names is the shipped skill byte for byte:
    /// what it names is outside the root, and a removal by receipt would reach there.
    /// </summary>
    [Fact]
    public async Task A_link_under_a_shipped_name_is_left_alone_even_where_it_names_the_shipped_skill()
    {
        using var dir = new TempPaths();
        var root = Harness(dir);
        var source = Source(dir, quickStart: true);
        var link = Path.Combine(root, "quick-start");
        try
        {
            Directory.CreateSymbolicLink(link, Path.Combine(source, "skills", "runtime", "quick-start"));
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("A directory symbolic link needs a privilege this account does not hold.");
        }

        var run = await RunAsync(dir, ["skills", "install", "--source", source, "--root", root]);

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.NotNull(new DirectoryInfo(link).LinkTarget);
        Assert.Contains(SkillsDeployer.LeftAloneSentence(new UnrecordedSkill(root, SkillPacks.Runtime, "quick-start")), run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(SkillsRecord.Read(root)!.Packs.SelectMany(pack => pack.Files), file => file.Path.StartsWith("quick-start/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The list a record keeps of what it left alone is a fact of the last deployment, never a standing claim: once the
    /// person moves their folder away, the command the sentence names writes Jason's skill there, and the name drops out of
    /// the list. In an agent host's folder and in the roles home, each by its own command.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_folder_moved_away_is_written_by_the_command_the_sentence_names_and_drops_out_of_the_list(bool roles)
    {
        using var dir = new TempPaths();
        var root = roles ? dir.Paths.RoleSkillsDirectory : Harness(dir);
        var name = roles ? "researcher" : "quick-start";
        var source = Source(dir, quickStart: true);
        Plant(root, name);
        string[] install = roles ? ["skills", "install", "--source", source, "--roles-only"] : ["skills", "install", "--source", source, "--root", root];
        var first = await RunAsync(dir, install);
        var sentence = SkillsDeployer.LeftAloneSentence(new UnrecordedSkill(root, SkillPacks.Runtime, name, roles));
        Assert.Contains(sentence, first.Output, StringComparison.Ordinal);
        Assert.Equal([name], SkillsRecord.Read(root)!.Packs.Single(pack => pack.Pack == SkillPacks.Runtime).LeftAlone);

        Directory.Move(Path.Combine(root, name), Path.Combine(dir.Paths.Root, "moved-" + name));
        var command = sentence[(sentence.LastIndexOf("run `", StringComparison.Ordinal) + 5)..^2];
        Assert.Equal(roles ? SkillsDeployer.RedeployRoles : SkillsDeployer.RedeployHost, command);
        string[] again = [.. command.Split(' ').Skip(1), .. roles ? new[] { "--source", source } : ["--root", root]];

        var run = await RunAsync(dir, again);

        Assert.Equal(ExitCodes.Success, run.Exit);
        Assert.DoesNotContain("Jason takes it for yours", run.Output, StringComparison.Ordinal);
        var shipped = Path.Combine(source, "skills", "runtime", roles ? Path.Combine("roles", name) : name, "SKILL.md");
        Assert.Equal(File.ReadAllText(shipped), File.ReadAllText(Path.Combine(root, name, "SKILL.md")));
        var runtime = SkillsRecord.Read(root)!.Packs.Single(pack => pack.Pack == SkillPacks.Runtime);
        Assert.Empty(runtime.LeftAlone);
        Assert.Contains(runtime.Files, file => file.Path == name + "/SKILL.md");
    }

    /// <summary>
    /// Nothing removes by the list of what was left alone: the uninstall reads the files a record names and nothing else, so
    /// a folder the list names stays, with every byte of it, while Jason's go.
    /// </summary>
    [Fact]
    public async Task The_uninstall_removes_nothing_by_the_list_of_what_was_left_alone()
    {
        using var dir = new TempPaths();
        var root = Harness(dir);
        var source = Source(dir, quickStart: true);
        var own = Plant(root, "quick-start");
        var before = Snapshot(own);
        await RunAsync(dir, ["skills", "install", "--source", source, "--root", root]);
        Assert.Equal(["quick-start"], SkillsRecord.Read(root)!.Packs.Single(pack => pack.Pack == SkillPacks.Runtime).LeftAlone);

        var (exit, report) = await UninstallAsync(dir, root);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(before, Snapshot(own));
        Assert.False(File.Exists(Path.Combine(root, "operating", "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(root, SkillsRecord.FileName)), "the record outlived an uninstall that removed every file it names");
        Assert.DoesNotContain("quick-start", JsonSerializer.Serialize(report, JasonJson.Options), StringComparison.Ordinal);
    }

    internal static string Harness(TempPaths dir) =>
        Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;

    /// <summary>A person's own skill folder under a name: a SKILL.md of theirs and a note beside it.</summary>
    internal static string Plant(string root, string name)
    {
        var folder = Path.Combine(root, name);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), Theirs.Replace("quick-start", name, StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(folder, "notes.md"), "mine");
        return folder;
    }

    /// <summary>Every file under a folder with its digest, so "as it was" is an assertion and not a hope.</summary>
    internal static IReadOnlyList<string> Snapshot(string folder) =>
        [.. Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetRelativePath(folder, file)} {SkillsRecord.Digest(file)}")];

    internal static string Source(TempPaths dir, bool quickStart, bool researcher = true)
    {
        var root = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "source")).FullName;
        Skill(Path.Combine(root, "skills", "runtime", "operating"), "operating", "body");
        if (quickStart)
        {
            Skill(Path.Combine(root, "skills", "runtime", "quick-start"), "quick-start", "Jason's");
        }

        Skill(Path.Combine(root, "skills", "runtime", "roles", "planner"), "planner", "body");
        if (researcher)
        {
            Skill(Path.Combine(root, "skills", "runtime", "roles", "researcher"), "researcher", "body");
        }

        Skill(Path.Combine(root, "skills", "business", "campaign-planning"), "campaign-planning", "body");
        return root;
    }

    internal static void Skill(string directory, string name, string body)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), $"---\nname: {name}\ndescription: one line\n---\n\n{body}\n");
        File.WriteAllText(Path.Combine(directory, "reference.md"), "where to look");
    }

    private static async Task<(int Exit, string Output, string Error)> RunAsync(TempPaths dir, string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(
            arguments,
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

    internal static Task<(int Exit, UninstallReport Report)> UninstallAsync(TempPaths dir, string harnessRoot) =>
        UninstallAsync(dir.Paths, HarnessLocators.At(harnessRoot), Path.Combine(dir.Paths.Root, "bin", "jason"));

    /// <summary>The uninstall, removing for real, of an installation at these paths and these harness roots.</summary>
    internal static async Task<(int Exit, UninstallReport Report)> UninstallAsync(JasonPaths paths, IHarnessLocator harnesses, string installPath)
    {
        var output = new StringWriter();
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            paths,
            Autostart: new RecordingRegistrar(),
            Harnesses: harnesses,
            InstallPath: installPath,
            Removes: new RecordingRemover { ReallyRemoves = true });

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: true, Force: false), Ct);
        return (exit, JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!);
    }
}
