using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// <c>jason skills update</c>: the act re-run against what the record says it was installed from.
/// </summary>
/// <remarks>
/// It takes no <c>--source</c> and no <c>--ref</c> on purpose. An update is the same deployment repeated, not
/// a second decision about where things come from — a verb that accepted both would let somebody "update" a
/// deployment into one from somewhere else and leave a record saying it had always been that way.
/// </remarks>
public class SkillsUpdateTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Update_uses_the_source_and_ref_the_record_names()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await RunAsync(dir, ["skills", "install", "--source", source, "--root", Harness(dir)]);

        Change(source, "operating", "a newer body");
        var update = await RunAsync(dir, ["skills", "update", "--root", Harness(dir)]);

        Assert.Equal(ExitCodes.Success, update.Exit);
        Assert.Contains(source, update.Output, StringComparison.Ordinal);
        Assert.Contains(
            "a newer body",
            await File.ReadAllTextAsync(Path.Combine(Harness(dir), "operating", "SKILL.md"), Ct),
            StringComparison.Ordinal);
    }

    /// <summary>A no-op says so rather than reporting an update it did not perform.</summary>
    [Fact]
    public async Task Update_over_an_unchanged_source_reports_that_nothing_changed()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await RunAsync(dir, ["skills", "install", "--source", source, "--root", Harness(dir)]);

        var update = await RunAsync(dir, ["skills", "update", "--root", Harness(dir)]);

        Assert.Equal(ExitCodes.Success, update.Exit);
        Assert.Contains("Already current: nothing to write", update.Output, StringComparison.Ordinal);
    }

    /// <summary>A root nothing was installed into has nothing to update, and says that rather than installing.</summary>
    [Fact]
    public async Task Update_without_a_record_refuses_and_names_the_install_verb()
    {
        using var dir = new TempPaths();

        var update = await RunAsync(dir, ["skills", "update", "--root", Harness(dir)]);

        Assert.Equal(ExitCodes.ApiError, update.Exit);
        Assert.Contains("jason skills install", update.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(Harness(dir), "operating")));
    }

    /// <summary>
    /// And an update validates before it writes exactly as an install does. The failure it prevents — every
    /// launch of that role refused — does not care which verb wrote the tree, and this is the verb that
    /// reaches the replace path in ordinary use.
    /// </summary>
    [Fact]
    public async Task Update_refuses_a_tree_the_launcher_would_refuse_and_leaves_the_old_one_in_place()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await RunAsync(dir, ["skills", "install", "--source", source, "--root", Harness(dir)]);
        var live = Path.Combine(dir.Paths.RoleSkillsDirectory, "researcher");
        var before = await File.ReadAllTextAsync(Path.Combine(live, "SKILL.md"), Ct);

        await File.WriteAllTextAsync(
            Path.Combine(source, "skills", "runtime", "roles", "researcher", "SKILL.md"),
            "---\nname: researcher-v2\ndescription: one line\n---\n\nbody\n",
            Ct);

        var update = await RunAsync(dir, ["skills", "update", "--root", Harness(dir)]);

        Assert.Equal(ExitCodes.ApiError, update.Exit);
        Assert.Contains("researcher-v2", update.Error, StringComparison.Ordinal);
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(live, "SKILL.md"), Ct));
    }

    /// <summary>
    /// An update leaves the role root holding role directories only, exactly as an install does. This is the
    /// verb that reaches the replace path in ordinary use — an install onto an empty root never renames
    /// anything — so it is the one that would leave a set-aside directory behind, to be reported as a role by
    /// the operation an operator asks whether this installation is ready.
    /// </summary>
    [Fact]
    public async Task An_update_leaves_the_role_root_holding_role_directories_only()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await RunAsync(dir, ["skills", "install", "--source", source, "--root", Harness(dir)]);
        Change(source, "roles/researcher", "a newer body");

        var update = await RunAsync(dir, ["skills", "update", "--root", Harness(dir)]);

        Assert.Equal(ExitCodes.Success, update.Exit);
        var deployed = Directory.GetDirectories(dir.Paths.RoleSkillsDirectory);
        Assert.NotEmpty(deployed);
        Assert.All(
            deployed,
            entry => Assert.True(
                File.Exists(Path.Combine(entry, "SKILL.md")),
                $"'{entry}' survived the update and is not a role directory."));
    }

    /// <summary>--dry-run carries over; --source and --ref do not exist on this verb at all.</summary>
    [Fact]
    public async Task Update_takes_a_dry_run_and_takes_no_source_or_ref()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        await RunAsync(dir, ["skills", "install", "--source", source, "--root", Harness(dir)]);
        Change(source, "operating", "a newer body");

        var dry = await RunAsync(dir, ["skills", "update", "--root", Harness(dir), "--dry-run"]);
        Assert.Equal(ExitCodes.Success, dry.Exit);
        Assert.DoesNotContain(
            "a newer body",
            await File.ReadAllTextAsync(Path.Combine(Harness(dir), "operating", "SKILL.md"), Ct),
            StringComparison.Ordinal);

        var refused = await RunAsync(dir, ["skills", "update", "--root", Harness(dir), "--source", source]);
        Assert.Equal(ExitCodes.Usage, refused.Exit);
    }

    /// <summary>
    /// An update carries a harness only where a record says a deployment was made there, so an installation
    /// made with <c>--roles-only</c> stays one.
    /// </summary>
    /// <remarks>
    /// Before, it wrote the interactive and business packs into every harness it could find: the first update
    /// after a roles-only install put into the agent's configuration exactly what the operator had declined.
    /// </remarks>
    [Fact]
    public async Task An_update_after_a_roles_only_install_writes_into_no_harness()
    {
        using var dir = new TempPaths();
        var source = Source(dir);
        var install = await RunAsync(dir, ["skills", "install", "--source", source, "--roles-only"]);
        Assert.Equal(ExitCodes.Success, install.Exit);

        Change(source, "roles/researcher", "a newer body");
        var update = await RunAsync(dir, ["skills", "update", "--root", Harness(dir)]);

        Assert.Equal(ExitCodes.Success, update.Exit);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Harness(dir)));
        Assert.Contains(
            "a newer body",
            await File.ReadAllTextAsync(Path.Combine(dir.Paths.RoleSkillsDirectory, "researcher", "SKILL.md"), Ct),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Each root is updated from its own record. After a full install from one source and a roles-only install
    /// from another, the harness still comes from the first and the role root from the second.
    /// </summary>
    /// <remarks>
    /// The update took the newest deployment anywhere and re-ran it everywhere: the harness was "updated" from the
    /// roles-only source, and its record rewritten to say it had always come from there.
    /// </remarks>
    [Fact]
    public async Task Each_root_is_updated_from_its_own_record()
    {
        using var dir = new TempPaths();
        var first = Source(dir, "first");
        var second = Source(dir, "second");
        Assert.Equal(ExitCodes.Success, (await RunAsync(dir, ["skills", "install", "--source", first, "--root", Harness(dir)])).Exit);
        Assert.Equal(ExitCodes.Success, (await RunAsync(dir, ["skills", "install", "--source", second, "--roles-only"])).Exit);

        Change(first, "operating", "the first source, moved on");
        Change(second, "operating", "the second source, which the harness never came from");
        Change(second, "roles/researcher", "the second source's role, moved on");
        var update = await RunAsync(dir, ["skills", "update", "--root", Harness(dir)]);

        Assert.Equal(ExitCodes.Success, update.Exit);
        Assert.Contains(
            "the first source, moved on",
            await File.ReadAllTextAsync(Path.Combine(Harness(dir), "operating", "SKILL.md"), Ct),
            StringComparison.Ordinal);
        Assert.Contains(
            "the second source's role, moved on",
            await File.ReadAllTextAsync(Path.Combine(dir.Paths.RoleSkillsDirectory, "researcher", "SKILL.md"), Ct),
            StringComparison.Ordinal);
        Assert.All(
            Jason.Contracts.Skills.SkillsRecord.Read(Harness(dir))!.Packs,
            deployment => Assert.Equal(first, deployment.Source));
    }

    /// <summary>
    /// A directory source is recorded as the full path, so an update finds it from anywhere. <c>--source .</c>
    /// was recorded as <c>.</c>, and the update worked only from the directory the install had been run in.
    /// </summary>
    [Fact]
    public async Task A_directory_source_is_recorded_as_a_full_path()
    {
        using var dir = new TempPaths();
        var source = Source(dir);

        // Relative to where this runs, where there is such a path. On a machine whose checkout and temporary
        // directory are on two drives there is none, and the source is spelled through `..` instead: a spelling
        // that is not the full path either, and was recorded as typed all the same.
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, source);
        var spelled = Path.IsPathRooted(relative) ? Path.Combine(source, "..", Path.GetFileName(source)) : relative;
        Assert.NotEqual(source, spelled);

        Assert.Equal(ExitCodes.Success, (await RunAsync(dir, ["skills", "install", "--source", spelled, "--root", Harness(dir)])).Exit);

        Assert.All(
            Jason.Contracts.Skills.SkillsRecord.Read(Harness(dir))!.Packs,
            deployment => Assert.Equal(source, deployment.Source));
        Assert.All(
            Jason.Contracts.Skills.SkillsRecord.Read(dir.Paths.RoleSkillsDirectory)!.Packs,
            deployment => Assert.Equal(source, deployment.Source));
    }

    private static string Harness(TempPaths dir) =>
        Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;

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

    private static string Source(TempPaths dir, string name = "source")
    {
        var root = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, name)).FullName;
        Skill(Path.Combine(root, "skills", "runtime", "operating"), "operating", "body");
        Skill(Path.Combine(root, "skills", "runtime", "roles", "researcher"), "researcher", "body");
        Skill(Path.Combine(root, "skills", "business", "campaign-planning"), "campaign-planning", "body");
        return root;
    }

    private static void Change(string source, string skill, string body) =>
        Skill(
            Path.Combine(source, "skills", skill.StartsWith("roles/", StringComparison.Ordinal) ? "runtime" : "runtime", skill.Replace('/', Path.DirectorySeparatorChar)),
            skill.Split('/')[^1],
            body);

    private static void Skill(string directory, string name, string body)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "SKILL.md"),
            $"---\nname: {name}\ndescription: one line\n---\n\n{body}\n");
        File.WriteAllText(Path.Combine(directory, "reference.md"), "where to look");
    }
}
