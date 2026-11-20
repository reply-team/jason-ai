using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// macOS: the launchd job Squirrel.Mac submits for ShipIt when Jason Desktop installs an update, which launchd keeps loaded
/// once ShipIt has ended. The one uninstall takes it out before ShipIt's cache, where launchd says it is loaded and not
/// running, and says so; one not loaded is nothing to say. One running, or set to run, is an update being installed. Nothing
/// the uninstall already refused with stands before that step: the application has quit for the swap, ShipIt runs from
/// Squirrel's framework inside the bundle and not as the application, the looks for another jason match only this
/// installation's image, and no update's lock or ledger is read. So the uninstall refuses with the update's own code, before
/// anything is removed, and never ends ShipIt part-way: where ShipIt begins after that look, what it installs is left, and
/// said.
/// </summary>
public sealed partial class UninstallCarriedTests
{
    /// <summary>What the refusal says to do, at a terminal and from the Jason menu.</summary>
    private const string RunItAgain = "run it again when the update has ended; from the Jason menu, reopen Jason and choose Uninstall Jason again.";

    /// <summary>
    /// What the refusal says of an update that never ends, such as a job launchd keeps starting again: the commands that
    /// show the job and take it out.
    /// </summary>
    private const string NeverEnds = "If the update never ends, `launchctl print gui/$(id -u)/ai.jasonharness.desktop.ShipIt` shows the job and `launchctl bootout gui/$(id -u)/ai.jasonharness.desktop.ShipIt` takes it out.";

    private static readonly LaunchdJob ShipItInstalling = new(LaunchdJobState.Running, Pid: 6262);

    /// <summary>
    /// The look at the verb's start, the first line: a job that reads running, or set to run with no process of its own,
    /// refuses before anything is removed, with the update's own code, and says what shows and takes out the job of an update
    /// that never ends.
    /// </summary>
    [Theory]
    [InlineData(false, 6262)]
    [InlineData(true, 6262)]
    [InlineData(false, null)]
    public async Task On_macOS_an_update_being_installed_refuses_the_uninstall_before_anything_is_removed(bool human, int? pid)
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var shipIt = Plant(Path.Combine(Home, "Library", "Caches", DesktopRemoval.ShipItCacheName));
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.Jobs.Add(new LaunchdJob(LaunchdJobState.Running, pid));
        var error = new StringWriter();

        var exit = await UninstallCommand.RunAsync(env with { Error = error }, new UninstallOptions(human, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var said = human ? error.ToString() : RuntimeVerbs.Envelope(output.ToString()).Message;
        if (!human)
        {
            Assert.Equal(UpdateCodes.InProgress, RuntimeVerbs.Envelope(output.ToString()).Code);
            Assert.False(JsonDocument.Parse(output.ToString()).RootElement.GetProperty("error").GetProperty("retryable").GetBoolean());
        }

        var process = pid is null ? string.Empty : $"process {pid}, ";
        Assert.Contains($"ShipIt is installing an update of Jason Desktop ({process}the launchd job {DesktopRemoval.ShipItLabel})", said, StringComparison.Ordinal);
        Assert.Contains($"so nothing was removed: {RunItAgain} {NeverEnds} The data directory is kept", said, StringComparison.Ordinal);
        Assert.DoesNotContain("jason update status", said, StringComparison.Ordinal);
        Assert.Equal([DesktopRemoval.ShipItLabel], remover.LaunchdReads);
        Assert.Empty(remover.Calls);
        Assert.True(Directory.Exists(application), "the application was removed");
        Assert.True(Directory.Exists(shipIt), "ShipIt's state was removed");
    }

    /// <summary>
    /// With the window open as well, the window is said first: ShipIt waits for the application to quit before it swaps, so
    /// waiting for ShipIt while the window stays open would never end.
    /// </summary>
    [Fact]
    public async Task With_the_window_open_and_ShipIt_running_the_open_window_is_said_first()
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.Running[Path.Combine(application, "Contents", "MacOS", "Jason")] = [5150];
        remover.Jobs.Add(ShipItInstalling);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(CliErrors.DesktopRunning, RuntimeVerbs.Envelope(output.ToString()).Code);
        Assert.Empty(remover.Calls);
    }

    /// <summary>A dry run is never refused for it: it reads the job once, and its plan names the job it would take out.</summary>
    [Fact]
    public async Task A_dry_run_with_an_update_being_installed_removes_nothing_and_names_the_job_in_its_plan()
    {
        var executable = MacApplication();
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.Jobs.Add(ShipItInstalling);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, DryRun: true, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        var desktop = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop");
        Assert.Equal(DesktopRemoval.ShipItLabel, desktop.GetProperty("updater_job").GetString());
        Assert.Equal([DesktopRemoval.ShipItLabel], remover.LaunchdReads);
        Assert.Empty(remover.Calls);
    }

