using System.Security.Cryptography;
using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Doctor;
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
    /// <c>setup/</c> is Jason's only where Jason says it made it — a layout stamp, or the record of a move under way. With
    /// neither, a <c>setup</c> folder in the data directory is a person's: the purge keeps it and names it as kept, and
    /// neither the question nor the dry run lists it as Jason's.
    /// </summary>
    [Fact]
    public async Task With_no_stamp_and_no_record_of_a_move_a_setup_folder_is_the_person_s_and_is_kept()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var mine = Path.Combine(dir.Paths.Root, "setup", "notes.txt");
        Write(mine, "a folder of my own, not Jason's");

        var asked = new StringWriter();
        await UninstallCommand.RunAsync(
            Machine(dir, asked, new RecordingRemover { ReallyRemoves = true }) with { In = new StringReader("n\n") },
            new UninstallOptions(Human: true, DryRun: false, PurgeData: true, Yes: false, Force: false),
            Ct);
        var dry = new StringWriter();
        await UninstallCommand.RunAsync(
            Machine(dir, dry, new RecordingRemover { ReallyRemoves = true }),
            new UninstallOptions(Human: true, DryRun: true, PurgeData: true, Yes: false, Force: false),
            Ct);
        var (exit, report) = await RunAsync(dir, purgeData: true, yes: true);

        var question = asked.ToString();
        var keeps = question.IndexOf("and to keep what Jason did not put there:", StringComparison.Ordinal);
        Assert.True(keeps > 0, question);
        Assert.DoesNotContain("  setup/", question[..keeps], StringComparison.Ordinal);
        Assert.Contains("  setup/", question[keeps..], StringComparison.Ordinal);
        Assert.DoesNotContain("setup", dry.ToString(), StringComparison.Ordinal);
        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("a folder of my own, not Jason's", File.ReadAllText(mine));
        Assert.Contains(report.Kept, line => line.Contains("'setup'", StringComparison.Ordinal));
        Assert.False(File.Exists(dir.Paths.DatabaseFile), "the purge kept Jason's own parts.");
    }

    /// <summary>The record of a move under way says Jason made <c>setup/</c> as surely as a stamp does: the purge takes it.</summary>
    [Fact]
    public async Task A_record_of_a_move_under_way_makes_setup_Jason_s_and_the_purge_removes_it()
    {
        using var dir = new TempPaths();
        Plant(dir);
        File.WriteAllText(Path.Combine(dir.Paths.Root, "layout-migration.json"), "{}");
        Write(Path.Combine(dir.Paths.Root, "setup", "plugins", "a.plugin", "plugin.yaml"), "id: a.plugin");

        var (exit, _) = await RunAsync(dir, purgeData: true, yes: true);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(Path.Combine(dir.Paths.Root, "setup")), "the purge left the setup folder a move made.");
    }

    /// <summary>
    /// The layout stamp and the folder layout 2 keeps a person's parts in are Jason's in every layout: a purge by a build
    /// that runs layout 1 removes a <c>setup/</c> a later build made, and a stamp that says layout 1.
    /// </summary>
    [Fact]
    public async Task The_purge_removes_the_layout_stamp_and_the_setup_folder()
    {
        using var dir = new TempPaths();
        Plant(dir);
        File.WriteAllText(dir.Paths.LayoutFile, """{"layout":1,"written_by":"0.42.0"}""");
        Write(Path.Combine(dir.Paths.Root, "setup", "plugins", "a.plugin", "plugin.yaml"), "id: a.plugin");

        var (exit, _) = await RunAsync(dir, purgeData: true, yes: true);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("layout.json", Contracts.Discovery.JasonPaths.OwnEntries);
        Assert.Contains("setup", Contracts.Discovery.JasonPaths.OwnEntries);
        Assert.False(File.Exists(dir.Paths.LayoutFile), "the purge left the layout stamp.");
        Assert.False(Directory.Exists(Path.Combine(dir.Paths.Root, "setup")), "the purge left the setup folder.");
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
    /// The desktop application is one more client of the installation, and what it keeps in the data directory is the
    /// product's own: its log beside the runtime's and its folder of settings, keys and the chat's working folder. The
    /// purge takes them with the rest.
    /// </summary>
    [Fact]
    public async Task The_purge_removes_the_desktop_s_log_and_its_own_folder()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var logs = Directory.CreateDirectory(dir.Paths.LogsDirectory).FullName;
        File.WriteAllText(Path.Combine(logs, "desktop-20261109.jsonl"), "{}");
        File.WriteAllText(Path.Combine(logs, "desktop-20261109_001.jsonl"), "{}");
        var desktop = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "desktop")).FullName;
        File.WriteAllText(Path.Combine(desktop, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(desktop, "provider-keys"), "sealed");
        Directory.CreateDirectory(Path.Combine(desktop, "chat", ".claude", "skills"));

        var (exit, report) = await RunAsync(dir, purgeData: true, yes: true);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(logs), "the desktop's log is still in the data directory.");
        Assert.False(Directory.Exists(desktop), "the desktop's own folder is still in the data directory.");
        Assert.DoesNotContain(report.Kept, line => line.Contains("desktop", StringComparison.Ordinal));
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
    /// A removal that ended in an error once nothing was left there is not a failure to remove something still there: the
    /// entry is gone, the report says so and gives the error beside it, and the purge goes on to remove the directory.
    /// </summary>
    [Fact]
    public async Task A_purge_whose_removal_failed_once_nothing_was_left_says_the_entry_is_gone()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var remover = new RecordingRemover { ReallyRemoves = true };
        remover.FailsOnceGone[dir.Paths.ConfigDirectory] = new IOException(RecordingRemover.Said(dir.Paths.ConfigDirectory, "is no volume's mount point"));
        var output = new StringWriter();

        var exit = await UninstallCommand.RunAsync(
            Machine(dir, output, remover),
            new UninstallOptions(Human: false, DryRun: false, PurgeData: true, Yes: true, Force: false),
            Ct);

        var report = JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;
        Assert.True(exit == ExitCodes.Success, $"the purge exited {exit}: {string.Join(" ", report.Problems)}");
        Assert.DoesNotContain(report.Problems, line => line.Contains(dir.Paths.ConfigDirectory, StringComparison.OrdinalIgnoreCase));
        var said = RecordingRemover.Said(dir.Paths.ConfigDirectory, "is no volume's mount point");
        Assert.True(
            report.Done.Any(line => line.StartsWith($"'{dir.Paths.ConfigDirectory}' is gone", StringComparison.Ordinal) && line.EndsWith(said, StringComparison.Ordinal)),
            $"no line says '{dir.Paths.ConfigDirectory}' is gone with the error beside it: {string.Join(" | ", report.Done)}");
        Assert.False(Directory.Exists(dir.Paths.Root), $"the data directory at '{dir.Paths.Root}' is still there.");
    }

    /// <summary>
    /// A file held open that made the removal fail once nothing was left there is said as the problems say one: from the
    /// home directory, without the operating system's message, which repeats the absolute path.
    /// </summary>
    [Fact]
    public async Task A_purge_whose_removal_was_held_once_nothing_was_left_says_the_entry_is_gone_from_home()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var remover = new RecordingRemover { ReallyRemoves = true };
        var config = dir.Paths.ConfigDirectory;
        remover.FailsOnceGone[config] = new HeldOpen(config, isDirectory: true, InstallationRemovers.HeldFiles, new IOException(RecordingRemover.Said(config, "is in use")));
        var output = new StringWriter();

        var exit = await UninstallCommand.RunAsync(
            Machine(dir, output, remover, home: dir.Paths.Root),
            new UninstallOptions(Human: false, DryRun: false, PurgeData: true, Yes: true, Force: false),
            Ct);

        var report = JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;
        Assert.True(exit == ExitCodes.Success, $"the purge exited {exit}: {string.Join(" ", report.Problems)}");
        var fromHome = $"'~{Path.DirectorySeparatorChar}{Path.GetFileName(config)}'";
        Assert.True(
            report.Done.Any(line => line.StartsWith($"{fromHome} is gone", StringComparison.Ordinal)
                && line.EndsWith($"{fromHome} or a file in it is held open by another program; it was tried for {InstallationRemovers.HeldFiles.WaitInWords}.", StringComparison.Ordinal)),
            $"no line says {fromHome} is gone with the held file beside it: {string.Join(" | ", report.Done)}");
        Assert.True(
            !report.Done.Any(line => line.Contains(config, StringComparison.OrdinalIgnoreCase) || line.Contains("is in use", StringComparison.Ordinal)),
            $"a line of what was done names '{config}' or the system's message: {string.Join(" | ", report.Done)}");
    }

    /// <summary>
    /// A refusal is said before anything is removed, so one that comes where nothing is left is no removal that ended in
    /// an error: it is named among the problems, as every refusal is.
    /// </summary>
    [Fact]
    public async Task A_purge_whose_removal_was_refused_once_nothing_was_left_names_the_refusal_among_the_problems()
    {
        using var dir = new TempPaths();
        Plant(dir);
        var remover = new RecordingRemover { ReallyRemoves = true };
        var config = dir.Paths.ConfigDirectory;
        remover.FailsOnceGone[config] = new RemovalRefused(CliErrors.UninstallRefused, $"'{config}' could not be removed.");
        var output = new StringWriter();

        var exit = await UninstallCommand.RunAsync(
            Machine(dir, output, remover),
            new UninstallOptions(Human: false, DryRun: false, PurgeData: true, Yes: true, Force: false),
            Ct);

        var report = JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;
        Assert.True(exit == ExitCodes.ApiError, $"the purge exited {exit}: {string.Join(" | ", report.Done)}");
        Assert.True(
            report.Problems.Any(line => line.Contains($"'{config}' could not be removed.", StringComparison.Ordinal)),
            $"the refusal of '{config}' is not among the problems: {string.Join(" | ", report.Problems)}");
        Assert.True(
            !report.Done.Any(line => line.Contains(config, StringComparison.OrdinalIgnoreCase)),
            $"a line of what was done names '{config}': {string.Join(" | ", report.Done)}");
    }

    /// <summary>
    /// An entry still there is never said to be gone because a look at its path could not see it: a look answers no for
    /// an entry this account may not read as well as for one that is not there, so the directory it is in is asked too,
    /// and a directory that will not say leaves the entry among the problems.
    /// </summary>
    /// <remarks>
    /// The entry is closed to a look as its removal begins, after the purge read the data directory: on Windows by rules
    /// denying this account the entry's attributes and the data directory's listing, elsewhere by taking the data
    /// directory's search permission, which a listing does not need.
    /// </remarks>
    [Fact]
    public async Task A_purge_whose_entry_cannot_be_seen_after_its_removal_failed_names_it_among_the_problems()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged account looks at any path, so none can be closed to it.");
        using var dir = new TempPaths();
        Plant(dir);
        var config = dir.Paths.ConfigDirectory;
        IDisposable? closed = null;
        var seen = true;
        var remover = new RecordingRemover
        {
            ReallyRemoves = true,
            AsATreeGoes = path =>
            {
                if (path == config)
                {
                    closed = ClosedToALook(config);
                    seen = Path.Exists(config);
                }
            },
        };
        remover.Denied.Add(config);
        var output = new StringWriter();

        int exit;
        try
        {
            exit = await UninstallCommand.RunAsync(
                Machine(dir, output, remover),
                new UninstallOptions(Human: false, DryRun: false, PurgeData: true, Yes: true, Force: false),
                Ct);
        }
        finally
        {
            closed?.Dispose();
        }

        var report = JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;
        Assert.False(seen, $"'{config}' was not closed to a look at its path.");
        Assert.True(Directory.Exists(config), $"'{config}' is gone, which this row needs it not to be.");
        Assert.True(exit == ExitCodes.ApiError, $"the purge exited {exit}: {string.Join(" | ", report.Done)}");
        Assert.True(
            report.Problems.Any(line => line.StartsWith($"'{config}' could not be removed", StringComparison.Ordinal)),
            $"'{config}' is not among the problems: {string.Join(" | ", report.Problems)}");
        Assert.True(
            !report.Done.Any(line => line.Contains(config, StringComparison.OrdinalIgnoreCase)),
            $"a line of what was done names '{config}': {string.Join(" | ", report.Done)}");
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
        var env = Machine(dir, output, remover) with { Paths = new Contracts.Discovery.JasonPaths(data, 1) };

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
    /// removes, in either layout. A directory added to a layout and not to that list would be left behind by every purge; this
    /// is where that arrives instead.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Every_path_the_product_composes_under_the_data_directory_is_one_of_its_own_entries(int layout)
    {
        var paths = new Contracts.Discovery.JasonPaths(Path.Combine(Path.GetTempPath(), "jason-own-entries"), layout);
        var composed = new List<string>();

        foreach (var property in typeof(Contracts.Discovery.JasonPaths).GetProperties().Where(property => property.PropertyType == typeof(string) && property.Name != nameof(paths.Root)))
        {
            composed.Add((string)property.GetValue(paths)!);
        }

        composed.AddRange(paths.StartDirectories);
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
    private static CliEnvironment Machine(TempPaths dir, TextWriter output, RecordingRemover remover, string? home = null) =>
        new(
            output,
            new StringWriter(),
            dir.Paths,
            Autostart: new RecordingRegistrar(),
            Harnesses: HarnessLocators.At(Path.Combine(dir.Paths.Root, "harness")),
            InstallPath: Path.Combine(dir.Paths.Root + "-install", "jason"),
            Removes: remover,
            Identity: home is null ? null : new IdentityFacts(home, "someone", "a-machine", null, null));

    /// <summary>
    /// Closes the directory <paramref name="entry"/> to a look at its path, leaving it where it is, until disposed: on
    /// Windows a rule denying this account its attributes and one denying it the listing of the directory it is in;
    /// elsewhere the directory it is in without its search permission.
    /// </summary>
    private static IDisposable ClosedToALook(string entry)
    {
        var parent = Path.GetDirectoryName(entry)!;
        if (OperatingSystem.IsWindows())
        {
            return new DeniedALook(entry, parent);
        }

        return new NotSearchable(parent);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class DeniedALook : IDisposable
    {
        private readonly (DirectoryInfo Folder, System.Security.AccessControl.FileSystemAccessRule Rule)[] _denied;

        public DeniedALook(string entry, string parent)
        {
            var account = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
            _denied =
            [
                (new DirectoryInfo(entry), new System.Security.AccessControl.FileSystemAccessRule(account, System.Security.AccessControl.FileSystemRights.ReadAttributes, System.Security.AccessControl.AccessControlType.Deny)),
                (new DirectoryInfo(parent), new System.Security.AccessControl.FileSystemAccessRule(account, System.Security.AccessControl.FileSystemRights.ListDirectory, System.Security.AccessControl.AccessControlType.Deny)),
            ];
            foreach (var (folder, rule) in _denied)
            {
                var security = folder.GetAccessControl();
                security.AddAccessRule(rule);
                folder.SetAccessControl(security);
            }
        }

        public void Dispose()
        {
            foreach (var (folder, rule) in _denied.Reverse())
            {
                var security = folder.GetAccessControl();
                security.RemoveAccessRule(rule);
                folder.SetAccessControl(security);
            }
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private sealed class NotSearchable : IDisposable
    {
        private readonly string _folder;

        private readonly UnixFileMode _mode;

        public NotSearchable(string folder)
        {
            _folder = folder;
            _mode = File.GetUnixFileMode(folder);
            File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        public void Dispose() => File.SetUnixFileMode(_folder, _mode);
    }

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
