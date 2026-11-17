using Jason.Cli.Status;

namespace Jason.Cli.Tests.Documentation;

public partial class PageClaimsTests
{
    /// <summary>
    /// The README's install by hand ends where an agent's install ends: the person's agent knows Jason, Jason is ready to
    /// work, and the last line sends the person back to their agent. Ready to work includes an execution profile for the
    /// roles, which an installation starts without: the block gives the two commands that make and name one, or Jason
    /// Desktop in Live, and says that <c>jason status</c> names what is missing until then, in the check and the commands
    /// the check itself prints.
    /// </summary>
    [Fact]
    public void The_readme_s_install_by_hand_ends_where_the_agent_s_does()
    {
        var readme = Page("README.md");
        var start = readme.IndexOf("<summary>Or install it yourself</summary>", StringComparison.Ordinal);
        Assert.True(start >= 0, "The README has no block for installing by hand.");
        var block = Flat(readme[start..readme.IndexOf("</details>", start, StringComparison.Ordinal)]);

        Assert.Contains("until one is named the default, `jason status` names it under `roles_launchable`.", block, StringComparison.Ordinal);
        Assert.Contains("jason profile create claude-code --host claude_code --program claude jason profile set-default claude-code", block, StringComparison.Ordinal);
        Assert.Contains("Or open Jason Desktop in Live, with Claude Code or Codex connected there: it makes and names one for the agent its chat runs on.", block, StringComparison.Ordinal);
        Assert.Contains("Then go back to your agent and ask it to start Jason's quick-start skill: it takes you to your first campaign.", block, StringComparison.Ordinal);
        Assert.DoesNotContain("brew install", block, StringComparison.Ordinal);

        // What the block says `jason status` names is what the check says on a fresh installation: absent, under that
        // name, with both commands, the first of them as its fix.
        var check = StatusChecks.RolesLaunchableCheck([], unread: null, "/data/config/settings.json", new StatusChecks.DefaultProfileSetting(null, null));
        Assert.Equal("roles_launchable", check.Name);
        Assert.Equal(CheckState.Absent, check.State);
        Assert.Equal("jason profile create claude-code --host claude_code --program claude", check.Fix);
        Assert.Contains("`jason profile set-default claude-code`", check.Fact, StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS and Windows the carried <c>jason uninstall</c> is the one uninstall. The install page, the README and the
    /// handbook say so where they take Jason off, with its other doors, and no page sends a person on macOS to Remove What
    /// Jason Placed, which is Linux's alone now, or to move Jason.app to the Trash.
    /// </summary>
    [Fact]
    public void The_pages_take_Jason_off_macOS_and_Windows_by_the_one_uninstall()
    {
        var install = Flat(Section(Page("docs", "INSTALL.md"), "## 7. Take it off again"));
        Assert.Contains("On macOS and Windows this is the one uninstall: one installation, Jason Desktop and the `jason` it carries, is removed once.", install, StringComparison.Ordinal);
        Assert.Contains("Run from a terminal while Jason Desktop is open, it refuses with `desktop_running` before anything is removed: quit Jason first.", install, StringComparison.Ordinal);
        Assert.Contains("On Linux the desktop is taken off its own way, because it places things `jason uninstall` has no receipt for: choose Remove What Jason Placed… in the Jason menu first", install, StringComparison.Ordinal);
        Assert.Equal("desktop_running", CliErrors.DesktopRunning);

        var readme = Flat(Page("README.md"));
        Assert.Contains("On macOS and Windows it takes Jason Desktop off with everything it placed, and it is also in Installed apps on Windows and in the Jason menu on macOS (Uninstall Jason…).", readme, StringComparison.Ordinal);
        Assert.Contains("On macOS and Windows it comes off with the one uninstall, `jason uninstall` or Uninstall Jason… in its menu on macOS, or Installed apps on Windows; on Linux, choose Remove What Jason Placed… in its menu and then remove the application.", readme, StringComparison.Ordinal);

        var handbook = Flat(Page("docs", "handbook.md"));
        Assert.Contains("On macOS and Windows this is the one uninstall: it takes Jason Desktop off with everything it placed and an older command-line install", handbook, StringComparison.Ordinal);

        foreach (var (page, text) in new[] { ("README.md", readme), ("docs/INSTALL.md", Flat(Page("docs", "INSTALL.md"))), ("docs/desktop.md", Flat(Page("docs", "desktop.md"))), ("docs/release-and-update.md", Flat(Page("docs", "release-and-update.md"))), (".github/release-notes-template.md", Flat(Page(".github", "release-notes-template.md"))), ("desktop/README.md", Flat(Page("desktop", "README.md"))) })
        {
            foreach (var said in new[] { "On macOS and Linux, choose Remove What Jason Placed", "on macOS and Linux (Windows's uninstaller does the same)", "(macOS and Linux) or Windows's own uninstaller", "On macOS, choose Remove What Jason Placed", "move Jason.app to the Trash", "moving Jason.app to the Trash", "Jason.app goes to the Trash" })
            {
                Assert.False(text.Contains(said, StringComparison.Ordinal), $"{page} still says \"{said}\".");
            }
        }
    }

    /// <summary>
    /// The install words are said once and the same way on every page that installs Jason: the install page, the README, the
    /// handbook, the desktop's page, the release and update page and every release's notes.
    /// </summary>
    [Theory]
    [InlineData("docs/INSTALL.md")]
    [InlineData("docs/handbook.md")]
    [InlineData("docs/desktop.md")]
    [InlineData("docs/release-and-update.md")]
    [InlineData(".github/release-notes-template.md")]
    [InlineData("desktop/README.md")]
    public void The_pages_that_install_Jason_say_one_installation_in_the_same_words(string page) =>
        Assert.Contains(
            "On macOS and Windows, Jason is one installation: the Jason Desktop application, which carries `jason`, the command line your terminal and your agents use.",
            Flat(Page(page.Split('/'))),
            StringComparison.Ordinal);
}