    /// <summary>
    /// The job read when the plan is read, at its step, again after a take-out, and at the last look before the application
    /// goes: one not loaded is nothing to say; one loaded and not running is booted out of the login domain before ShipIt's
    /// cache goes, read again, and said gone, or said still loaded with the command that takes it out; one that runs at its
    /// step is left, and so are ShipIt's cache, the application and its own state under ~/Library, each said kept, with the
    /// problem saying that an update was being installed, what was left, and what to do; one that runs at the last look
    /// leaves the application and its state, and is said; and one launchd could not be asked about is left as it was, and
    /// said.
    /// </summary>
    [Theory]
    [InlineData("not loaded", "absent", false, null, true, true)]
    [InlineData("loaded, then gone", "loaded loaded absent", true, "done", true, true)]
    [InlineData("still loaded after it", "loaded loaded loaded", true, "still", true, true)]
    [InlineData("running at its step", "loaded running", false, "running", false, false)]
    [InlineData("running at the last look", "loaded loaded absent running", true, "late", true, false)]
    [InlineData("not read", "unread", false, "unread", true, true)]
    public async Task On_macOS_the_updaters_job_is_taken_out_before_its_cache_where_it_is_not_running(
        string row, string reads, bool bootedOut, string? said, bool cacheGoes, bool applicationGoes)
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var shipIt = Plant(Path.Combine(Home, "Library", "Caches", DesktopRemoval.ShipItCacheName));
        var state = Plant(Path.Combine(Home, "Library", "Saved Application State", DesktopRemoval.Identifier + ".savedState"));
        var (env, output, remover) = Environment(MacPlaces(), executable);
        foreach (var read in reads.Split(' '))
        {
            remover.Jobs.Add(read switch
            {
                "absent" => LaunchdJob.Absent,
                "loaded" => new LaunchdJob(LaunchdJobState.Loaded),
                "running" => ShipItInstalling,
                _ => new LaunchdJob(LaunchdJobState.Unread, Why: "did not answer within 10 seconds"),
            });
        }

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        var report = JsonDocument.Parse(output.ToString()).RootElement;
        var done = report.GetProperty("done").EnumerateArray().Select(line => line.GetString()!).ToList();
        var kept = report.GetProperty("kept").EnumerateArray().Select(line => line.GetString()!).ToList();
        var problems = report.GetProperty("problems").EnumerateArray().Select(line => line.GetString()!).ToList();
        var label = DesktopRemoval.ShipItLabel;
        var bootout = remover.Calls.IndexOf($"launchd.bootout {label}");
        Assert.True(bootout >= 0 == bootedOut, $"{row}: {string.Join(" | ", remover.Calls)}");
        Assert.True(exit == (said is null or "done" ? ExitCodes.Success : ExitCodes.ApiError), $"{row}: {output}");
        Assert.True(Directory.Exists(shipIt) != cacheGoes, $"{row}: ShipIt's state {(cacheGoes ? "is still there" : "was removed")}");
        Assert.True(Directory.Exists(application) != applicationGoes, $"{row}: the application {(applicationGoes ? "is still there" : "was removed")}");
        Assert.True(Directory.Exists(state) != applicationGoes, $"{row}: the application's own state {(applicationGoes ? "is still there" : "was removed")}");
        if (bootedOut)
        {
            var cache = remover.Calls.IndexOf($"tree.remove {shipIt}");
            Assert.True(cache > bootout, $"{row}: {string.Join(" | ", remover.Calls)}");
        }

        if (applicationGoes)
        {
            Assert.True(remover.Calls.IndexOf($"tree.remove {application}") > remover.Calls.IndexOf($"tree.remove {shipIt}"), $"{row}: {string.Join(" | ", remover.Calls)}");
        }

