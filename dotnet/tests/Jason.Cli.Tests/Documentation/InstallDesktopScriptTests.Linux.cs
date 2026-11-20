using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Documentation;

/// <summary>
/// install.sh's Linux routes, run by sh against a feed of the test's own: with apt, the release's deb installed by apt with
/// one password, then set up by the jason the package carries; without apt, the AppImage placed per account, its menu
/// entry, icons and the copy of its jason written, then set up by that copy; and the refusals of each, said before
/// anything is downloaded or changed.
/// </summary>
/// <remarks>
/// Nothing real is installed or asked. The commands that decide the route and do the privileged act are stand-ins, and
/// the PATH holds them and links to the system's own tools the script needs and nothing else, so a row decides which of
/// apt-get, dpkg, pkexec, unshare and fusermount3 the machine has. The stand-in pkexec runs the stand-in apt-get, which
/// "installs" the deb by putting a stand-in carried jason in the place the script's copy names as the deb's; or says what
/// a row gives it on stderr and exits as the real one does. The AppImage is a shell script that answers
/// --appimage-extract as the AppImage runtime does, with the files the real one holds at those paths. The carried jason
/// is the macOS rows' stand-in: it logs each call and prints a row's report. A terminal (/dev/tty) is never there under
/// the test runner, so sudo's half of the apt route is CI's apt-route leg's, which gives it one.
/// </remarks>
public sealed partial class InstallDesktopScriptTests
{
    private const string LinuxVersion = "0.49.1";

