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

    /// <summary>
    /// A dry run is not asked for --yes: it deletes nothing, so there is nothing to have said in advance, and a
    /// script that wants to look before it purges is exactly who runs one.
    /// </summary>
    [Fact]
    public async Task A_dry_run_of_the_purge_needs_no_yes_and_removes_nothing()
    {
        using var dir = new TempPaths();
        var planted = Plant(dir);
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true };

        var exit = await CliApp.RunAsync(["uninstall", "--purge-data", "--dry-run"], Machine(dir, output, remover), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(remover.Calls);
        Assert.True(File.Exists(planted));
        Assert.True(JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!.Plan.PurgesData);
    }

    /// <summary>
    /// The person's shape prints what will be deleted and asks — and a no changes nothing at all, and says so
    /// with exit 1.
    /// </summary>
    /// <remarks>
    /// A no used to keep the data directory and remove everything else, exiting 0. The question was whether to
    /// delete this installation's data along with the rest of it, and somebody who says no to that has not said
    /// yes to the rest.
    /// </remarks>
    [Fact]
    public async Task The_human_shape_asks_before_purging_and_a_no_changes_nothing()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var output = new StringWriter();
        var error = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true };
        var env = Machine(dir, output, remover) with { In = new StringReader("n\n"), Error = error };

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: true, DryRun: false, PurgeData: true, Yes: false, Force: false),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(remover.Calls);
        Assert.True(Directory.Exists(dir.Paths.Root), "an unconfirmed purge deleted the data directory.");

        var printed = output.ToString();
        Assert.Contains("About to delete what Jason keeps in", printed, StringComparison.Ordinal);
        Assert.Contains("config/", printed, StringComparison.Ordinal);
        Assert.Contains("nothing at all was removed", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// And nothing means nothing that is not a file either: the registration stays registered and the runtime is
    /// never asked to stop.
    /// </summary>
    /// <remarks>
    /// The test above counts the remover's calls, and steps 1 and 2 make none. A question moved to after them —
    /// the registration taken and the runtime stopped before anybody said no — removed no file and stayed green.
    /// </remarks>
    [Fact]
    public async Task A_no_leaves_the_registration_registered_and_the_runtime_running()
    {
        using var dir = new TempPaths();
        Plant(dir);
        dir.WriteDescriptor(Commands.RuntimeVerbs.Descriptor("rt_LIVE"));
        var registrar = new RecordingRegistrar();
        var asked = new List<string>();
        var processes = new Process.FakeProcessControl();
        processes.RunningPids.Add(Commands.RuntimeVerbs.Pid);
        var remover = new RecordingRemover { ReallyRemoves = true };
        var env = Machine(dir, new StringWriter(), remover) with
        {
            In = new StringReader("n\n"),
            Autostart = registrar,
            Processes = processes,
            HttpHandler = new FakeHandler(request =>
            {
                asked.Add(request.RequestUri!.AbsolutePath);
                return Commands.RuntimeVerbs.Response(System.Net.HttpStatusCode.OK, Commands.RuntimeVerbs.ShutdownJson("rt_LIVE"));
            }),
        };

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: true, DryRun: false, PurgeData: true, Yes: false, Force: false),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(0, registrar.Removals);
        Assert.Empty(asked);
        Assert.Empty(remover.Calls);
        Assert.True(File.Exists(dir.Paths.DescriptorFile), "the runtime's descriptor went, so something stopped it.");
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

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: true, DryRun: false, PurgeData: true, Yes: false, Force: false),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(Directory.Exists(dir.Paths.Root));
    }

    /// <summary>
    /// The purge removes what Jason keeps in the data directory, and nothing else. <c>JASON_DATA_DIR</c> may name
    /// any directory, and the purge used to delete the one it named with everything in it; somebody's own file
    /// beside Jason's is theirs, and the directory holding it stays and is named.
    /// </summary>
    [Fact]
    public async Task The_purge_removes_what_jason_keeps_there_and_keeps_what_it_did_not_put_there()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var notes = Path.Combine(dir.Paths.Root, "my-notes.txt");
        File.WriteAllText(notes, "mine");
        var photos = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "photos")).FullName;
        File.WriteAllText(Path.Combine(photos, "one.jpg"), "mine too");

        var (exit, report) = await RunAsync(dir, purgeData: true, yes: true);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(File.Exists(notes), "the purge removed a file Jason did not put there.");
        Assert.True(File.Exists(Path.Combine(photos, "one.jpg")), "the purge removed a directory Jason did not put there.");
        foreach (var own in Contracts.Discovery.JasonPaths.OwnEntries)
        {
            Assert.False(Path.Exists(Path.Combine(dir.Paths.Root, own)), $"'{own}' is still in the data directory.");
        }

        var line = Assert.Single(report.Kept, line => line.Contains("my-notes.txt", StringComparison.Ordinal));
        Assert.Contains("photos", line, StringComparison.Ordinal);
    }

    /// <summary>
    /// And where one of Jason's own entries could not be removed, the line about what was kept does not say that
    /// everything Jason keeps there is gone: the entry is a problem, and the verb exits 1 for it.
    /// </summary>
    [Fact]
    public async Task A_purge_that_could_not_remove_an_entry_does_not_say_everything_is_gone()
    {
        using var dir = new TempPaths();
        Plant(dir);
        File.WriteAllText(Path.Combine(dir.Paths.Root, "my-notes.txt"), "mine");
        var remover = new RecordingRemover { ReallyRemoves = true };
        remover.Locked.Add(dir.Paths.ConfigDirectory);
        var output = new StringWriter();

        var exit = await UninstallCommand.RunAsync(
            Machine(dir, output, remover),
            new UninstallOptions(Human: false, DryRun: false, PurgeData: true, Yes: true, Force: false),
            Ct);

        var report = JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;
        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(report.Problems, line => line.Contains(dir.Paths.ConfigDirectory, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(report.Kept, line => line.Contains("is gone", StringComparison.Ordinal));
        Assert.Contains(report.Kept, line => line.Contains("my-notes.txt", StringComparison.Ordinal));
    }

    /// <summary>
    /// A directory that is not a data directory is refused before anything is read, let alone removed: here, one
    /// holding the installation. The rest of the belt's cases are <c>DataDirectoryBeltTests</c>.
    /// </summary>
    [Fact]
    public async Task A_purge_of_a_directory_holding_the_installation_is_refused_before_anything_is_removed()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true };
        var env = Machine(dir, output, remover) with { InstallPath = Path.Combine(dir.Paths.Root, "bin", "jason") };

        var exit = await UninstallCommand.RunAsync(
            env,
            new UninstallOptions(Human: false, DryRun: false, PurgeData: true, Yes: true, Force: false),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.UninstallRefused, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("holds the installation", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.Empty(remover.Reads);
        Assert.True(File.Exists(dir.Paths.UserSettingsFile));
    }

    /// <summary>
    /// Every path this product composes under the data directory begins with one of the names the purge
    /// removes. A directory added to the layout and not to that list would be left behind by every purge; this
    /// is where that arrives instead.
    /// </summary>
    [Fact]
    public void Every_path_the_product_composes_under_the_data_directory_is_one_of_its_own_entries()
    {
        var paths = new Contracts.Discovery.JasonPaths(Path.Combine(Path.GetTempPath(), "jason-own-entries"));
        var composed = new List<string>();

        foreach (var property in typeof(Contracts.Discovery.JasonPaths).GetProperties().Where(property => property.PropertyType == typeof(string) && property.Name != nameof(paths.Root)))
        {
            composed.Add((string)property.GetValue(paths)!);
        }

        composed.AddRange(paths.Layout);
        composed.Add(paths.AttemptWorkDirectory("wi_X", "att_X"));
        composed.Add(paths.PluginPackageDirectory("some.plugin"));
        composed.Add(paths.PluginInvocationDirectory("inv_X"));
        composed.Add(paths.SkillsStagedSourceDirectory("v0.1.0"));

        var update = new Cli.Update.UpdatePaths(paths);
        composed.AddRange(typeof(Cli.Update.UpdatePaths).GetProperties().Where(property => property.PropertyType == typeof(string)).Select(property => (string)property.GetValue(update)!));

        composed.Add(Cli.Autostart.AutostartArtifacts.ArtifactPath(Cli.Autostart.AutostartPlatform.Windows, Path.GetTempPath(), paths.Root));

        Assert.True(composed.Count > 20, "the reflection above found almost nothing, so it is asserting nothing.");
        foreach (var path in composed)
        {
            var first = Path.GetRelativePath(paths.Root, path).Split(Path.DirectorySeparatorChar)[0];
            Assert.Contains(first, Contracts.Discovery.JasonPaths.OwnEntries);
        }
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

    /// <remarks>
    /// The installation beside the data directory rather than in it: a data directory holding the installation
    /// is one the purge refuses outright, which is a test of its own above.
    /// </remarks>
    private static CliEnvironment Machine(TempPaths dir, TextWriter output, RecordingRemover remover) =>
        new(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            InstallPath: Path.Combine(dir.Paths.Root + "-install", "jason"),
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
