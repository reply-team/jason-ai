using System.Text.RegularExpressions;
using Jason.Cli;
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
            "opening the browser UI (this release's console shows only in a tab Jason Desktop opened; any other tab opens on the Demo, or, while something waits, on one page of text that says so; neither is the person's work)",
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
}
