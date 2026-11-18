using System.Text.RegularExpressions;
using Jason.Cli;
using Jason.Cli.Update;
using Jason.Contracts.Discovery;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// A runtime refuses a database a newer Jason changed, and says so with <c>data_newer</c>. The troubleshooting skill names
    /// the refusal where it reads whether the runtime answers, and gives the one repair the refusal's own sentence gives:
    /// updating this Jason. It never sends an agent round the refusal by restoring, deleting or starting an older build.
    /// </summary>
    [Fact]
    public void The_troubleshooting_skill_names_a_newer_database_and_its_one_repair()
    {
        var skill = Flattened("troubleshooting-jason");

        Assert.Contains($"When `jason runtime start` answers `{DataNewer.Code}`, the database was changed by a newer Jason, and this one refuses to run on it: it started nothing and changed nothing.", skill, StringComparison.Ordinal);
        Assert.Contains("The repair is to update this Jason, as the refusal's own sentence says: `jason update apply`, or Check for Updates in Jason Desktop.", skill, StringComparison.Ordinal);
        Assert.Contains("Never restore an older backup over the database, delete anything in the data directory, or start an older build on it to get past the refusal", skill, StringComparison.Ordinal);
        Assert.Contains($"**Applying an update**, when the installed version is the known cause, and always when a start answered `{DataNewer.Code}`.", skill, StringComparison.Ordinal);

        // The skill's repair is the one the refusal says, word for word.
        Assert.Contains("`jason update apply`, or Check for Updates in Jason Desktop", DataNewer.Sentence("0.49.0", "/data"), StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS and Windows Jason Desktop is always there, so a browser tab <c>jason ui open</c> opens is not the person's
    /// work because only the desktop draws this release's console, not because the desktop is missing: the skills that send
    /// a person to see what waits say so in those words, and the catalog's line does too.
    /// </summary>
    [Fact]
    public void The_skills_say_a_tab_of_jason_ui_open_is_no_view_of_the_work_with_the_desktop_installed_or_not()
    {
        foreach (var name in new[] { "operating-the-installation", "managed-campaign-work" })
        {
            var skill = Flattened(name);
            Assert.DoesNotContain("Without Jason Desktop", skill, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("whether Jason Desktop is installed or not", skill, StringComparison.Ordinal);
        }

        var catalog = Regex.Replace(File.ReadAllText(SkillPack.Catalog()), @"\s+", " ");
        Assert.Contains(
            "opening the browser UI (this release's UI build draws its console only in Jason Desktop and in a tab the desktop opens itself, so a tab `jason ui open` opens shows the Demo, or, while something waits, one page of text that says so, whether Jason Desktop is installed or not, and neither is the person's work; a custom build under `ui/custom` is served first)",
            catalog,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS and Windows the carried <c>jason uninstall</c> is the one uninstall of the application and of everything
    /// placed for it. The operating skill says so, names the other two doors, and says what a terminal's run answers while
    /// the desktop is open, in the code the verb refuses with.
    /// </summary>
    [Fact]
    public void The_operating_skill_says_the_one_uninstall()
    {
        var skill = Flattened("operating-the-installation");

        Assert.Contains("On macOS and Windows this is the one uninstall: it takes Jason Desktop off with everything it placed", skill, StringComparison.Ordinal);
        Assert.Contains("It is also reached from Installed apps on Windows and from Uninstall Jason… in the Jason menu on macOS.", skill, StringComparison.Ordinal);
        Assert.Contains($"Run from a terminal while Jason Desktop is open, it refuses with `{CliErrors.DesktopRunning}` before anything is removed", skill, StringComparison.Ordinal);
        Assert.Contains("The harness root is never touched.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A drain is said by <c>jason status</c> in its required <c>dispatcher</c> check. The operating skill names the check
    /// beside the drain and resume verbs, once, with its two repairs as the check gives them: the resume, and the update's
    /// own apply while an update stands unfinished, never a resume in the middle of it.
    /// </summary>
    [Fact]
    public void The_operating_skill_names_the_dispatcher_check_and_its_repairs_once()
    {
        var skill = Flattened("operating-the-installation");

        Assert.Contains("`jason status` names a drain in its `dispatcher` check, a required one, so `ready` is false while the runtime drains.", skill, StringComparison.Ordinal);
        Assert.Contains("Its repair is `jason runtime resume`; while an update stands unfinished the drain is the update's, and the repair is `jason update apply`, never a resume in the middle of it.", skill, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(skill, Regex.Escape("`jason runtime drain` has the runtime claim no new work")));
        Assert.Single(Regex.Matches(skill, Regex.Escape("`dispatcher` check")));
    }

    /// <summary>
    /// The operating skill says how a carried update goes: handed to Jason Desktop's own updater and finished by the new
    /// version's <c>jason</c>; the desktop's first start of each version runs the set-up and the finish itself; a held lock,
    /// <c>update_in_progress</c> or <c>install_in_progress</c>, means later; <c>update_desktop_not_installed</c> names a
    /// running Jason Desktop as the likely cause, as the verb's own message does; and on Linux the carried <c>jason</c> is
    /// refused. It says too that a desktop installed by hand sets the machine up at its first start, and that on macOS and
    /// Windows a person is sent to open Jason Desktop rather than download it.
    /// </summary>
    [Fact]
    public void The_operating_skill_says_how_the_carried_update_goes_and_what_a_held_lock_means()
    {
        var skill = Flattened("operating-the-installation");

        Assert.Contains("Where `jason` is the one Jason Desktop carries, as it is on macOS and Windows once the install script ran, `jason update apply` replaces no file itself: it hands the update to the application's own updater, with no window, waits for it, and the new version's `jason` finishes it", skill, StringComparison.Ordinal);
        Assert.Contains("Jason Desktop's first start of each version runs `jason install` and `jason update apply --finish` itself", skill, StringComparison.Ordinal);
        Assert.Contains($"A refusal with `{UpdateCodes.InProgress}` or `{CliErrors.InstallInProgress}` means later, not a failure", skill, StringComparison.Ordinal);
        Assert.Contains($"`{UpdateCodes.DesktopNotInstalled}` means Jason Desktop's installer had not installed the version ten minutes later; where nothing was installed, its message names the likely cause, a Jason Desktop that is running, one of another data directory included", skill, StringComparison.Ordinal);
        Assert.Contains($"refused with `{UpdateCodes.CarriedByDesktop}`", skill, StringComparison.Ordinal);
        Assert.Contains("A person who installs Jason Desktop on macOS or Windows by hand, from its disk image or its installer, gets the same: the application's first start of each version runs the `jason install` it carries", skill, StringComparison.Ordinal);
        Assert.Contains("Jason Desktop is already there, since it is the installation itself: name opening it, not the page's download link, which is for Linux", skill, StringComparison.Ordinal);

        // The likely cause the skill names is the one the verb's refusal gives.
        var carried = Regex.Replace(File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "dotnet", "src", "Jason.Cli", "Update", "CarriedUpdate.cs")), @"""\s*\+\s*""", string.Empty);
        Assert.Contains("The likely cause is a Jason Desktop that is running, one of another data directory included", carried, StringComparison.Ordinal);
    }
}