        var command = $"`launchctl bootout gui/$(id -u)/{label}`";
        var again = $"Once the update has ended, uninstall again: from the Jason menu, reopen Jason and choose Uninstall Jason again, or run `\"{executable}\" uninstall`; what is already gone is not an error the second time.";
        switch (said)
        {
            case null:
                Assert.DoesNotContain(done.Concat(problems), line => line.Contains(label, StringComparison.Ordinal) && !line.Contains(shipIt, StringComparison.Ordinal));
                Assert.Empty(problems);
                break;
            case "done":
                Assert.Contains($"Took the launchd job {label}, which Squirrel submits for ShipIt when Jason Desktop installs an update, out of launchd.", done);
                Assert.Empty(problems);
                break;
            case "still":
                Assert.Contains($"The launchd job {label} is still loaded after `launchctl bootout`: {command} takes it out.", problems);
                break;
            case "running":
                Assert.Equal(
                    [$"An update of Jason Desktop was being installed by ShipIt (process 6262, the launchd job {label}), and an update is never ended part-way, so these were left: the job, ShipIt's state, the application and its own state under ~/Library. {again}"],
                    problems);
                Assert.Contains($"Kept {shipIt}, ShipIt's state: an update of Jason Desktop was being installed.", kept);
                Assert.Contains($"Kept the application at {application} and its own state under ~/Library: an update of it was being installed.", kept);
                break;
            case "late":
                Assert.Equal(
                    [$"An update of Jason Desktop was being installed by ShipIt (process 6262, the launchd job {label}) when the application was to go, and an update is never ended part-way, so these were left: the application at {application} and its own state under ~/Library. {again}"],
                    problems);
                break;
            default:
                Assert.Contains($"launchd could not be asked about the job {label} (did not answer within 10 seconds), so it was left as it was: `launchctl print gui/$(id -u)/{label}` says whether it is loaded, and {command} takes it out.", problems);
                break;
        }
    }

    /// <summary>
    /// What the problem says was left is what was: a job running at its step, set to run with no process of its own, where no
    /// ShipIt state and no state of the application's under ~/Library were found, names the job and the application alone.
    /// </summary>
    [Fact]
    public async Task On_macOS_an_update_being_installed_at_the_jobs_step_names_only_what_it_left()
    {
        var executable = MacApplication();
        var application = ApplicationOf(executable);
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.Jobs.Add(new LaunchdJob(LaunchdJobState.Loaded));
        remover.Jobs.Add(new LaunchdJob(LaunchdJobState.Running));

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        var problem = Assert.Single(report.GetProperty("problems").EnumerateArray()).GetString()!;
        Assert.StartsWith(
            $"An update of Jason Desktop was being installed by ShipIt (the launchd job {DesktopRemoval.ShipItLabel}), and an update is never ended part-way, so these were left: the job and the application. ",
            problem,
            StringComparison.Ordinal);
        Assert.Contains($"Kept the application at {application}: an update of it was being installed.", report.GetProperty("kept").EnumerateArray().Select(line => line.GetString()));
        Assert.True(Directory.Exists(application), "the application was removed");
    }

    /// <summary>
    /// An account with no login session - reached over ssh, or run by a management tool, while it is not signed in at the
    /// screen - has no login domain, and so no ShipIt job. launchctl's answers are a stand-in's here, and every read goes
    /// through the one read the uninstall has (<see cref="LaunchdJobs.Read"/>): the plan's look before anything is removed,
    /// the job's step and the last look before the application goes each ask about the job, then, where launchctl cannot
    /// answer for it, about the domain itself. A domain launchd says is not there reads as no job at all three, so the
    /// uninstall completes, exits 0 and says nothing of the job; a domain it says is there leaves the job unread, and the
    /// step says so as before. No exit code stands for one of launchd's own: the stand-in's codes are its own.
    /// </summary>
    [Theory]
    [InlineData("no login domain", 1, true)]
    [InlineData("the login domain there", 0, false)]
    public async Task On_macOS_an_account_with_no_login_domain_has_no_updaters_job_and_the_uninstall_completes(string row, int domainExit, bool noJob)
    {
        var executable = MacApplication();
        var application = ApplicationOf(executable);
        var shipIt = Plant(Path.Combine(Home, "Library", "Caches", DesktopRemoval.ShipItCacheName));
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var label = DesktopRemoval.ShipItLabel;
        var job = $"/bin/launchctl print gui/501/{label}";
        var asked = new List<string>();
        remover.Launchctl = (command, _) =>
        {
            var said = string.Join(' ', command);
            asked.Add(said);
            return said == job
                ? (5, string.Empty, "the stand-in's own words about the job\n")
                : (domainExit, domainExit == 0 ? "gui/501 = {\n\ttype = login\n}\n" : string.Empty, domainExit == 0 ? string.Empty : "the stand-in's own words about the domain\n");
        };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        var report = JsonDocument.Parse(output.ToString()).RootElement;
        var done = report.GetProperty("done").EnumerateArray().Select(line => line.GetString()!).ToList();
        var problems = report.GetProperty("problems").EnumerateArray().Select(line => line.GetString()!).ToList();
        Assert.True(
            asked.SequenceEqual(new[] { job, "/bin/launchctl print gui/501", job, "/bin/launchctl print gui/501", job, "/bin/launchctl print gui/501" }),
            $"{row}: the plan's look, the job's step and the last look did not each ask about the job, then the domain: {string.Join(" | ", asked)}");
        Assert.DoesNotContain($"launchd.bootout {label}", remover.Calls);
        Assert.False(Directory.Exists(shipIt), $"{row}: ShipIt's state is still there");
        Assert.False(Directory.Exists(application), $"{row}: the application is still there");
        if (noJob)
        {
            Assert.True(exit == ExitCodes.Success, $"{row}: {output}");
            Assert.Empty(problems);
            Assert.DoesNotContain(done, line => line.Contains(label, StringComparison.Ordinal) && !line.Contains(shipIt, StringComparison.Ordinal));
        }
        else
        {
            Assert.True(exit == ExitCodes.ApiError, $"{row}: {output}");
            Assert.Equal(
                [$"launchd could not be asked about the job {label} (`launchctl print` ended with 5: the stand-in's own words about the job), so it was left as it was: `launchctl print gui/$(id -u)/{label}` says whether it is loaded, and `launchctl bootout gui/$(id -u)/{label}` takes it out."],
                problems);
        }
    }
}
