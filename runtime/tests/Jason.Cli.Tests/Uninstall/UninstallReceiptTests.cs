using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Uninstall;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// What an uninstall removes, and the four kinds of file it does not.
/// </summary>
/// <remarks>
/// A verb that deletes by pattern will eventually delete somebody's own file; a verb that deletes by receipt
/// cannot. Each of the things that must survive is planted here on purpose rather than assumed absent.
/// </remarks>
public class UninstallReceiptTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Exactly_the_recorded_paths_go_and_the_neighbours_stay()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        var ours = Deploy(root, "operating-the-installation");
        var theirSkill = Write(Path.Combine(root, "someone-elses-skill", "SKILL.md"), "# not ours");
        Record(root, "runtime", ours);

        var (exit, _) = await RunAsync(dir, root);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(ours), "a recorded file survived.");
        Assert.True(File.Exists(theirSkill), "a skill nobody recorded was removed.");
        Assert.True(Directory.Exists(root), "the root holding somebody else's skill was removed.");
    }

    /// <summary>
    /// A file the operator put inside one of Jason's own skill directories keeps that directory. Somebody put
    /// it there on purpose, and an uninstall is not the moment to decide it did not matter.
    /// </summary>
    [Fact]
    public async Task A_directory_holding_something_else_is_kept_and_reported()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        var ours = Deploy(root, "operating-the-installation");
        var theirs = Write(Path.Combine(root, "operating-the-installation", "my-notes.md"), "mine");
        Record(root, "runtime", ours);

        var (exit, report) = await RunAsync(dir, root);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(ours));
        Assert.True(File.Exists(theirs), "a file the installer never wrote was removed with the directory.");
        Assert.Contains(report.Kept, line => line.Contains("my-notes", StringComparison.Ordinal)
            || line.Contains("operating-the-installation", StringComparison.Ordinal));
    }

    /// <summary>And a directory nothing is left in goes, along with the record and the root itself.</summary>
    [Fact]
    public async Task An_empty_directory_goes_and_so_does_the_record()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        var ours = Deploy(root, "operating-the-installation");
        Record(root, "runtime", ours);

        var (exit, _) = await RunAsync(dir, root);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(Path.Combine(root, SkillsRecord.FileName)), "the record survived its own files.");
        Assert.False(Directory.Exists(root), "an empty root was left behind.");
    }

    /// <summary>
    /// The receipt says what Jason wrote; a digest that no longer matches says somebody else wrote it since.
    /// Removing it would be the data-loss bug the installer refuses to be, with no undo.
    /// </summary>
    [Fact]
    public async Task A_file_whose_bytes_are_not_the_bytes_the_record_names_is_reported_and_kept()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        var ours = Deploy(root, "operating-the-installation");
        Record(root, "runtime", ours);
        File.WriteAllText(ours, "# I changed this");

        var (exit, report) = await RunAsync(dir, root);

        // Kept, not failed: a report is the answer, and the operator decides.
        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(File.Exists(ours), "an edit was removed without --force.");
        Assert.Contains(report.Kept, line => line.Contains("--force", StringComparison.Ordinal));
    }

    /// <summary>
    /// And the record stays with it. A receipt removed while the file it names is still on disk leaves a file
    /// nothing can ever account for again.
    /// </summary>
    [Fact]
    public async Task The_record_stays_while_it_still_names_a_file_that_is_there()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        var ours = Deploy(root, "operating-the-installation");
        Record(root, "runtime", ours);
        File.WriteAllText(ours, "# I changed this");

        var (_, report) = await RunAsync(dir, root);

        Assert.True(File.Exists(Path.Combine(root, SkillsRecord.FileName)));
        Assert.Contains(report.Kept, line => line.Contains("Kept the record", StringComparison.Ordinal));
    }

    [Fact]
    public async Task And_force_removes_it()
    {
        using var dir = new TempPaths();
        var root = Path.Combine(dir.Paths.Root, "harness");
        var ours = Deploy(root, "operating-the-installation");
        Record(root, "runtime", ours);
        File.WriteAllText(ours, "# I changed this");

        var (exit, _) = await RunAsync(dir, root, force: true);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(ours));
    }

    /// <summary>
    /// The role skills live inside the data directory and go anyway: they are a deployment Jason made and
    /// recorded, not the operator's own work. Everything else under the data directory stays.
    /// </summary>
    [Fact]
    public async Task Role_skills_are_removed_by_receipt_although_they_live_in_the_data_directory()
    {
        using var dir = new TempPaths();
        var roles = dir.Paths.RoleSkillsDirectory;
        var skill = Deploy(roles, "researcher");
        Record(roles, "runtime-roles", skill);

        var settings = Write(dir.Paths.UserSettingsFile, "{}");
        var staged = Write(Path.Combine(dir.Paths.SkillsStagedSourcesDirectory, "v0.1.0", "README.md"), "a clone");

        var (exit, _) = await RunAsync(dir, Path.Combine(dir.Paths.Root, "harness"));

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(skill), "a recorded role skill survived.");
        Assert.True(File.Exists(settings), "the operator's settings were removed without --purge-data.");
        Assert.True(File.Exists(staged), "a staged source was removed without --purge-data.");
        Assert.True(Directory.Exists(dir.Paths.Root), "the data directory was removed without --purge-data.");
    }

    /// <summary>
    /// A root reported unknown is not guessed at: nothing there is removed, the verb says so and exits 1.
    /// Removing what this build recognises and reporting a clean uninstall is how the rest becomes nobody's.
    /// </summary>
    [Fact]
    public async Task An_unreadable_record_removes_nothing_from_that_root_and_is_reported()
    {
        using var dir = new TempPaths();
        var root = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;
        var skill = Deploy(root, "operating-the-installation");
        File.WriteAllText(Path.Combine(root, SkillsRecord.FileName), "{ this is not json");

        var (exit, report) = await RunAsync(dir, root);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(skill), "a root nothing could read had something removed from it anyway.");
        Assert.Contains(report.Problems, problem => problem.Contains(root, StringComparison.OrdinalIgnoreCase));
        Assert.False(report.Completed);
    }

    private static async Task<(int Exit, UninstallReport Report)> RunAsync(TempPaths dir, string harnessRoot, bool force = false)
    {
        var output = new StringWriter();
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(harnessRoot),
            InstallPath: Path.Combine(dir.Paths.Root, "bin", "jason"),
            Removes: new RecordingRemover { ReallyRemoves = true });

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: true, Force: force),
            Ct);

        return (exit, JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!);
    }

    /// <summary>One skill in a root, and the record naming it: a deployment as the installer leaves one.</summary>
    internal static string Recorded(string root, string skill)
    {
        var file = Deploy(root, skill);
        Record(root, "jason-runtime-skills", file);
        return file;
    }

    private static string Deploy(string root, string skill) =>
        Write(Path.Combine(root, skill, "SKILL.md"), $"---\nname: {skill}\n---\n");

    private static string Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static void Record(string root, string pack, params string[] files) =>
        SkillsRecord.Write(root, new SkillsRecord(
            SkillsRecord.CurrentVersion,
            [new SkillsDeployment(
                pack,
                "/somewhere",
                "v0.1.0",
                false,
                null,
                DateTimeOffset.UtcNow,
                [.. files.Select(file => new DeployedFile(
                    Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/'),
                    SkillsRecord.Digest(file)))])]));
}
