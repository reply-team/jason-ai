using Jason.Cli.Status;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;

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
    /// Jason Placed, which is Linux's alone now, or to move Jason.app to the Trash. What the install page says the verb
    /// refuses with, and what it takes off, is what the verb's own help says; which door each platform has is held to the
    /// desktop's code by <c>desktop/test/uninstall-pages.test.ts</c>.
    /// </summary>
    [Fact]
    public async Task The_pages_take_Jason_off_macOS_and_Windows_by_the_one_uninstall()
    {
        var install = Flat(Section(Page("docs", "INSTALL.md"), "## 7. Take it off again"));
        Assert.Contains("On macOS and Windows this is the one uninstall: one installation, Jason Desktop and the `jason` it carries, is removed once.", install, StringComparison.Ordinal);
        Assert.Contains("Run from a terminal while Jason Desktop is open, it refuses with `desktop_running` before anything is removed: quit Jason first.", install, StringComparison.Ordinal);
        Assert.Contains("On Linux the desktop is taken off its own way, because it places things `jason uninstall` has no receipt for: choose Remove What Jason Placed… in the Jason menu first", install, StringComparison.Ordinal);
        Assert.Contains($"it refuses with `{CliErrors.DesktopRunning}` before anything is removed", install, StringComparison.Ordinal);

        using var dir = new TempPaths();
        var output = new StringWriter();
        await CliApp.RunAsync(["uninstall", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);
        var help = Flat(output.ToString());
        Assert.Contains($"refused before anything is removed while Jason Desktop is open ({CliErrors.DesktopRunning}): quit it first.", help, StringComparison.Ordinal);
        Assert.Contains("on macOS and Windows, this takes Jason Desktop off as well, one uninstall from either door", help, StringComparison.Ordinal);
        foreach (var taken in new[] { "Open Jason at Login", "the terminal command jason install placed", "An older command-line install of Jason", "the updater's cache" })
        {
            Assert.Contains(taken, help, StringComparison.Ordinal);
        }

        Assert.Contains("And the harness root (Harness:Root, ~/Jason by default) with every folder and file in it, --purge-data included", help, StringComparison.Ordinal);
        Assert.Contains("one uninstall from either door, the data directory kept unless --purge-data, the harness root never", help, StringComparison.Ordinal);
        Assert.Contains("The data directory stays unless you say otherwise, with `--purge-data` or that box, and the harness root always stays.", Flat(Section(Page("docs", "INSTALL.md"), "## 7. Take it off again")), StringComparison.Ordinal);

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
    /// install.sh refuses an Intel Mac by its own choice: it installs Jason Desktop on Apple silicon only. Every release
    /// builds and publishes the Intel disk image, zip and block map, so neither the script nor the install page says that
    /// no build for an Intel Mac is published, and the pages that list the Intel image keep listing it.
    /// </summary>
    [Fact]
    public void An_Intel_Mac_is_refused_by_the_script_s_choice_and_its_image_is_published()
    {
        var workflow = Page(".github", "workflows", "release.yml");
        foreach (var asset in new[] { "jason-desktop-macos-x64.dmg", "jason-desktop-macos-x64.zip", "jason-desktop-macos-x64.zip.blockmap" })
        {
            Assert.Contains($"artifacts/release/{asset}'", workflow, StringComparison.Ordinal);
        }

        var script = Flat(Page("install", "install.sh"));
        var install = Flat(Page("docs", "INSTALL.md"));
        Assert.Contains("fail \"This script installs Jason Desktop on Apple silicon Macs only, and this is an Intel Mac; nothing was installed.\"", script, StringComparison.Ordinal);
        Assert.Contains("On an Intel Mac it stops before it downloads anything, since it installs Jason Desktop on Apple silicon only:", install, StringComparison.Ordinal);
        Assert.Contains("stops on an Intel Mac before it downloads anything, since it installs Jason Desktop on Apple silicon only.", install, StringComparison.Ordinal);
        foreach (var (page, text) in new[] { ("install/install.sh", script), ("docs/INSTALL.md", install) })
        {
            foreach (var said in new[] { "no build for one is published", "for which no build is published", "no Jason Desktop for one is published", "for an Intel Mac is not published", "The releases are built for" })
            {
                Assert.False(text.Contains(said, StringComparison.Ordinal), $"{page} still says \"{said}\".");
            }
        }

        Assert.Contains("`jason-desktop-macos-x64.dmg`", Page("docs", "desktop.md"), StringComparison.Ordinal);
        Assert.Contains("`jason-desktop-macos-x64.dmg`", Page(".github", "release-notes-template.md"), StringComparison.Ordinal);
    }

    /// <summary>
    /// On a Mac the script puts Jason.app in <c>/Applications</c> where the account may write there, a folder every account
    /// of the machine shares, else in <c>~/Applications</c>. No page says that everything is installed under the person's own
    /// account on every system; the pages that install Jason name the two places, as the script chooses them.
    /// </summary>
    [Fact]
    public void The_pages_name_where_the_application_goes_on_a_Mac()
    {
        var script = Page("install", "install.sh");
        Assert.Contains("SYSTEM_APPLICATIONS=\"/Applications\"", script, StringComparison.Ordinal);
        Assert.Contains("elif [ -d \"$SYSTEM_APPLICATIONS\" ] && [ -w \"$SYSTEM_APPLICATIONS\" ]; then\n        PLACE=\"$SYSTEM_APPLICATIONS\"\n    else\n        PLACE=\"$HOME/Applications\"", script, StringComparison.Ordinal);

        foreach (var page in new[] { "README.md", "docs/INSTALL.md", "docs/handbook.md" })
        {
            Assert.Contains("`/Applications` where your account may write there, else `~/Applications`", Flat(Page(page.Split('/'))), StringComparison.Ordinal);
        }

        Assert.Contains("`/Applications` where this account may write there, else `~/Applications`", Flat(Page("docs", "release-and-update.md")), StringComparison.Ordinal);

        foreach (var page in new[] { "README.md", "docs/INSTALL.md", "docs/handbook.md", "docs/release-and-update.md", "docs/desktop.md", ".github/release-notes-template.md" })
        {
            var text = Flat(Page(page.Split('/')));
            foreach (var said in new[] { "Everything here is per-user", "Everything goes under your own account", "nothing is installed for other people on the machine", "Under your own account, with no admin rights", "installs Jason Desktop under your own account", "All of it is per-user" })
            {
                Assert.False(text.Contains(said, StringComparison.Ordinal), $"{page} still says \"{said}\".");
            }
        }
    }

    /// <summary>
    /// <c>jason install</c> puts Jason's skills in for Claude Code and Codex only, each where its home folder is found, and
    /// Codex's folder is the one Copilot, Cursor and Gemini CLI read too. The pages say exactly that, never "every agent
    /// host", and tell every reader that another host takes them with <c>jason skills install --host</c>. The hosts named
    /// are the routine's own, read from its help; the hosts said to read Codex's folder are the table's.
    /// </summary>
    [Fact]
    public async Task The_pages_name_the_two_hosts_the_install_gives_skills()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        await CliApp.RunAsync(["install", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);
        var help = Flat(output.ToString());
        Assert.Contains("into the folder of each of two hosts found by its home folder, and of no other: Claude Code (~/.claude -> ~/.claude/skills) and Codex (~/.codex -> ~/.agents/skills, which Copilot, Cursor and Gemini CLI read too)", help, StringComparison.Ordinal);
        Assert.Contains("Set this machine up for the jason Jason Desktop carries: the terminal command, the skills of Claude Code and Codex where each is found, and an older command-line install retired.", help, StringComparison.Ordinal);
        Assert.DoesNotContain("every agent host", help, StringComparison.Ordinal);

        var root = new StringWriter();
        await CliApp.RunAsync(["--help"], new CliEnvironment(root, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);
        Assert.Contains("the skills of Claude Code and Codex where each is found", Flat(root.ToString()), StringComparison.Ordinal);
        Assert.DoesNotContain("every agent host", Flat(root.ToString()), StringComparison.Ordinal);

        var installer = Flat(Page("desktop", "installer", "uninstall.nsh"));
        Assert.Contains("DetailPrint \"Setting this machine up for Jason: the jason command in the terminal and the skills of Claude Code and Codex where each is found.\"", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("every agent host", installer, StringComparison.Ordinal);

        var codex = AgentHosts.All.Single(row => row.Kind == AgentHostKind.Codex);
        var claude = AgentHosts.All.Single(row => row.Kind == AgentHostKind.ClaudeCode);
        Assert.Equal((".claude", ".claude/skills"), (claude.HomeMarker, claude.HomeSkills));
        Assert.Equal((".codex", ".agents/skills"), (codex.HomeMarker, codex.HomeSkills));
        Assert.Equal(
            new[] { AgentHostKind.CopilotCli, AgentHostKind.CursorCli, AgentHostKind.GeminiCli },
            AgentHosts.All.Where(row => row.HomeSkills == codex.HomeSkills && row.Kind != AgentHostKind.Codex).Select(row => row.Kind));

        var install = Flat(Page("docs", "INSTALL.md"));
        Assert.Contains("On macOS and Windows the install script, and on Windows the installer, put Jason's skills where Claude Code and Codex read them, each where it is found (`~/.claude/skills`, and `~/.agents/skills`, which Copilot, Cursor and Gemini CLI read too):", install, StringComparison.Ordinal);

        var readme = Flat(Page("README.md"));
        Assert.Contains("Where your agent is another host than Claude Code or Codex, or on Linux, give it Jason's skills with `jason skills install --host <your host>`", readme, StringComparison.Ordinal);
        Assert.Contains("another host gets them with `jason skills install --host <host>`.", Flat(Page("docs", "handbook.md")), StringComparison.Ordinal);
        Assert.Contains("another agent host gets them with `jason skills install --host <host>`.", Flat(Page(".github", "release-notes-template.md")), StringComparison.Ordinal);
        Assert.Contains("Any other agent host, and on Linux every one, gets Jason's skills with `jason skills install --host <host>`", Flat(Page("skills", "runtime", "operating-the-installation", "SKILL.md")), StringComparison.Ordinal);

        foreach (var page in new[] { "README.md", "CLAUDE.md", "docs/INSTALL.md", "docs/handbook.md", "docs/desktop.md", "docs/release-and-update.md", ".github/release-notes-template.md", "desktop/README.md", "skills/runtime/operating-the-installation/SKILL.md", "install/install.sh", "install/install.ps1", "desktop/installer/uninstall.nsh", "dotnet/src/Jason.Cli/CliApp.cs", "dotnet/src/Jason.Cli/Installation/InstallCommand.cs" })
        {
            var text = Flat(Page(page.Split('/')));
            foreach (var said in new[] { "every agent host it finds", "every agent host found", "skills of every agent host", "where every agent host" })
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
