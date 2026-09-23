using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Uninstall;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// What an uninstall says it will do, composed from receipts and read before anything is touched.
/// </summary>
/// <remarks>
/// A plan nobody saw is one nobody could have stopped, and a plan that guessed at a root it could not read is
/// one that removes nothing and reports success.
/// </remarks>
public class UninstallPlanTests
{
    [Fact]
    public void Only_files_a_receipt_names_are_in_the_plan()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        var mine = Deploy(root, "operating-the-installation");
        var theirs = Write(Path.Combine(root, "someone-elses-skill", "SKILL.md"), "# not ours");

        Record(root, ("runtime", [Relative(root, mine)]));

        var plan = UninstallReader.Read(Machine(dir, root), purgeData: false);

        var planned = plan.Roots.SelectMany(candidate => candidate.Files).Select(file => file.Path).ToList();
        Assert.Contains(planned, path => string.Equals(path, mine, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(planned, path => path.Contains("someone-elses-skill", StringComparison.Ordinal));
        Assert.True(File.Exists(theirs), "reading a plan is not supposed to touch anything.");
    }

    /// <summary>
    /// And a root whose record cannot be read is <c>unknown</c> rather than empty. Empty means "Jason put
    /// nothing here", which is the opposite fact and would have this verb remove nothing and report success.
    /// </summary>
    [Fact]
    public void A_root_whose_record_cannot_be_read_is_unknown_and_nothing_is_planned_for_it()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, SkillsRecord.FileName), "{ this is not json");

        var plan = UninstallReader.Read(Machine(dir, root), purgeData: false);

        var unknown = Assert.Single(plan.Unknown);
        Assert.Equal(Path.GetFullPath(root), Path.GetFullPath(unknown.Root));
        Assert.DoesNotContain(plan.Roots, candidate => Same(candidate.Root, root));
    }

    /// <summary>A root Jason never wrote into is neither planned for nor unknown. It is simply not ours.</summary>
    [Fact]
    public void A_root_with_no_record_at_all_is_not_in_the_plan()
    {
        using var dir = new TempPaths();
        var root = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;
        Write(Path.Combine(root, "someone-elses-skill", "SKILL.md"), "# not ours");

        var plan = UninstallReader.Read(Machine(dir, root), purgeData: false);

        Assert.Empty(plan.Roots);
        Assert.Empty(plan.Unknown);
    }

    /// <summary>
    /// The runtime's own role root is read as well as the harnesses. It is a root a deployment wrote into and
    /// carries a record of its own, and it is the half the documents call not optional.
    /// </summary>
    [Fact]
    public void The_role_skills_root_is_a_root_this_verb_reads()
    {
        using var dir = new TempPaths();
        var roles = dir.Paths.RoleSkillsDirectory;
        var skill = Deploy(roles, "researcher");
        Record(roles, ("runtime-roles", [Relative(roles, skill)]));

        var plan = UninstallReader.Read(Machine(dir, Path.Combine(dir.Paths.Root, "harness")), purgeData: false);

        var root = Assert.Single(plan.Roots, candidate => Same(candidate.Root, roles));
        Assert.Contains(root.Files, file => string.Equals(file.Path, skill, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A file whose bytes are no longer the bytes the record names is somebody's own edit, and the plan says
    /// so before anything decides what to do about it.
    /// </summary>
    [Fact]
    public void A_file_that_no_longer_digests_to_what_the_record_says_is_marked_edited()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        var mine = Deploy(root, "operating-the-installation");
        Record(root, ("runtime", [Relative(root, mine)]));
        File.WriteAllText(mine, "# I changed this");

        var plan = UninstallReader.Read(Machine(dir, root), purgeData: false);

        var file = Assert.Single(plan.Roots.SelectMany(candidate => candidate.Files));
        Assert.True(file.Present);
        Assert.True(file.Edited, "the bytes on disk are not the bytes the record names, and the plan says they are.");
    }

    /// <summary>A recorded file somebody already deleted is absent, not edited: there is nothing to keep.</summary>
    [Fact]
    public void A_recorded_file_that_is_already_gone_is_absent_and_not_edited()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        var mine = Deploy(root, "operating-the-installation");
        Record(root, ("runtime", [Relative(root, mine)]));
        File.Delete(mine);

        var plan = UninstallReader.Read(Machine(dir, root), purgeData: false);

        var file = Assert.Single(plan.Roots.SelectMany(candidate => candidate.Files));
        Assert.False(file.Present);
        Assert.False(file.Edited);
    }

    /// <summary>Without the word, the data directory is named as kept rather than planned for removal.</summary>
    [Fact]
    public void The_data_directory_is_kept_unless_the_word_is_said()
    {
        using var dir = new TempPaths();
        var harness = Path.Combine(dir.Paths.Root, "harness");

        Assert.False(UninstallReader.Read(Machine(dir, harness), purgeData: false).PurgesData);
        Assert.True(UninstallReader.Read(Machine(dir, harness), purgeData: true).PurgesData);
    }

    /// <summary>And the plan is printed in every mode, not only under --dry-run.</summary>
    [Fact]
    public async Task The_plan_is_printed_before_anything_is_removed()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        var mine = Deploy(root, "operating-the-installation");
        Record(root, ("runtime", [Relative(root, mine)]));

        var output = new StringWriter();
        var remover = new RecordingRemover();

        var exit = await CliApp.RunAsync(
            ["uninstall", "--dry-run"],
            Machine(dir, root, output, remover),
            TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);

        // Read back as the document it is rather than as a substring: the default shape is JSON, where a
        // Windows path is spelled with escaped separators, and a test matching the raw path would be
        // asserting about the escaping rather than about the plan.
        var printed = JsonSerializer.Deserialize<UninstallPlan>(output.ToString(), JasonJson.Options);
        Assert.NotNull(printed);
        Assert.Contains(printed.Roots, candidate => Same(candidate.Root, root));

        Assert.Empty(remover.Calls);
        Assert.True(File.Exists(mine), "--dry-run removed a file.");
    }

    private static bool Same(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

    private static string Deploy(string root, string skill) =>
        Write(Path.Combine(root, skill, "SKILL.md"), $"---\nname: {skill}\n---\n");

    private static string Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static void Record(string root, params (string Pack, string[] Files)[] packs) =>
        SkillsRecord.Write(root, new SkillsRecord(
            SkillsRecord.CurrentVersion,
            [.. packs.Select(pack => new SkillsDeployment(
                pack.Pack,
                "/somewhere",
                "v0.1.0",
                false,
                null,
                DateTimeOffset.UtcNow,
                [.. pack.Files.Select(file => new DeployedFile(
                    file,
                    SkillsRecord.Digest(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar)))))]))]));

    private static CliEnvironment Machine(TempPaths dir, string harnessRoot, TextWriter? output = null, IInstallationRemover? remover = null) =>
        new(
            output ?? new StringWriter(),
            new StringWriter(),
            dir.Paths,
            Harnesses: HarnessLocators.At(harnessRoot),
            Removes: remover ?? new RecordingRemover(),
            InstallPath: Path.Combine(dir.Paths.Root, "bin", "jason"),
            SearchPath: Path.Combine(dir.Paths.Root, "bin"));
}
