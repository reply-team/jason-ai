using System.Security.Cryptography;
using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Uninstall;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// The data directory is the operator's, and it goes only on the explicit word.
/// </summary>
/// <remarks>
/// It holds the database, the settings, the plugins, the logs and the work directories — the record of what
/// somebody did, which is not the installer's to decide about. The one thing inside it that does go by default
/// is the role skills root, because that is a deployment Jason made and recorded rather than the operator's
/// own work.
/// </remarks>
public class UninstallDataTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_the_flag_everything_but_the_role_skills_is_byte_identical_afterwards()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var before = Snapshot(dir.Paths.Root, except: dir.Paths.RoleSkillsDirectory);

        var (exit, _) = await RunAsync(dir, purgeData: false);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(before, Snapshot(dir.Paths.Root, except: dir.Paths.RoleSkillsDirectory));
    }

    /// <summary>And the verb says it kept it, and where. A person who wanted it gone has to learn it is not.</summary>
    [Fact]
    public async Task Without_the_flag_the_verb_says_it_kept_it_and_where()
    {
        using var dir = new TempPaths();
        Plant(dir);

        var (_, report) = await RunAsync(dir, purgeData: false);

        Assert.Contains(report.Kept, line => line.Contains(dir.Paths.Root, StringComparison.OrdinalIgnoreCase)
            && line.Contains("--purge-data", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_the_flag_and_yes_the_directory_is_gone()
    {
        using var dir = new TempPaths();
        Plant(dir);

        var (exit, report) = await RunAsync(dir, purgeData: true, yes: true);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(dir.Paths.Root));
        Assert.Contains(report.Done, line => line.Contains("Removed the data directory", StringComparison.Ordinal));
    }

    /// <summary>
    /// The machine shape refuses up front rather than reading a stream nobody is typing into — and it refuses
    /// with the whole installation still in place, not with everything but the data already gone.
    /// </summary>
    [Fact]
    public async Task The_machine_shape_refuses_purge_without_yes_and_removes_nothing_at_all()
    {
        using var dir = new TempPaths();
        var planted = Plant(dir);
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(
            ["uninstall", "--purge-data"],
            Machine(dir, output, new RecordingRemover { ReallyRemoves = true }),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.UninstallRefused, output.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(planted), "a refusal removed something anyway.");
        Assert.True(Directory.Exists(dir.Paths.Root));
    }

    /// <summary>The person's shape prints what will be deleted and asks; "n" keeps it and the rest is done.</summary>
    [Fact]
    public async Task The_human_shape_asks_before_purging_and_a_no_keeps_it()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var output = new StringWriter();
        var env = Machine(dir, output, new RecordingRemover { ReallyRemoves = true }) with { In = new StringReader("n\n") };

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: true, DryRun: false, PurgeData: true, Yes: false, Force: false),
            Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(Directory.Exists(dir.Paths.Root), "an unconfirmed purge deleted the data directory.");

        var printed = output.ToString();
        Assert.Contains("About to delete", printed, StringComparison.Ordinal);
        Assert.Contains("config/", printed, StringComparison.Ordinal);
        Assert.Contains("you did not confirm", printed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task And_a_yes_at_the_prompt_removes_it()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var env = Machine(dir, new StringWriter(), new RecordingRemover { ReallyRemoves = true }) with { In = new StringReader("y\n") };

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: true, DryRun: false, PurgeData: true, Yes: false, Force: false),
            Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(dir.Paths.Root));
    }

    /// <summary>
    /// The question is asked before anything at all is removed, not after everything else has gone.
    /// </summary>
    /// <remarks>
    /// It used to be the last step, so a person who stopped at the prompt to think was looking at a machine
    /// already half uninstalled — and on Windows the answer was read by a process whose own file had already
    /// been moved aside, which is the state a single-file build can no longer load anything new in.
    /// </remarks>
    [Fact]
    public async Task The_question_is_asked_before_anything_is_removed()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var remover = new RecordingRemover { ReallyRemoves = true };
        var answer = new Answering("y", () => remover.Calls.Count);
        var env = Machine(dir, new StringWriter(), remover) with { In = answer };

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: true, DryRun: false, PurgeData: true, Yes: false, Force: false),
            Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(0, answer.RemovedBeforeAsked);
        Assert.False(Directory.Exists(dir.Paths.Root));
    }

    /// <summary>An answer, and how much had been removed at the moment it was asked for.</summary>
    private sealed class Answering(string answer, Func<int> removedSoFar) : TextReader
    {
        public int? RemovedBeforeAsked { get; private set; }

        public override string? ReadLine()
        {
            RemovedBeforeAsked ??= removedSoFar();
            return answer;
        }
    }

    /// <summary>Nothing on the stream at all is not a yes. The default is the safe one.</summary>
    [Fact]
    public async Task An_empty_answer_keeps_it()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var env = Machine(dir, new StringWriter(), new RecordingRemover { ReallyRemoves = true }) with { In = new StringReader(string.Empty) };

        await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: true, DryRun: false, PurgeData: true, Yes: false, Force: false),
            Ct);

        Assert.True(Directory.Exists(dir.Paths.Root));
    }

    /// <summary>A file in each of the places the data directory is documented to hold the operator's own work.</summary>
    private static string Plant(TempPaths dir)
    {
        Directory.CreateDirectory(dir.Paths.ConfigDirectory);
        File.WriteAllText(dir.Paths.UserSettingsFile, "{}");
        Directory.CreateDirectory(dir.Paths.PluginsDirectory);
        File.WriteAllText(Path.Combine(dir.Paths.PluginsDirectory, "notes.txt"), "mine");
        Directory.CreateDirectory(dir.Paths.StateDirectory);
        File.WriteAllText(dir.Paths.DatabaseFile, "not really a database");
        return dir.Paths.UserSettingsFile;
    }

    /// <summary>Every file under a root by relative path and digest, so "unchanged" means the bytes.</summary>
    private static IReadOnlyList<string> Snapshot(string root, string except)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(file => !file.StartsWith(except, StringComparison.OrdinalIgnoreCase))
                .Select(file => $"{Path.GetRelativePath(root, file)} {Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file)))}")
                .Order(StringComparer.Ordinal),
        ];
    }

    private static CliEnvironment Machine(TempPaths dir, TextWriter output, RecordingRemover remover) =>
        new(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            InstallPath: Path.Combine(dir.Paths.Root, "bin", "jason"),
            Removes: remover);

    private static async Task<(int Exit, UninstallReport Report)> RunAsync(TempPaths dir, bool purgeData, bool yes = false)
    {
        // A role skill with a record, so the one thing inside the data directory that does go is exercised.
        var roles = dir.Paths.RoleSkillsDirectory;
        var skill = Path.Combine(roles, "researcher", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(skill)!);
        File.WriteAllText(skill, "---\nname: researcher\n---\n");
        SkillsRecord.Write(roles, new SkillsRecord(
            SkillsRecord.CurrentVersion,
            [new SkillsDeployment("runtime-roles", "/somewhere", "v0.1.0", false, null, DateTimeOffset.UtcNow,
                [new DeployedFile("researcher/SKILL.md", SkillsRecord.Digest(skill))])]));

        var output = new StringWriter();
        var exit = await UninstallCommand.RunAsync(
            Machine(dir, output, new RecordingRemover { ReallyRemoves = true }),
            new UninstallOptions(Human: false, DryRun: false, PurgeData: purgeData, Yes: yes, Force: false),
            Ct);

        return (exit, JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!);
    }
}
