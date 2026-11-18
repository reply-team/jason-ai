using Jason.Cli.Status;
using Jason.Contracts.Api;
using Jason.Contracts.Hosts;
using Jason.Cli.Update;

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

    /// <summary>
    /// On macOS and Windows a Jason Desktop installed by hand, from its disk image or its installer, sets the machine up as
    /// the install script does: the application's first start of each version runs the <c>jason install</c> it carries, and
    /// the Windows installer runs it too. The pages that offer the download say so; the installer's own include is where
    /// the Windows half is held, and the desktop's tests hold the first start (<c>desktop/test/one-installation-pages.test.ts</c>).
    /// </summary>
    [Fact]
    public void The_pages_say_a_package_installed_by_hand_sets_the_machine_up_at_its_first_start()
    {
        var installer = Page("desktop", "installer", "uninstall.nsh");
        Assert.Contains("!macro customInstall", installer, StringComparison.Ordinal);
        Assert.Contains("nsExec::Exec '\"$INSTDIR\\resources\\bundle\\jason.exe\" install'", installer, StringComparison.Ordinal);

        Assert.Contains("Installed by hand from one of them, it sets the machine up as the script does (§2): the application's first start of each version runs the `jason install` it carries, which puts `jason` in the terminal and Jason's skills for Claude Code and Codex, where each is found, and its Command Line item says what that placed", Flat(Section(Page("docs", "INSTALL.md"), "## 2. Install from a release")), StringComparison.Ordinal);
        Assert.Contains("the Windows installer runs it too, at every install and update.", Flat(Page("docs", "INSTALL.md")), StringComparison.Ordinal);
        Assert.Contains("On macOS and Windows both make one installation: Jason Desktop and the `jason` it carries, whose first start of each version puts `jason` in your terminal and Jason's skills where Claude Code and Codex read them, each where it is found.", Flat(Page("README.md")), StringComparison.Ordinal);
        Assert.Contains("and so does the package for your system below, installed by hand: the application's first start of each version then puts `jason` in your terminal and Jason's skills where Claude Code and Codex read them, each where it is found", Flat(Page("docs", "desktop.md")), StringComparison.Ordinal);
        Assert.Contains("On macOS and Windows, the application's first start of each version puts `jason` in the terminal and Jason's skills where Claude Code and Codex read them, each where it is found, through the `jason` it carries; the Windows installer does the same at every install and update.", Flat(Page(".github", "release-notes-template.md")), StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS and Windows the carried <c>jason update apply</c> hands the update to Jason Desktop's own updater and the new
    /// version's <c>jason</c> finishes it, as the verb's own help says. Another update or a <c>jason install</c> holding the
    /// update's lock is refused with <c>update_in_progress</c> or <c>install_in_progress</c>, which mean later: the
    /// refusals the lock itself raises say to let that one end and run it again, and the pages say so in the same sense.
    /// </summary>
    [Fact]
    public async Task The_pages_say_the_carried_update_goes_through_Jason_Desktop_and_a_held_lock_means_later()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        await CliApp.RunAsync(["update", "apply", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);
        Assert.Contains("Run as the jason Jason Desktop carries, the application is updated by the Desktop's own updater with no window", Flat(output.ToString()), StringComparison.Ordinal);

        var update = new UpdatePaths(dir.Paths);
        using (UpdateLock.Take(update, "a test's update"))
        {
            var held = Assert.Throws<UpdateException>(() => UpdateLock.Take(update, "this update"));
            Assert.Equal(UpdateCodes.InProgress, held.Code);
            Assert.Contains("Let that one end, then run this again", held.Message, StringComparison.Ordinal);
        }

        using (UpdateLock.Take(update, "a test's setup", holder: UpdateLock.Holder.Install))
        {
            var held = Assert.Throws<UpdateException>(() => UpdateLock.Take(update, "this update"));
            Assert.Equal(CliErrors.InstallInProgress, held.Code);
            Assert.Contains("let it end, then run this again", held.Message, StringComparison.Ordinal);
        }

        var handbook = Flat(Page("docs", "handbook.md"));
        Assert.Contains("On macOS and Windows the `jason` Jason Desktop carries replaces no file itself: `apply` hands the update to the application's own updater, with no window, and the new version's `jason` finishes it", handbook, StringComparison.Ordinal);
        Assert.Contains($"it is refused with `{UpdateCodes.InProgress}` or `{CliErrors.InstallInProgress}`, which mean later: let that one end, then run it again.", handbook, StringComparison.Ordinal);
        Assert.Contains("On macOS and Windows the `jason` Jason Desktop carries hands the update to the application's own updater, and the new version's `jason` takes these steps when it finishes it", Flat(Page("docs", "INSTALL.md")), StringComparison.Ordinal);
        Assert.Contains("On macOS and Windows, `jason update apply` typed into the `jason` the application carries is such a word too: it hands the update to the same updater with no window, and the new version's `jason` finishes it", Flat(Page(".github", "release-notes-template.md")), StringComparison.Ordinal);
    }

    /// <summary>
    /// An install, an update or the desktop's set-up that stops the runtime leaves a note that a start is owed, and the
    /// install page names that note by the path the command line writes it to.
    /// </summary>
    [Fact]
    public async Task The_install_page_names_the_note_of_a_start_that_is_owed_and_both_ways_it_goes()
    {
        using var dir = new TempPaths();
        var relative = Path.GetRelativePath(dir.Paths.Root, dir.Paths.StartOwedFile).Replace('\\', '/');
        Assert.Equal("run/start-owed.json", relative);

        var start = Flat(Section(Page("docs", "INSTALL.md"), "## 6. Start it, and keep it started"));
        Assert.Contains($"An install, an update or Jason Desktop's set-up that stops the runtime leaves a note that a start is owed, `<data>/{relative}`, and Jason Desktop's next start starts one from the `jason` it carries where it finds the note and no runtime answering", start, StringComparison.Ordinal);
        Assert.Contains("The note goes in two ways: when a runtime starts on that data directory again, and when you stop the runtime yourself with `jason runtime stop`, so a stop of your own is never taken for one an install owes.", start, StringComparison.Ordinal);

        // The second way is the stop's own: a person's `jason runtime stop` that succeeded takes the note back.
        Directory.CreateDirectory(dir.Paths.RunDirectory);
        File.WriteAllText(dir.Paths.StartOwedFile, "{\"stopped_at\":\"2026-11-17T15:05:35Z\"}");
        dir.WriteDescriptor(Commands.RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ =>
        {
            File.Delete(dir.Paths.DescriptorFile);
            return Commands.RuntimeVerbs.Response(System.Net.HttpStatusCode.OK, Commands.RuntimeVerbs.ShutdownJson("rt_LIVE"));
        });
        var (person, _, _) = Commands.RuntimeVerbs.Environment(dir, handler, new Process.FakeProcessControl());
        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "stop"], person, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(dir.Paths.StartOwedFile), "a person's own stop left the note that a start is owed");
    }

    /// <summary>
    /// The Command Line item is the machine set-up's status on macOS and Windows and the PATH command on Linux; the README
    /// says which, and no longer says the item puts a folder on the PATH on macOS. The desktop's own split is held by
    /// <c>desktop/test/one-installation-pages.test.ts</c>.
    /// </summary>
    [Fact]
    public void The_readme_says_what_the_command_line_item_is_on_each_system()
    {
        var readme = Flat(Page("README.md"));
        Assert.Contains("on macOS and Windows the first start of each version puts `jason` there and gives Claude Code and Codex Jason's skills, each where it is found, and the Command Line item of its menu says what that placed and puts it back; on Linux that item puts a command there", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("and on macOS and Linux puts its folder on your terminal's PATH", readme, StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS and Windows a command-line install of an earlier version moves into Jason Desktop through its own update, and
    /// the pages say what a person sees. Each claim is held to a real run of the move: on a system with no application (Linux)
    /// it does not apply; on Windows a jason whose own signature does not verify fetches nothing, changes nothing and points to
    /// the release page; <c>jason status</c> names it in its <c>move</c> check; and a rollback points to the release page's
    /// section on going back from 0.49, which is there.
    /// </summary>
    [Fact]
    public async Task The_pages_say_how_a_command_line_install_moves_into_Jason_Desktop()
    {
        var ct = TestContext.Current.CancellationToken;
        using (var rig = new Update.MoveRig(windows: true))
        {
            var answer = await new CommandLineMove(rig.Env with { Setup = null }, rig.Update, TimeProvider.System).RunAsync(rig.Complete(), rig.Installation.Feed, ct);
            Assert.Equal(MoveStates.NotApplicable, answer.State);
            Assert.Contains("on macOS and Windows only", answer.Reason!, StringComparison.Ordinal);
        }

        using (var rig = new Update.MoveRig(windows: true))
        {
            rig.Installation.WithRuntime();
            rig.System.SelfSignedBy = null;
            var fetches = rig.Installation.Release.Requested.Count;
            var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, ct);
            Assert.Equal(MoveStates.Failed, answer.State);
            Assert.Equal(fetches, rig.Installation.Release.Requested.Count);
            Assert.Contains("nothing was downloaded or changed, and the command-line install answers as jason as it did", answer.Reason!, StringComparison.Ordinal);
            Assert.Contains("Install Jason Desktop yourself from", answer.Reason!, StringComparison.Ordinal);
            Assert.Contains("its first start takes the command-line install in", answer.Reason!, StringComparison.Ordinal);
        }

        using var dir = new TempPaths();
        Assert.Equal("move", StatusChecks.Move(new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths)).Name);
        Assert.EndsWith("/docs/release-and-update.md#going-back-from-049", PublicRepository.GoingBackFrom049, StringComparison.Ordinal);
        Assert.Contains("\n### Going back from 0.49\n", Page("docs", "release-and-update.md"), StringComparison.Ordinal);

        var install = Flat(Page("docs", "INSTALL.md"));
        Assert.Contains("On macOS and Windows, a command-line install of an earlier version moves into Jason Desktop through its own update: `jason update apply` updates it to this version, then installs Jason Desktop, or takes one of 0.49.0 or later that is already there, and moves the command line into it", install, StringComparison.Ordinal);
        Assert.Contains("The update never fails because of the move. Where the move cannot go on, the command-line install is left as it was and still answers as `jason`, `jason status` says where the move stands in its `move` check", install, StringComparison.Ordinal);
        Assert.Contains("On Windows the move first verifies this `jason`'s own signature; where that does not verify, it fetches nothing and says to install Jason Desktop from the release page, whose first start takes the command-line install in.", install, StringComparison.Ordinal);
        Assert.Contains("Once the install is moved, `jason update rollback` does not take it back ([release-and-update.md](release-and-update.md), \"Going back from 0.49\"). On Linux there is no move", install, StringComparison.Ordinal);

        Assert.Contains("On macOS and Windows a command-line install of an earlier version moves into Jason Desktop through that same `apply`", Flat(Page("docs", "handbook.md")), StringComparison.Ordinal);
        Assert.Contains("A command-line install of an earlier version on macOS or Windows moves into Jason Desktop through its own `jason update apply`, which installs the application", Flat(Page("docs", "desktop.md")), StringComparison.Ordinal);
        Assert.Contains("A command-line install of an earlier version on macOS or Windows moves into Jason Desktop through its own `jason update apply`: it installs the application and moves the command line into it, and the update never fails because of the move; `jason status` says where the move stands. Linux has no move.", Flat(Page(".github", "release-notes-template.md")), StringComparison.Ordinal);
    }

    /// <summary>
    /// On macOS and Windows the install script's set-up teaches the role skills from the folder beside the jason the
    /// application carries, with no network, as the routine reads them; §1 of the install page says so, and keeps the fetch of
    /// the release's package for Linux and for a later <c>jason skills install</c>.
    /// </summary>
    [Fact]
    public void The_install_page_says_the_role_skills_come_with_the_application_on_macOS_and_Windows()
    {
        var setup = Page("dotnet", "src", "Jason.Cli", "Installation", "MachineSetup.cs");
        Assert.Contains("// The folder beside this jason, read where it stands: no download, nothing written under the data directory.", setup, StringComparison.Ordinal);
        Assert.Contains("SkillsSource.StageAsync(env with { Out = TextWriter.Null }, Bundle, null, null, cancellationToken)", setup, StringComparison.Ordinal);

        var need = Flat(Section(Page("docs", "INSTALL.md"), "## 1. What you need"));
        Assert.Contains("On macOS and Windows they come with Jason Desktop: the set-up reads them from the folder beside the `jason` it carries, with no network.", need, StringComparison.Ordinal);
        Assert.Contains("On Linux teaching it is a fetch of its own, and so is a later `jason skills install` on any system: a release takes its skills from the package of them it published, `jason-skills.zip`, over the network, at the tag it was published as.", need, StringComparison.Ordinal);
    }

    /// <summary>
    /// Restoring a database by hand starts the version the backup was written by. A command-line install's update kept that
    /// version under <c>update/previous</c>; where Jason Desktop carries jason, the copy of the application the Desktop kept
    /// is that version, as the rollback's own line says, and <c>update/previous</c> there holds what a move retired.
    /// </summary>
    [Fact]
    public void Restoring_a_database_by_hand_names_the_version_before_for_each_kind_of_install()
    {
        Assert.Contains("the copy Jason Desktop kept under its rollback folder", Page("dotnet", "src", "Jason.Cli", "Update", "UpdateRollback.cs"), StringComparison.Ordinal);

        var restoring = Flat(Section(Page("docs", "release-and-update.md"), "### Restoring a database by hand"));
        Assert.Contains("for a backup taken before a migration is the version you were on before the update; a command-line # install's update kept it here: ~/.jason/update/previous/jason runtime start", restoring, StringComparison.Ordinal);
        Assert.Contains("Step 4 is the command line's. **Where Jason Desktop carries `jason`**, on macOS and Windows, the version before the update is the copy of the application Jason Desktop kept under `~/.jason/desktop/rollback/<version>/` (`kept_application` in the ledger): quit Jason Desktop, put that copy back in the place of the application's folder, then run `jason runtime start`.", restoring, StringComparison.Ordinal);
        Assert.Contains("`~/.jason/update/previous/jason` is not that version there: after a move into Jason Desktop it is the command-line executable the move's update replaced, a release before 0.49.", restoring, StringComparison.Ordinal);
    }
}
