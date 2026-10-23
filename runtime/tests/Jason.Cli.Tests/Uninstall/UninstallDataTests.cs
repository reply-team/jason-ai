using System.Security.Cryptography;
using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Uninstall;
using Jason.Contracts.Harness;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;
using Jason.Runtime.Tests;

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
    [Critical(Family.I3, "the data directory is kept")]
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
    [Critical(Family.I3, "--purge-data needs --yes")]
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
    /// And a data directory that is a folder of the harness root — <c>JASON_DATA_DIR</c> pointed at the root, a campaign
    /// in it called "Logs" — is refused before anything is read, saying why and that nothing was removed: the root and its
    /// folders are the person's, and the purge empties a data directory, never the root.
    /// </summary>
    [Fact]
    public async Task A_purge_of_a_folder_of_the_harness_root_is_refused_before_anything_is_removed()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var campaign = Path.Combine(dir.Paths.Root, "logs");
        foreach (var (folder, marker) in new[] { (dir.Paths.Root, new HarnessMarker(HarnessMarker.Root, null)), (campaign, new HarnessMarker(HarnessMarker.Campaign, "cmp_01JB0000000000000000000000")) })
        {
            Directory.CreateDirectory(Path.Combine(folder, HarnessMarker.Directory));
            File.WriteAllBytes(HarnessMarker.PathIn(folder), marker.Bytes());
        }

        File.WriteAllText(Path.Combine(campaign, "AGENTS.md"), "Write to them plainly.\n");
        var before = Snapshot(dir.Paths.Root, except: "\0");
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true };

        var exit = await UninstallCommand.RunAsync(
            Machine(dir, output, remover),
            new UninstallOptions(Human: false, DryRun: false, PurgeData: true, Yes: true, Force: false),
            Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.UninstallRefused, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("nothing has been removed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("harness root", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.Empty(remover.Reads);
        Assert.Equal(before, Snapshot(dir.Paths.Root, except: "\0"));
    }

    /// <summary>
    /// Wherever the root stands from the data directory, the purge takes nothing under it: a data directory that is a
    /// folder of the person's own inside a campaign's, one with the root two folders down in a folder the purge removes
    /// whole, and one whose project folders' markers another program holds open. Each purge exited 0, said it completed,
    /// and removed the person's files; each is refused, says so, and leaves every file under the root as it was.
    /// </summary>
    [Theory]
    [InlineData("inside a campaign's folder")]
    [InlineData("the root two folders down")]
    [InlineData("a marker held open")]
    public async Task A_purge_never_reaches_under_the_harness_root_wherever_it_stands(string arrangement)
    {
        using var dir = new TempPaths();
        var top = Path.Combine(dir.Paths.Root, "top");
        string data;
        string held;
        switch (arrangement)
        {
            case "inside a campaign's folder":
                var campaign = Path.Combine(top, "Jason", "projects", "acme", "campaigns", "q4");
                Mark(Path.Combine(top, "Jason"), new HarnessMarker(HarnessMarker.Root, null));
                Mark(Path.Combine(top, "Jason", "projects", "acme"), new HarnessMarker(HarnessMarker.Project, "prj_01JB0000000000000000000000"));
                Mark(campaign, new HarnessMarker(HarnessMarker.Campaign, "cmp_01JB0000000000000000000000"));
                data = Path.Combine(campaign, "research");
                Write(Path.Combine(data, "logs", "call-notes.txt"), "Called Marta at example.com on Tuesday.\n");
                held = string.Empty;
                break;
            case "the root two folders down":
                data = Path.Combine(top, "Sales");
                Mark(Path.Combine(data, "work", "Jason"), new HarnessMarker(HarnessMarker.Root, null));
                Write(Path.Combine(data, "work", "Jason", "AGENTS.md"), "We sell widgets.\n");
                held = string.Empty;
                break;
            default:
                data = Path.Combine(top, "Jason", "projects");
                held = Path.Combine(data, "a-first");
                Mark(held, new HarnessMarker(HarnessMarker.Project, "prj_01JB0000000000000000000001"));
                Mark(Path.Combine(data, "logs"), new HarnessMarker(HarnessMarker.Project, "prj_01JB0000000000000000000002"));
                Write(Path.Combine(data, "logs", "AGENTS.md"), "The Logs project's words.\n");
                break;
        }

        var before = Snapshot(top, except: "\0");
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true };
        var env = Machine(dir, output, remover) with { Paths = new Contracts.Discovery.JasonPaths(data) };

        int exit;
        using (held.Length > 0 ? new FileStream(HarnessMarker.PathIn(held), FileMode.Open, FileAccess.Read, FileShare.None) : null)
        {
            exit = await UninstallCommand.RunAsync(
                env,
                new UninstallOptions(Human: false, DryRun: false, PurgeData: true, Yes: true, Force: false),
                Ct);
        }

        Assert.True(exit == ExitCodes.ApiError, $"{arrangement}: the purge of '{data}' exited {exit}: {output}");
        Assert.Contains(CliErrors.UninstallRefused, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("nothing has been removed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("harness root", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.Equal(before, Snapshot(top, except: "\0"));
    }

    /// <summary>
    /// A purge that cannot see all of what it would remove does not begin: a folder in <c>work/</c> closed to this account's
    /// listing refuses it, and nothing at all is removed — the logs beside it included, which the purge could have taken.
    /// </summary>
    [Fact]
    public async Task A_purge_with_a_folder_it_cannot_enter_is_refused_and_removes_nothing()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged account lists any folder, so none can be closed to it.");

        using var dir = new TempPaths();
        Plant(dir);
        var logged = Path.Combine(dir.Paths.LogsDirectory, "runtime-20261023.jsonl");
        Write(logged, "{}\n");
        var closed = Path.Combine(dir.Paths.Root, "work", "wi_01JB0000000000000000000000");
        Directory.CreateDirectory(Path.Combine(closed, "inside"));
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true };

        int exit;
        using (DataDirectoryBeltTests.Unlistable(closed))
        {
            exit = await UninstallCommand.RunAsync(
                Machine(dir, output, remover),
                new UninstallOptions(Human: false, DryRun: false, PurgeData: true, Yes: true, Force: false),
                Ct);
        }

        Assert.True(exit == ExitCodes.ApiError, $"the purge with '{closed}' closed to it exited {exit}: {output}");
        Assert.Contains(CliErrors.UninstallRefused, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("nothing has been removed", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("could not be looked through", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.True(File.Exists(logged) && File.Exists(dir.Paths.UserSettingsFile), $"the refused purge removed '{logged}' or the settings.");
    }

    private static void Mark(string folder, HarnessMarker marker)
    {
        Directory.CreateDirectory(Path.Combine(folder, HarnessMarker.Directory));
        File.WriteAllBytes(HarnessMarker.PathIn(folder), marker.Bytes());
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
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