    /// <summary>
    /// With apt and no terminal - an agent runs the line in the person's session - the deb is downloaded, checked, and
    /// installed by apt through pkexec, whose dialog asks the password once, with the package lock waited for ten minutes
    /// and the whole run for longer, so apt's own answer comes first; then the jason the package carries sets the machine up,
    /// and the last line is the full path of jason. Catches a bound that ends the wait while apt still waits on the lock.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "install.sh installs the deb on Linux with apt only once its checksum holds, and sets it up")]
    [UnsupportedOSPlatform("windows")]
    public void Linux_with_apt_the_deb_is_installed_by_apt_through_the_desktop_s_dialog_then_set_up()
    {
        var linux = Linux.Start(_tree, apt: true);

        var run = linux.Run(linux.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        var calls = linux.Calls();
        var install = Assert.Single(calls, call => call.StartsWith("pkexec ", StringComparison.Ordinal));
        Assert.Contains("apt-get install -y -o DPkg::Lock::Timeout=600 ", install, StringComparison.Ordinal);
        Assert.EndsWith("/jason-desktop-linux-x64.deb|", install, StringComparison.Ordinal);
        var bound = Assert.Single(calls, call => call.StartsWith("timeout ", StringComparison.Ordinal));
        Assert.True(int.Parse(bound["timeout ".Length..^1], System.Globalization.CultureInfo.InvariantCulture) > 600, bound);
        Assert.Equal($"install --roles|{linux.Data}", calls[^1]);
        Assert.True(File.Exists(linux.DebCarried), run.Stdout + run.Stderr);
        Assert.Empty(linux.InApplications());
        Assert.Equal($"A terminal opened from now on finds jason by name; this one, and an agent's session already running, call it by its full path: {Path.Combine(linux.Home, ".local", "bin", "jason")}", Lines(run.Stdout)[^1]);
    }

    /// <summary>
    /// Where the password could not be asked or was not given, the line refuses in the words of the case, read from pkexec's
    /// and apt's own words and never from an exit code alone: the dialog dismissed (and, for an account that is not an
    /// administrator, who can type a password there), no agent to ask in, the system's dialog failing, the package lock held
    /// for ten minutes (apt's own words, also where the line's bound passed while apt waited), the dialog left unanswered
    /// until the line's bound ended it, no pkexec at all, and apt itself failing. Nothing is set up after any of them.
    /// Catches apt's lock read as an unanswered dialog, and an unanswered dialog said as an apt that may yet install.
    /// </summary>
    [Theory]
    [InlineData("Error executing command as another user: Request dismissed", 126, "user sudo", "The install needs an administrator's password once, and none was given, so nothing was installed. Run the line again to be asked again.")]
    [InlineData("Error executing command as another user: Not authorized", 127, "user", "This account is not an administrator: an administrator of this computer can type their password in that dialog, or install the package for you.")]
    [InlineData("Error executing command as another user: No authentication agent found.", 127, "user sudo", "this session has no way to ask for it: run the line in a terminal of your desktop")]
    [InlineData("Error getting authority: Error initializing authority: Could not connect: No such file or directory", 127, "user sudo", "this session has no way to ask for it: run the line in a terminal of your desktop")]
    [InlineData("sudo-rs: Maximum 3 incorrect authentication attempts", 1, "user sudo", "The install needs an administrator's password once, and none was given, so nothing was installed.")]
    [InlineData("GDBus.Error:org.freedesktop.DBus.Error.NoReply: Message recipient disconnected from message bus without replying", 127, "user sudo", "The system's password dialog failed, so nothing was installed. Sign out and back in (or restart), then run the line again.")]
    [InlineData("E: Could not get lock /var/lib/dpkg/lock-frontend. It is held by process 1234 (unattended-upgr)", 100, "user sudo", "Another installation (Ubuntu's own updates, likely) held the package system for ten minutes, so nothing was installed")]
    [InlineData("E: Could not get lock /var/lib/dpkg/lock-frontend. It is held by process 1234 (unattended-upgr)", 124, "user sudo", "Another installation (Ubuntu's own updates, likely) held the package system for ten minutes, so nothing was installed")]
    [InlineData("", 124, "user sudo", "The password dialog was not answered within 20 minutes, so nothing was installed. Run the line again to be asked again.")]
    [InlineData("E: Sub-process /usr/bin/dpkg returned an error code (1)", 100, "user sudo", "Jason Desktop was not installed whole: apt said why above. sudo dpkg --configure -a finishes it, or sudo apt purge jason-desktop takes it off.")]
    [InlineData(null, 0, "user sudo", "this session has no way to ask for it: run the line in a terminal of your desktop")]
    [UnsupportedOSPlatform("windows")]
    public void Linux_with_apt_a_password_not_asked_or_not_given_is_refused_in_its_own_words(string? said, int exit, string groups, string refusal)
    {
        var linux = Linux.Start(_tree, apt: true, pkexec: said is not null);
        linux.Variables["STUB_GROUPS"] = groups;
        if (said is not null)
        {
            linux.Variables["STUB_PKEXEC_SAYS"] = said;
            linux.Variables["STUB_PKEXEC_EXIT"] = exit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var run = linux.Run(linux.Feed());

        Assert.NotEqual(0, run.Exit);
        Assert.Contains(refusal, run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(linux.Calls(), call => call.StartsWith("install", StringComparison.Ordinal));
        Assert.False(File.Exists(linux.DebCarried));
    }

    /// <summary>
    /// The bound on the system's dialog ends the wait, not apt, which runs as root once the password is given: where apt
    /// installed the package all the same, the line says so and sets the machine up rather than report that nothing was
    /// installed. Catches a late answer to the dialog, or apt waiting on the package lock, read as no password given.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Linux_with_apt_a_bound_that_ran_out_after_apt_installed_goes_on_to_the_set_up()
    {
        var linux = Linux.Start(_tree, apt: true, pkexec: true);
        linux.Variables["STUB_PKEXEC_AFTER"] = "the bound ran out";

        var run = linux.Run(linux.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.True(File.Exists(linux.DebCarried), run.Stdout + run.Stderr);
        Assert.Equal($"install --roles|{linux.Data}", linux.Calls()[^1]);
    }

    /// <summary>
    /// The package already installed at the release's version downloads nothing and asks nothing: the line sets the
    /// machine up again, which changes nothing in place. With Jason Desktop running, nothing is changed either.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public void Linux_the_package_already_at_the_version_asks_nothing_and_is_set_up_again_while_it_runs()
    {
        var linux = Linux.Start(_tree, apt: true);
        linux.PlaceDeb(LinuxVersion);
        linux.Variables["STUB_RUNNING"] = "1";

        var run = linux.Run(linux.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        Assert.Contains($"Jason Desktop {LinuxVersion} is already installed; nothing to download.", run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain(linux.Calls(), call => call.StartsWith("pkexec", StringComparison.Ordinal));
        Assert.Equal($"install --roles|{linux.Data}", linux.Calls()[^1]);
    }

    /// <summary>A change to the application while it runs is refused, and the window is never closed from here.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [UnsupportedOSPlatform("windows")]
    public void Linux_an_install_over_a_running_jason_desktop_is_refused_and_changes_nothing(bool apt)
    {
        var linux = Linux.Start(_tree, apt);
        if (apt)
        {
            linux.PlaceDeb("0.49.0");
        }

        linux.Variables["STUB_RUNNING"] = "1";

        var run = linux.Run(linux.Feed());

        Assert.NotEqual(0, run.Exit);
        Assert.Contains("Jason Desktop is running, in its window or in the tray, so nothing was installed: quit it - Quit in its tray menu - and run this line again.", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain(linux.Calls(), call => call.StartsWith("pkexec", StringComparison.Ordinal) || call.StartsWith("kill", StringComparison.Ordinal));
        Assert.Empty(linux.InApplications());
    }

    /// <summary>
    /// Without apt the AppImage is the installation, per account and with no password: placed at
    /// ~/Applications/jason-desktop-linux-x64.AppImage by one rename, the menu entry written under ~/.local/share with
    /// TryExec naming the file, so it hides once the file is gone, MimeType declaring the desktop's link scheme, and Exec
    /// starting it with -- before the address, the folder's cache written again so GIO finds it, the icons beside it, and the
    /// jason it carries copied under the data directory as the application's own start copies it, whose `jason install` then
    /// runs. The entry is the one the jason it carries writes (AppImageMenuEntry.Form): the path bare in Exec wherever nothing
    /// in it needs quoting, which xdg-open in every mode starts; else spelled as the Desktop Entry Specification reads it,
    /// TryExec with a string's escapes, Exec quoted, a double quote, a backtick, a dollar sign and a backslash taking a
    /// backslash and a per-cent doubled, then every backslash doubled again. Catches a path a launcher reads as another file,
    /// an entry that takes no link, and the line and the jason writing two forms, a path with a space of another script's
    /// among them, whatever the locale the line runs in. A per-cent is spelled as the specification asks, and GLib's
    /// launchers still list no such entry.
    /// </summary>
    [Theory]
    [InlineData("home", "home", "home", true)]
    [InlineData("""Jo "Q" `b` $x 100% \y""", """Jo "Q" `b` $x 100% \\y""", """Jo \\"Q\\" \\`b\\` \\$x 100%% \\\\y""", false)]
    [InlineData("Jo\u2003Doe", "Jo\u2003Doe", "Jo\u2003Doe", true)]
    [Critical(Family.I1, "install.sh places the AppImage, its menu entry and its copy on Linux with no password")]
    [UnsupportedOSPlatform("windows")]
    public void Linux_without_apt_the_appimage_is_placed_with_its_menu_entry_and_its_copy_then_set_up(string home, string tryExecHome, string execHome, bool bare)
    {
        var linux = Linux.Start(_tree, apt: false, home: home);

        var run = linux.Run(linux.Feed());

        Assert.True(run.Exit == 0, run.Stdout + run.Stderr);
        var placed = Path.Combine(linux.Applications, "jason-desktop-linux-x64.AppImage");
        Assert.Equal([placed], linux.InApplications());
        var applications = Path.Combine(linux.Home, ".local", "share", "applications");
        var written = File.ReadAllText(Path.Combine(applications, "jason-desktop.desktop"));
        var entry = written.Split('\n');
        Assert.Equal("[Desktop Entry]", entry[0]);
        var above = Path.GetDirectoryName(linux.Home)!;
        Assert.Equal($"TryExec={Path.Combine(above, tryExecHome, "Applications", "jason-desktop-linux-x64.AppImage")}", entry[1]);
        Assert.Equal("MimeType=x-scheme-handler/jason-desktop;", entry[2]);
        var exec = Path.Combine(above, execHome, "Applications", "jason-desktop-linux-x64.AppImage");
        Assert.Contains(bare ? $"Exec={exec} -- %U" : $"Exec=\"{exec}\" -- %U", entry);
        Assert.Single(entry, line => line.StartsWith("Exec=", StringComparison.Ordinal));
        Assert.Equal(Jason.Cli.Installation.AppImageMenuEntry.Form(Linux.CarriedEntry, placed), written);
        Assert.Contains($"update-desktop-database {applications}|", linux.Calls());
        Assert.True(File.Exists(Path.Combine(linux.Home, ".local", "share", "icons", "hicolor", "256x256", "apps", "jason-desktop-app.png")));
        var copy = Path.Combine(linux.Data, "desktop", "bundle", LinuxVersion);
        Assert.Equal(["jason", "version.json"], Directory.EnumerateFiles(copy).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal($"install --roles|{linux.Data}", linux.Calls()[^1]);
        Assert.DoesNotContain(linux.Calls(), call => call.StartsWith("pkexec", StringComparison.Ordinal) || call.StartsWith("sudo", StringComparison.Ordinal));
    }

    /// <summary>
    /// Where the AppImage could not run its sandbox or open, or the place holds a file of that name that is not Jason
    /// Desktop, the line refuses before anything is downloaded or written, saying why.
    /// </summary>
    [Theory]
    [InlineData("user namespaces refused", "This system does not let an application make the user namespaces Chromium's sandbox needs, and Jason never runs without its sandbox, so nothing was installed.")]
    [InlineData("no setuid fusermount", "Jason Desktop is an AppImage here, which needs FUSE 3 to open: install the fuse3 package (sudo pacman -S fuse3 on Arch), then run the line again. Nothing was installed.")]
    [InlineData("another file in the place", "A file of that name that is not Jason Desktop is in ")]
    [UnsupportedOSPlatform("windows")]
    public void Linux_without_apt_what_the_appimage_cannot_do_is_refused_before_anything_is_written(string why, string refusal)
    {
        var linux = Linux.Start(_tree, apt: false, userNamespaces: why != "user namespaces refused", fuse: why != "no setuid fusermount");
        if (why == "another file in the place")
        {
            Directory.CreateDirectory(linux.Applications);
            File.WriteAllText(Path.Combine(linux.Applications, "jason-desktop-linux-x64.AppImage"), "#!/bin/sh\nexit 1\n");
            File.SetUnixFileMode(Path.Combine(linux.Applications, "jason-desktop-linux-x64.AppImage"), (UnixFileMode)0b111_101_101);
        }

        var before = Directory.EnumerateFileSystemEntries(linux.Home, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();

        var run = linux.Run(linux.Feed());

        Assert.NotEqual(0, run.Exit);
        Assert.Contains(refusal, run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("Downloading", run.Stdout, StringComparison.Ordinal);
        Assert.Equal(before, Directory.EnumerateFileSystemEntries(linux.Home, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.Empty(linux.Calls());
    }

    /// <summary>
    /// Refused before anything is read, on Linux as on macOS: a run as root, the two options of the command-line install,
    /// and a line inside an agent's own sandbox, which sets NoNewPrivs, so neither sudo, pkexec nor a setuid fusermount could
    /// raise privileges there.
    /// </summary>
    [Theory]
    [InlineData("root", "this script runs as root, and Jason Desktop and everything it sets up are your account's own, so nothing was downloaded or installed: run the install line again as yourself, without sudo.")]
    [InlineData("--install-dir", "--install-dir is not taken on Linux: Jason Desktop is installed as the system installs applications")]
    [InlineData("--no-modify-path", "--no-modify-path is not taken on Linux: every install puts jason in the terminal.")]
    [InlineData("NoNewPrivs", "This command runs inside a sandbox that cannot install software: run the line in your own terminal, or let your agent run it outside its sandbox. Nothing was installed.")]
    [UnsupportedOSPlatform("windows")]
    public void Linux_what_the_line_cannot_do_is_refused_before_anything_is_read(string why, string refusal)
    {
        var linux = Linux.Start(_tree, apt: true);
        string[] arguments = why switch
        {
            "--install-dir" => ["--install-dir", Path.Combine(linux.Home, "bin")],
            "--no-modify-path" => ["--no-modify-path"],
            _ => [],
        };
        if (why == "root")
        {
            linux.Variables["STUB_UID"] = "0";
        }

        var setpriv = new[] { "/usr/bin/setpriv", "/bin/setpriv" }.FirstOrDefault(File.Exists);
        Assert.SkipWhen(why == "NoNewPrivs" && setpriv is null, "setpriv is not on this machine; CI's Ubuntu has it.");

        var run = why == "NoNewPrivs" ? linux.Run(linux.Feed(), ["--no-new-privs", "--"], setpriv) : linux.Run(linux.Feed(), arguments);

        Assert.NotEqual(0, run.Exit);
        Assert.Contains(refusal, run.Stderr, StringComparison.Ordinal);
        Assert.Empty(linux.Calls());
        Assert.Empty(Directory.EnumerateFileSystemEntries(linux.Home));
    }

    /// <summary>install.sh's Linux routes, run by sh with the commands that decide them stood in for and a home of the test's own.</summary>
    [UnsupportedOSPlatform("windows")]
    private sealed class Linux
    {
        /// <summary>The system's own tools the script runs, linked into the PATH a row gives it.</summary>
        private static readonly string[] Tools =
            ["sh", "cat", "cp", "mv", "rm", "mkdir", "mktemp", "chmod", "sed", "grep", "awk", "tr", "cut", "head", "tail", "sha256sum", "basename", "dirname", "sort", "base64"];

        private readonly TempTree _tree;

        private Linux(TempTree tree, bool apt, bool pkexec, bool userNamespaces, bool fuse, string home)
        {
            _tree = tree;
            Home = tree.NewDirectory(home);
            Applications = Path.Combine(Home, "Applications");
            Data = tree.NewDirectory("data");
            Log = Path.Combine(tree.NewDirectory("log"), "calls.log");
            Running = Path.Combine(tree.NewDirectory("flag"), "running");
            DebPlace = Path.Combine(tree.NewDirectory("opt"), "Jason");
            Stubs = tree.NewDirectory("stubs");
            foreach (var tool in Tools)
            {
                var found = new[] { "/usr/bin", "/bin" }.Select(folder => Path.Combine(folder, tool)).FirstOrDefault(File.Exists);
                Assert.SkipWhen(found is null, $"{tool} is not on this machine; CI's Ubuntu has it.");
                File.CreateSymbolicLink(Path.Combine(Stubs, tool), found!);
            }

            Stub("uname", "case \"${1:-}\" in -m) echo x86_64 ;; *) echo Linux ;; esac\n");
            Stub("id", "case \"${1:-}\" in -u) echo \"${STUB_UID:-1000}\" ;; -nG) echo \"${STUB_GROUPS:-user sudo}\" ;; *) exit 1 ;; esac\n");
            Stub("pgrep", "[ -n \"${STUB_RUNNING:-}\" ]\n");
            Stub("timeout", $"echo \"timeout $1|\" >> '{Log}'\nshift\nexec \"$@\"\n");
            Stub("update-desktop-database", $"echo \"update-desktop-database $*|\" >> '{Log}'\n");
            Stub("unshare", userNamespaces ? "exit 0\n" : "echo 'unshare: write failed /proc/self/uid_map: Operation not permitted' >&2\nexit 1\n");
            if (fuse)
            {
                Stub("fusermount3", "exit 0\n");
                File.SetUnixFileMode(Path.Combine(Stubs, "fusermount3"), (UnixFileMode)0b100_111_101_101);
            }

            if (apt)
            {
                // apt-get installs the file by putting the package's carried jason in its place, and dpkg-query says what is
                // installed by the file the stand-in keeps beside that place.
                Stub("apt-get", $"echo \"apt-get $*|\" >> '{Log}'\nmkdir -p '{DebPlace}/resources/bundle'\ncp '{Path.Combine(DebPlace, "..", "jason")}' '{DebCarried}'\nchmod 755 '{DebCarried}'\necho '{LinuxVersion}' > '{DebPlace}.version'\n");
                Stub("dpkg", "[ \"$1\" = --compare-versions ] || exit 0\n[ \"$(printf '%s\\n%s\\n' \"$2\" \"$4\" | sort -V | tail -n 1)\" = \"$2\" ] && [ \"$2\" != \"$4\" ]\n");
                Stub("dpkg-query", $"[ -f '{DebPlace}.version' ] || exit 1\nprintf 'ii |%s' \"$(cat '{DebPlace}.version')\"\n");
            }

            if (pkexec)
            {
                // A dialog nobody answers: timeout ends pkexec at the bound, and pkexec says nothing.
                Stub("pkexec", $"echo \"pkexec $*|\" >> '{Log}'\nif [ \"${{STUB_PKEXEC_EXIT:-}}\" = 124 ] && [ -z \"${{STUB_PKEXEC_SAYS:-}}\" ]; then exit 124; fi\nif [ -n \"${{STUB_PKEXEC_AFTER:-}}\" ]; then \"$@\"; echo \"$STUB_PKEXEC_AFTER\" >&2; exit 124; fi\nif [ -n \"${{STUB_PKEXEC_SAYS:-}}\" ]; then echo \"$STUB_PKEXEC_SAYS\" >&2; exit \"$STUB_PKEXEC_EXIT\"; fi\nexec \"$@\"\n");
            }

            File.WriteAllText(Path.Combine(DebPlace, "..", "jason"), StandInJason);
        }

        public string Home { get; }

        public string Applications { get; }

        public string Data { get; }

        public string Log { get; }

        public string Running { get; }

        /// <summary>Where this row's copy of the script puts the deb's application.</summary>
        public string DebPlace { get; }

        /// <summary>The jason the deb carries, where the script reads it.</summary>
        public string DebCarried => Path.Combine(DebPlace, "resources", "bundle", "jason");

        private string Stubs { get; }

        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

        /// <summary>The stand-in jason: it logs each call and prints a row's report for install --roles.</summary>
        private string StandInJason =>
            "#!/bin/sh\n"
            + $"printf '%s|%s\\n' \"$*\" \"${{JASON_DATA_DIR:-}}\" >> '{Log}'\n"
            + "case \"$1 ${2:-}\" in\n"
            + $"  'runtime status') [ -f '{Running}' ] ;;\n"
            + "  'install --roles') printf '%s\\n' \"${STANDIN_REPORT:-$STANDIN_DEFAULT}\" | sed \"s|{self}|$HOME/.local/bin/jason|g; s|{dir}|$HOME/.local/bin|g\" ;;\n"
            + "esac\n";

        /// <param name="home">The name of the account's home folder, which may hold any character a file name can.</param>
        public static Linux Start(TempTree tree, bool apt, bool pkexec = true, bool userNamespaces = true, bool fuse = true, string home = "home")
        {
            Assert.SkipWhen(!OperatingSystem.IsLinux(), "install.sh's Linux routes are run by sh on Linux; CI's Linux leg runs these rows.");
            return new Linux(tree, apt, pkexec, userNamespaces, fuse, home);
        }

        public List<string> Calls() => File.Exists(Log) ? [.. File.ReadAllLines(Log)] : [];

        /// <summary>What ~/Applications holds; nothing where the folder is not there.</summary>
        public List<string> InApplications() => Directory.Exists(Applications) ? [.. Directory.EnumerateFileSystemEntries(Applications)] : [];

        /// <summary>The package installed already, at a version.</summary>
        public void PlaceDeb(string version)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DebCarried)!);
            File.Copy(Path.Combine(DebPlace, "..", "jason"), DebCarried, overwrite: true);
            File.SetUnixFileMode(DebCarried, (UnixFileMode)0b111_101_101);
            File.WriteAllText(DebPlace + ".version", version);
        }

        /// <summary>The menu entry the AppImage carries, as the release's does in its parts the line reads.</summary>
        public const string CarriedEntry = "[Desktop Entry]\nName=Jason\nExec=AppRun %U\nTerminal=false\nType=Application\nIcon=jason-desktop-app\n";

        /// <summary>
        /// A feed of the release's deb and AppImage, its manifest and checksums. The AppImage answers --appimage-extract as
        /// the runtime does, with the files the script reads at their paths: the bundle (the stand-in jason and its
        /// version.json), the menu entry at the root, and an icon.
        /// </summary>
        public string Feed(string version = LinuxVersion)
        {
            var feed = _tree.NewDirectory("feed");
            File.WriteAllText(Path.Combine(feed, "jason-desktop-linux-x64.deb"), "a deb\n");
            var jason = Convert.ToBase64String(Encoding.UTF8.GetBytes(StandInJason));
            File.WriteAllText(
                Path.Combine(feed, "jason-desktop-linux-x64.AppImage"),
                "#!/bin/sh\n"
                + "[ \"${1:-}\" = --appimage-extract ] || exit 1\n"
                + "r=squashfs-root\n"
                + "mkdir -p \"$r/resources/bundle\" \"$r/usr/share/icons/hicolor/256x256/apps\"\n"
                + $"echo '{jason}' | base64 -d > \"$r/resources/bundle/jason\"\n"
                + "chmod 755 \"$r/resources/bundle/jason\"\n"
                + $"printf '{{\\n  \"version\": \"{version}\"\\n}}\\n' > \"$r/resources/bundle/version.json\"\n"
                + "printf '" + CarriedEntry.Replace("%", "%%", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "' > \"$r/jason-desktop-app.desktop\"\n"
                + "printf 'png' > \"$r/usr/share/icons/hicolor/256x256/apps/jason-desktop-app.png\"\n");
            File.WriteAllText(Path.Combine(feed, ReleaseAssets.Manifest), "{\n  \"schema\": 1,\n  \"version\": \"" + version + "\"\n}\n");
            File.WriteAllText(
                Path.Combine(feed, ReleaseAssets.Checksums),
                string.Concat(new[] { "jason-desktop-linux-x64.deb", "jason-desktop-linux-x64.AppImage" }.Select(name => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(feed, name)))) + "  " + name + "\n")));
            return feed;
        }

        public (int Exit, string Stdout, string Stderr) Run(string feed, string[] arguments, string? wrapper = null)
        {
            var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "install", "install.sh"));
            Assert.Equal(1, Count(text, "\nDEB_PLACE=\"/opt/Jason\"\n"));
            var script = Path.Combine(_tree.NewDirectory("test-build"), "install.sh");
            File.WriteAllText(script, text.Replace("\nDEB_PLACE=\"/opt/Jason\"\n", $"\nDEB_PLACE=\"{DebPlace}\"\n", StringComparison.Ordinal));

            var environment = new Dictionary<string, string>(Variables)
            {
                ["HOME"] = Home,
                ["JASON_DATA_DIR"] = Data,
                // The account's own folders, never the machine's: a test runner's XDG_DATA_HOME is a real home's.
                ["XDG_DATA_HOME"] = Path.Combine(Home, ".local", "share"),
                ["XDG_CONFIG_HOME"] = Path.Combine(Home, ".config"),
                ["PATH"] = Stubs,
                // A person's terminal speaks UTF-8, where gawk's character classes reach past ASCII.
                ["LC_ALL"] = "C.UTF-8",
                ["STANDIN_DEFAULT"] = Report(placed: true, runtime: "not_running", completed: true),
            };
            return wrapper is null
                ? RunToEnd(Path.Combine(Stubs, "sh"), environment, [script, "--feed", feed, .. arguments])
                : RunToEnd(wrapper, environment, [.. arguments, Path.Combine(Stubs, "sh"), script, "--feed", feed]);
        }

        public (int Exit, string Stdout, string Stderr) Run(string feed) => Run(feed, []);

        private void Stub(string name, string body)
        {
            var path = Path.Combine(Stubs, name);
            File.WriteAllText(path, "#!/bin/sh\n" + body);
            File.SetUnixFileMode(path, (UnixFileMode)0b111_101_101);
        }
    }
}
