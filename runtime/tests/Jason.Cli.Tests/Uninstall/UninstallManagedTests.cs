using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Uninstall;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// <c>jason uninstall</c> on an installation a package manager owns. It takes off what is Jason's own — the logon
/// registration, the skills by receipt, the data directory on the word — and leaves the executable, its directory and the
/// PATH entry that reaches it to the manager, naming the command that takes them off.
/// </summary>
/// <remarks>
/// The winget facts rest on the record of a real winget install that <see cref="ManagedLayouts"/> states; the Homebrew
/// facts on a planted keg.
/// </remarks>
public sealed class UninstallManagedTests : IDisposable
{
    private readonly ManagedLayouts _layouts = new();
    private readonly TempPaths _dir = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _dir.Dispose();
        _layouts.Dispose();
    }

    [Theory]
    [InlineData("Homebrew", "brew uninstall jason")]
    [InlineData("winget", "winget uninstall Reply.Jason")]
    public async Task It_takes_off_what_is_jasons_and_leaves_the_rest_to_the_manager(string manager, string uninstall)
    {
        var (env, executable, output, registrar, remover) = Managed(manager);
        var skill = Deploy(Path.Combine(_dir.Paths.Root, "harness"));

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(Human: false, DryRun: false, PurgeData: false, Yes: true, Force: false), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var report = JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;

        // Jason's own: the registration and the recorded skill.
        Assert.Equal(1, registrar.Removals);
        Assert.False(File.Exists(skill), "a recorded skill was left behind");

        // The manager's: never asked for, and still there.
        Assert.True(File.Exists(executable), "the manager's executable was removed");
        Assert.True(Directory.Exists(Path.GetDirectoryName(executable)), "the manager's directory was removed");
        Assert.DoesNotContain(remover.Calls, call => call.StartsWith("executable.remove", StringComparison.Ordinal));
        Assert.DoesNotContain(remover.Calls, call => call.StartsWith("path.remove", StringComparison.Ordinal));
        Assert.DoesNotContain(remover.Calls, call => call.StartsWith("extracted.remove", StringComparison.Ordinal));
        Assert.DoesNotContain(remover.Calls, call => call.Contains(Path.GetDirectoryName(executable)!, StringComparison.OrdinalIgnoreCase));

        // And the report says whose they are and what takes them off.
        var left = Assert.Single(report.Kept, line => line.Contains($"Left to {manager}", StringComparison.Ordinal));
        Assert.Contains(executable, left, StringComparison.Ordinal);
        Assert.Contains($"`{uninstall}`", left, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Kept, line => line.Contains("not a published single file", StringComparison.Ordinal));
        Assert.DoesNotContain(report.Kept, line => line.Contains("no PATH entry was looked for", StringComparison.Ordinal));
    }

    /// <summary>
    /// The plan a person reads before anything happens names what is left to the manager, the command that takes it off,
    /// and that the runtime is stopped first, as it always is: never the executable under what is removed.
    /// </summary>
    [Theory]
    [InlineData("Homebrew", "brew uninstall jason")]
    [InlineData("winget", "winget uninstall Reply.Jason")]
    public async Task The_plan_says_what_it_leaves_to_the_manager_and_that_the_runtime_is_stopped_first(string manager, string uninstall)
    {
        var (env, executable, output, _, remover) = Managed(manager);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(Human: true, DryRun: true, PurgeData: false, Yes: false, Force: false), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var text = output.ToString();
        Assert.Contains($"Left to {manager}", text, StringComparison.Ordinal);
        Assert.Contains($"`{uninstall}`", text, StringComparison.Ordinal);
        Assert.Contains("stops the runtime first", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"the executable at {executable}", text, StringComparison.Ordinal);
        Assert.DoesNotContain("off the PATH", text, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
    }

    /// <summary>The plan an agent reads carries the same: who owns what is left, and the command, as one field.</summary>
    [Theory]
    [InlineData("Homebrew", "brew uninstall jason")]
    [InlineData("winget", "winget uninstall Reply.Jason")]
    public async Task The_plan_an_agent_reads_names_the_manager_and_its_command(string manager, string uninstall)
    {
        var (env, executable, output, _, _) = Managed(manager);

        await UninstallCommand.RunAsync(env, new UninstallOptions(Human: false, DryRun: true, PurgeData: false, Yes: false, Force: false), Ct);

        var plan = JsonNode.Parse(output.ToString())?["plan"];
        Assert.Equal(manager, (string?)plan?["left_to"]?["manager"]);
        Assert.Equal(uninstall, (string?)plan?["left_to"]?["uninstall_command"]);
        Assert.Equal(executable, (string?)plan?["left_to"]?["executable"]);
        Assert.Null(plan?["executable"]);
        Assert.Null(plan?["path_entry"]);
    }

    /// <summary>The data directory is Jason's: on the word, it goes, and the manager's file stays.</summary>
    [Fact]
    public async Task The_data_directory_goes_on_the_word_and_the_managers_file_stays()
    {
        var (env, executable, output, _, _) = Managed("Homebrew");
        Directory.CreateDirectory(_dir.Paths.StateDirectory);
        File.WriteAllText(_dir.Paths.DatabaseFile, "work");

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(Human: false, DryRun: false, PurgeData: true, Yes: true, Force: false), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(File.Exists(_dir.Paths.DatabaseFile), output.ToString());
        Assert.True(File.Exists(executable), "the manager's executable went with the data directory");
    }

    /// <summary>
    /// An installation a manager owns, named as the file it runs as, with the manager's record beside it — and a PATH entry
    /// that looks like the installer's own and native libraries this build unpacked, so that leaving them alone is the
    /// verb's decision and not the machine's.
    /// </summary>
    private (CliEnvironment Env, string Executable, StringWriter Output, RecordingRegistrar Registrar, RecordingRemover Remover) Managed(string manager)
    {
        string executable;
        CliEnvironment env;
        var output = new StringWriter();
        var registrar = new RecordingRegistrar { State = new Jason.Cli.Autostart.AutostartState(true, ["jason", "runtime", "run"], null) };

        if (manager == "Homebrew")
        {
            executable = _layouts.Homebrew().Executable;
            env = Environment(output, registrar, executable, new PlantedRecords());
        }
        else
        {
            var winget = _layouts.Winget();
            executable = winget.Executable;
            env = Environment(output, registrar, executable, new PlantedRecords(winget.Registration()));
        }

        var remover = new RecordingRemover
        {
            ReallyRemoves = true,
            PathEntry = new PathEntryPlan(Path.GetDirectoryName(executable)!, [], "the installer's", Ours: true),
            ExtractedLibraries = Directory.CreateDirectory(Path.Combine(_layouts.Root, ".net", "jason", "abc")).FullName,
        };

        return (env with { Removes = remover }, executable, output, registrar, remover);
    }

    private CliEnvironment Environment(StringWriter output, RecordingRegistrar registrar, string executable, PlantedRecords records) =>
        new(
            output,
            new StringWriter(),
            _dir.Paths,
            Autostart: registrar,
            Harnesses: HarnessLocators.At(Path.Combine(_dir.Paths.Root, "harness")),
            InstallPath: executable,
            Managers: records);

    /// <summary>A recorded skill in a harness root, as <c>jason skills install</c> leaves one; returns the file.</summary>
    private static string Deploy(string root)
    {
        var skill = Path.Combine(root, "researcher", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(skill)!);
        File.WriteAllText(skill, "---\nname: researcher\n---\n");
        SkillsRecord.Write(root, new SkillsRecord(
            SkillsRecord.CurrentVersion,
            [new SkillsDeployment("jason-runtime-skills", "/somewhere", "v0.1.0", false, null, DateTimeOffset.UnixEpoch,
                [new DeployedFile("researcher/SKILL.md", SkillsRecord.Digest(skill))])]));
        return skill;
    }
}
