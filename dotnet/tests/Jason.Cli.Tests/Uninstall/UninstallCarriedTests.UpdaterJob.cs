using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// macOS: the launchd job Squirrel.Mac submits for ShipIt when Jason Desktop installs an update, which launchd keeps loaded
/// once ShipIt has ended. The one uninstall takes it out before ShipIt's cache, where launchd says it is loaded and not
/// running, and says so; one not loaded is nothing to say. One running is an update being installed. Nothing the uninstall
/// already refused with stands before that step: the application has quit for the swap, ShipIt runs from Squirrel's
/// framework inside the bundle and not as the application, the looks for another jason match only this installation's
/// image, and no update's lock or ledger is read. So the uninstall refuses with the update's own code, before anything is
/// removed, and never ends ShipIt part-way.
/// </summary>
public sealed partial class UninstallCarriedTests
{
    private static readonly LaunchdJob ShipItInstalling = new(LaunchdJobState.Running, Pid: 6262);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task On_macOS_an_update_being_installed_refuses_the_uninstall_before_anything_is_removed(bool human)
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var shipIt = Plant(Path.Combine(Home, "Library", "Caches", DesktopRemoval.ShipItCacheName));
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.Jobs.Add(ShipItInstalling);
        var error = new StringWriter();

        var exit = await UninstallCommand.RunAsync(env with { Error = error }, new UninstallOptions(human, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var said = human ? error.ToString() : RuntimeVerbs.Envelope(output.ToString()).Message;
        if (!human)
        {
            Assert.Equal(UpdateCodes.InProgress, RuntimeVerbs.Envelope(output.ToString()).Code);
            Assert.False(JsonDocument.Parse(output.ToString()).RootElement.GetProperty("error").GetProperty("retryable").GetBoolean());
        }

        Assert.Contains($"ShipIt is installing an update of Jason Desktop (process 6262, the launchd job {DesktopRemoval.ShipItLabel})", said, StringComparison.Ordinal);
        Assert.Contains("Wait for it to end, then run this again. Nothing was removed", said, StringComparison.Ordinal);
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

    [Fact]
    public async Task A_dry_run_with_an_update_being_installed_removes_nothing_and_asks_launchd_nothing()
    {
        var executable = MacApplication();
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.Jobs.Add(ShipItInstalling);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, DryRun: true, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.Empty(remover.LaunchdReads);
        Assert.Empty(remover.Calls);
    }

    /// <summary>
    /// The job at its step, read once before the uninstall begins and once at the step: one not loaded is nothing to say; one
    /// loaded and not running is booted out of the login domain before ShipIt's cache goes, read again, and said gone, or
    /// said still loaded with the command that takes it out; one that began running since is left, never ended part-way, and
    /// said; and one launchd could not be asked about is left as it was, and said.
    /// </summary>
    [Theory]
    [InlineData("not loaded", "absent", false, null)]
    [InlineData("loaded, then gone", "loaded loaded absent", true, "done")]
    [InlineData("still loaded after it", "loaded loaded loaded", true, "still")]
    [InlineData("running at its step", "loaded running", false, "running")]
    [InlineData("not read", "unread", false, "unread")]
    public async Task On_macOS_the_updaters_job_is_taken_out_before_its_cache_where_it_is_not_running(string row, string reads, bool bootedOut, string? said)
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var shipIt = Plant(Path.Combine(Home, "Library", "Caches", DesktopRemoval.ShipItCacheName));
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
        var problems = report.GetProperty("problems").EnumerateArray().Select(line => line.GetString()!).ToList();
        var label = DesktopRemoval.ShipItLabel;
        var bootout = remover.Calls.IndexOf($"launchd.bootout {label}");
        Assert.True(bootout >= 0 == bootedOut, $"{row}: {string.Join(" | ", remover.Calls)}");
        Assert.True(exit == (said is null or "done" ? ExitCodes.Success : ExitCodes.ApiError), $"{row}: {output}");
        Assert.False(Directory.Exists(shipIt), $"{row}: ShipIt's state is still there");
        Assert.False(Directory.Exists(application), $"{row}: the application is still there");
        if (bootedOut)
        {
            var cache = remover.Calls.IndexOf($"tree.remove {shipIt}");
            Assert.True(cache > bootout && remover.Calls.IndexOf($"tree.remove {application}") > cache, $"{row}: {string.Join(" | ", remover.Calls)}");
        }

        var command = $"`launchctl bootout gui/$(id -u)/{label}`";
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
                Assert.Contains($"The launchd job {label} was left: ShipIt began installing an update of Jason Desktop (process 6262) while this ran, and an update is never ended part-way. Once it has ended, {command} takes the job out.", problems);
                break;
            default:
                Assert.Contains($"launchd could not be asked about the job {label} (did not answer within 10 seconds), so it was left as it was: `launchctl print gui/$(id -u)/{label}` says whether it is loaded, and {command} takes it out.", problems);
                break;
        }
    }
}
