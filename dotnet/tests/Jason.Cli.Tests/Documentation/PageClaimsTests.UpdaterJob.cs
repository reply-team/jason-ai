using Jason.Cli.Update;

namespace Jason.Cli.Tests.Documentation;

public partial class PageClaimsTests
{
    /// <summary>
    /// What the one uninstall takes on macOS includes the launchd job Squirrel submits for ShipIt on an update, and what it
    /// refuses with includes an update ShipIt is installing, with what shows and takes out the job of an update that never
    /// ends. The verb's help, whose list of what is removed is a contract, and every page that lists what the uninstall takes
    /// on macOS or what it refuses with say so, each in its own words. The skill an agent reads says to tell the person and
    /// run it again, and the refusal it relays carries the commands.
    /// </summary>
    [Fact]
    public async Task The_pages_say_the_macOS_uninstall_takes_the_updaters_job_out_and_waits_for_an_update_being_installed()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        await CliApp.RunAsync(["uninstall", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);
        var help = Flat(output.ToString());
        Assert.Contains($"- on macOS, refused before anything is removed while ShipIt installs an update of Jason Desktop, its launchd job ai.jasonharness.desktop.ShipIt running or set to run ({UpdateCodes.InProgress}): run this again when the update has ended; from the Jason menu, reopen Jason and choose Uninstall Jason… again. If the update never ends, launchctl print gui/$(id -u)/ai.jasonharness.desktop.ShipIt shows the job and launchctl bootout gui/$(id -u)/ai.jasonharness.desktop.ShipIt takes it out. A job launchctl could not be asked about refuses nothing: its step reads it again, takes it out if it is loaded then, keeps it, ShipIt's cache, the application and its state if it is running then, and leaves it and says so if it is still unread.", help, StringComparison.Ordinal);
        Assert.Contains("and the updater's cache (on macOS also ~/Library/Caches/ ai.jasonharness.desktop.ShipIt, after the launchd job of that name Squirrel submits for ShipIt on an update, taken out with /bin/launchctl bootout gui/<uid>/ai.jasonharness.desktop.ShipIt where launchd says it is loaded and not running; one running or set to run keeps ShipIt's cache, the application and its state, one found running just before the application goes keeps the application and its state, and one launchctl could not be asked about is left; each is said).", help, StringComparison.Ordinal);

        var neverEnds = "If the update never ends, `launchctl print gui/$(id -u)/ai.jasonharness.desktop.ShipIt` shows the job and `launchctl bootout gui/$(id -u)/ai.jasonharness.desktop.ShipIt` takes it out.";
        var update = Flat(Page("docs", "release-and-update.md"));
        Assert.Contains($"On macOS it refuses with `{UpdateCodes.InProgress}` before anything is removed, a dry run aside, while ShipIt installs an update of Jason Desktop, its launchd job `ai.jasonharness.desktop.ShipIt` running or set to run: the swap is never cut off part-way, and nothing else refuses it, since the application has quit for the swap and ShipIt runs from Squirrel's framework inside the bundle. Run it again when the update has ended; from the Jason menu, reopen Jason and choose **Uninstall Jason…** again. {neverEnds}", update, StringComparison.Ordinal);
        Assert.Contains("and the updater's cache — on macOS also Squirrel's ShipIt state, `~/Library/Caches/ai.jasonharness.desktop.ShipIt`, after the launchd job Squirrel submits for ShipIt on an update, `ai.jasonharness.desktop.ShipIt`, which launchd keeps loaded once ShipIt has ended: taken out of this account's login domain where `launchctl print` says it is loaded and not running (`/bin/launchctl bootout gui/<uid>/ai.jasonharness.desktop.ShipIt`, then `launchctl remove` by its label, each waited for ten seconds at most) and said in the report; one running or set to run is an update being installed, and is left, with ShipIt's state, the application and its own state under `~/Library`, each said under `kept` and the reason among the problems; a ShipIt found running at the last look, just before the application goes, keeps the application and its own state, and is said among the problems; one `launchctl` could not be asked about is left as it was, and said; one not loaded is nothing to say. The plan names it as `updater_job` where launchd says it is loaded when the plan is read |", update, StringComparison.Ordinal);
        Assert.Contains("`caches`, `updater_job` (macOS: `ai.jasonharness.desktop.ShipIt` where launchd says that job is loaded when the plan is read, else null), `application_state`", update, StringComparison.Ordinal);

        var desktop = Flat(Page("docs", "desktop.md"));
        Assert.Contains($"It takes out the job Squirrel leaves loaded in launchd after an update, `ai.jasonharness.desktop.ShipIt`, too, and while that job is still installing an update it refuses with `{UpdateCodes.InProgress}` and takes nothing off: wait for the update to end, then run it again. {neverEnds}", desktop, StringComparison.Ordinal);
        Assert.Contains("A refusal from the menu is in that file too, and running it again from the menu is reopening Jason and choosing **Uninstall Jason…** again.", desktop, StringComparison.Ordinal);
        Assert.Contains("an older command-line install, the updater's cache and, on macOS, the job its updater leaves loaded in launchd - and last the application", Flat(Page(".github", "release-notes-template.md")), StringComparison.Ordinal);

        var waiting = $"On macOS, while Jason Desktop's updater installs an update, it refuses with `{UpdateCodes.InProgress}` before anything is removed:";
        Assert.Contains(waiting + " wait for the update to end, then run it again. " + neverEnds, Flat(Section(Page("docs", "INSTALL.md"), "## 7. Take it off again")), StringComparison.Ordinal);
        Assert.Contains(waiting + " say so, and run it again once the update has ended.", Flat(Page("skills", "runtime", "operating-the-installation", "SKILL.md")), StringComparison.Ordinal);
    }
}
