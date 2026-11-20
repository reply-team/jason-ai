using System.Runtime.Versioning;
using Jason.Cli.Installation;
using Jason.Cli.Status;
using Jason.Cli.Update;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The move of a command-line install into Jason Desktop on Linux, through the seams alone: where there is apt, the deb
/// installed by apt and set up by the jason it carries; elsewhere, or where an AppImage is already in its place, the AppImage
/// placed in the account's home and set up by the copy of its jason. Where the application cannot be installed, the move is
/// pending, nothing is changed and the command-line install answers as it did. Nothing is downloaded, installed or run on
/// the machine running the suite: apt, the probes and the AppImage's own runtime are stood in for.
/// </summary>
public sealed class CommandLineMoveLinuxTests
{
    private const string LinuxRow = "A Linux row, which CI's Linux leg runs.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Where there is apt and no application: the release's deb downloaded and held to its line, installed by apt, then the
    /// jason the package carries runs the routine under the move's lock, and the runtime it stopped is started from it. apt
    /// is waited for longer than the package lock it is told to wait for, so its own answer comes first: catches a bound
    /// that ends the wait while apt still waits on the lock.
    /// </summary>
    [Fact]
    public async Task Where_there_is_apt_the_deb_is_installed_by_apt_and_its_jason_sets_the_machine_up()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), LinuxRow);
        using var rig = new MoveRig(SetupSystem.Linux);
        rig.Installation.WithRuntime();
        var carried = Path.Combine(rig.DebApplication, "resources", "bundle", "jason");

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Equal(MoveSteps.Started, answer.Step);
        Assert.Equal(rig.DebApplication, answer.Application);
        Assert.Equal(["apt", "routine", "stop", $"launch {carried}"], rig.Acts);
        Assert.True(rig.System.Bounds.Single() > LockWait(MoveSystem.AptOptions(null, null)), $"apt was waited for {rig.System.Bounds.Single()}");
        Assert.Equal([carried, "install", "--retire", rig.Old], rig.Installation.Processes.Runs.Single().Command);
        Assert.Contains(rig.Installation.Release.Requested, path => path.EndsWith("/" + CommandLineMove.DebAsset, StringComparison.Ordinal));
        Assert.DoesNotContain(rig.Installation.Release.Requested, path => path.EndsWith("/" + CommandLineMove.AppImageAsset, StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFileSystemEntries(rig.Update.MoveDownloads));
        Assert.True(rig.Installation.Running);
        Assert.True(rig.Record()!.DoneAndStanding(windows: false));
        Assert.Equal(CheckState.Ok, StatusChecks.Move(rig.Env).State);
    }

    /// <summary>
    /// A package already installed of this version or later is reused as it is: nothing downloaded, apt not run, its jason runs
    /// the routine. An older one is installed over by apt, as an upgrade of a system package is.
    /// </summary>
    [Theory]
    [InlineData("this", false)]
    [InlineData("9.0.0", false)]
    [InlineData("0.48.2", true)]
    public async Task A_package_of_this_version_or_later_is_reused_and_an_older_one_is_installed_over(string there, bool installs)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), LinuxRow);
        using var rig = new MoveRig(SetupSystem.Linux);
        var version = there == "this" ? SemanticVersion.Current.ToString() : there;
        MoveRig.PlantPackage(rig.DebApplication, version);
        var carried = Path.Combine(rig.DebApplication, "resources", "bundle", "jason");

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Equal(installs, rig.Acts.Contains("apt"));
        Assert.Equal(installs, rig.Installation.Release.Requested.Any(path => path.EndsWith("/" + CommandLineMove.DebAsset, StringComparison.Ordinal)));
        Assert.Equal(installs ? SemanticVersion.Current.ToString() : version, CarriedExecutable.Of(carried)!.Version);
        Assert.Equal(carried, rig.Installation.Processes.Runs.Single().Command[0]);
    }

    /// <summary>
    /// Where there is no apt: the release's AppImage downloaded and held to its line, put in the account's Applications folder
    /// by one rename, executable, with nothing staged left beside it; its menu entry written to start that file and hidden once
    /// it is gone, its icons beside it; and the copy of the jason it carries made under the data directory, which runs the
    /// routine and starts the runtime. The entry takes the desktop's links (MimeType) and starts the file with -- before the
    /// address, its path bare wherever nothing in it needs quoting, which xdg-open in every mode starts; else spelled as the
    /// Desktop Entry Specification reads it: TryExec with a string's escapes, Exec quoted, a double quote, a backtick, a dollar
    /// sign and a backslash taking a backslash and a per-cent doubled, then every backslash doubled again. Catches a path a
    /// launcher reads as another file, and an entry that takes no link. A per-cent is spelled as the specification asks, and
    /// GLib's launchers still list no such entry.
    /// </summary>
    [Theory]
    [InlineData("home", "home", "home")]
    [InlineData("""Jo "Q" `b` $x 100% \y""", """Jo "Q" `b` $x 100% \\y""", """Jo \\"Q\\" \\`b\\` \\$x 100%% \\\\y""")]
    [UnsupportedOSPlatform("windows")]
    public async Task Without_apt_the_AppImage_is_put_in_place_by_one_rename_with_its_menu_entry_and_its_copy(string home, string tryExecHome, string execHome)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), LinuxRow);
        using var rig = new MoveRig(SetupSystem.Linux, home: home);
        rig.System.Apt = false;
        rig.Installation.WithRuntime();
        var copy = AppImageCopy.CopyOf(rig.Bundles, SemanticVersion.Current);

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Equal(rig.AppImage, answer.Application);
        Assert.Equal("Jason Desktop " + SemanticVersion.Current, File.ReadAllText(rig.AppImage));
        Assert.Equal((UnixFileMode)0b111_101_101, File.GetUnixFileMode(rig.AppImage));
        Assert.Equal([rig.AppImage], Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(rig.AppImage)!));

        var share = Path.Combine(rig.Home, ".local", "share");
        var entry = File.ReadAllLines(Path.Combine(share, "applications", "jason-desktop.desktop"));
        Assert.Equal(["[Desktop Entry]", $"TryExec={Path.Combine(rig.Installation.Root, tryExecHome, "Applications", AppImageCopy.FileName)}", "MimeType=x-scheme-handler/jason-desktop;"], entry[..3]);
        var exec = Path.Combine(rig.Installation.Root, execHome, "Applications", AppImageCopy.FileName);
        Assert.Contains(home == "home" ? $"Exec={exec} -- %U" : $"Exec=\"{exec}\" -- %U", entry);
        Assert.Single(entry, line => line.StartsWith("TryExec=", StringComparison.Ordinal));
        Assert.Single(entry, line => line.StartsWith("Exec=", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(share, "icons", "hicolor", "512x512", "apps", "jason-desktop-app.png")));

        Assert.Equal(SemanticVersion.Current.ToString(), CarriedExecutable.Of(copy)!.Version);
        Assert.Equal(["routine", "stop", $"launch {copy}"], rig.Acts);
        Assert.Equal(copy, rig.Installation.Processes.Runs.Single().Command[0]);
        Assert.Empty(Directory.EnumerateFileSystemEntries(rig.Update.MoveDownloads));
        Assert.True(rig.Installation.Running);
        Assert.Equal(CheckState.Ok, StatusChecks.Move(rig.Env).State);
    }

    /// <summary>
    /// An AppImage already at its place stays the installation, apt or not, as the install line keeps it: its version read out
    /// of it, the copy of its jason made for that version, and nothing downloaded or installed beside it.
    /// </summary>
    [Fact]
    public async Task An_AppImage_already_in_its_place_is_reused_at_its_own_version_even_where_there_is_apt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), LinuxRow);
        using var rig = new MoveRig(SetupSystem.Linux);
        Directory.CreateDirectory(Path.GetDirectoryName(rig.AppImage)!);
        File.WriteAllText(rig.AppImage, "Jason Desktop 9.0.0");
        var copy = AppImageCopy.CopyOf(rig.Bundles, SemanticVersion.Parse("9.0.0"));

        var answer = await rig.Move().RunAsync(rig.Complete(), rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Done, answer.State);
        Assert.Equal(rig.AppImage, answer.Application);
        Assert.Equal("Jason Desktop 9.0.0", File.ReadAllText(rig.AppImage));
        Assert.DoesNotContain("apt", rig.Acts);
        Assert.DoesNotContain(rig.Installation.Release.Requested, path => path.EndsWith("/" + CommandLineMove.DebAsset, StringComparison.Ordinal) || path.EndsWith("/" + CommandLineMove.AppImageAsset, StringComparison.Ordinal));
        Assert.False(Directory.Exists(rig.DebApplication));
        Assert.Equal("9.0.0", CarriedExecutable.Of(copy)!.Version);
        Assert.Equal(copy, rig.Installation.Processes.Runs.Single().Command[0]);
    }

    /// <summary>
    /// Where the application cannot be installed or set up now — apt asked and no password was given, no session to ask one
    /// in, a system that refuses user namespaces, no FUSE, Jason Desktop running, an AppImage older than this version or one
    /// whose version cannot be read — the move is pending and says why; nothing is installed, copied or left downloaded, the
    /// command-line install is untouched and its runtime runs; and <c>jason status</c> names the repair that can work, or none
    /// where nothing typed here changes it. What the look refuses downloads nothing.
    /// </summary>
    [Theory]
    [InlineData("no_password", "needs an administrator's password once, and none was given", true)]
    [InlineData("no_session", "this session cannot ask for it", true)]
    [InlineData("apt_unended", "apt had not ended after 20 minutes", true)]
    [InlineData("user_namespaces", "user namespaces are refused", false)]
    [InlineData("no_fuse", "needs FUSE 3 here", true)]
    [InlineData("running", "is running", true)]
    [InlineData("older_appimage", "is older than", false)]
    [InlineData("not_jason_appimage", "is not a Jason Desktop whose version can be read", false)]
    public async Task Where_Jason_Desktop_cannot_be_installed_the_move_is_pending_and_the_command_line_answers_as_it_did(string why, string said, bool repaired)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), LinuxRow);
        using var rig = new MoveRig(SetupSystem.Linux);
        rig.Installation.WithRuntime();
        switch (why)
        {
            case "no_password":
                rig.System.OnPackage = _ => new PackageRun(true, 126, "Error executing command as another user: Request dismissed\n");
                break;
            case "no_session":
                rig.System.OnPackage = _ => new PackageRun(false, 127, string.Empty);
                break;
            case "apt_unended":
                rig.System.OnPackage = _ => new PackageRun(true, PackageRun.TimedOut, string.Empty);
                break;
            case "user_namespaces":
                rig.System.Apt = false;
                rig.System.UserNamespaces = false;
                break;
            case "no_fuse":
                rig.System.Apt = false;
                rig.System.Fuse = false;
                break;
            case "running":
                rig.System.RunningNamed.Add("jason-desktop-app");
                break;
            case "older_appimage":
                Directory.CreateDirectory(Path.GetDirectoryName(rig.AppImage)!);
                File.WriteAllText(rig.AppImage, "Jason Desktop 0.48.2");
                break;
            case "not_jason_appimage":
                Directory.CreateDirectory(Path.GetDirectoryName(rig.AppImage)!);
                File.WriteAllText(rig.AppImage, "somebody else's program");
                break;
        }

        var ledger = rig.Complete();
        var before = File.ReadAllBytes(rig.Old);

        var answer = await rig.Move().RunAsync(ledger, rig.Installation.Feed, Ct);

        Assert.Equal(MoveStates.Pending, answer.State);
        Assert.Contains(said, answer.Reason!, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(rig.Old));
        Assert.True(rig.Installation.Running);
        Assert.DoesNotContain("routine", rig.Acts);
        Assert.False(Directory.Exists(rig.DebApplication));
        Assert.Equal(why is "older_appimage" or "not_jason_appimage", File.Exists(rig.AppImage));
        Assert.False(Directory.Exists(rig.Bundles) && Directory.EnumerateFileSystemEntries(rig.Bundles).Any());
        Assert.False(Directory.Exists(rig.Update.MoveDownloads) && Directory.EnumerateFileSystemEntries(rig.Update.MoveDownloads).Any());
        if (why is not ("no_password" or "no_session" or "apt_unended"))
        {
            Assert.DoesNotContain(rig.Installation.Release.Requested, path => path.EndsWith("/" + CommandLineMove.DebAsset, StringComparison.Ordinal) || path.EndsWith("/" + CommandLineMove.AppImageAsset, StringComparison.Ordinal));
        }

        var check = StatusChecks.Move(rig.Env);
        Assert.Contains(answer.Reason!, check.Fact, StringComparison.Ordinal);
        Assert.Equal(repaired ? CheckState.Failed : CheckState.Unknown, check.State);
        Assert.Equal(repaired ? "jason update apply" : null, check.Fix);
    }

    /// <summary>
    /// apt's run, read from the words of sudo, sudo-rs (Ubuntu 26.04's), pkexec and apt as the install line reads them, never
    /// from an exit code alone; a run still going at its bound first, then the package lock, which apt waits on after the
    /// password was given. Catches sudo-rs's wrong passwords and a machine with no polkit daemon read as apt failing, a lock
    /// apt gave up on read as no password given, and a run still going at its bound, which may yet change the package, read as
    /// no password given or as the lock.
    /// </summary>
    [Theory]
    [InlineData(true, 126, "Error executing command as another user: Request dismissed", AptRefusal.NoPassword)]
    [InlineData(true, 1, "sudo-rs: Maximum 3 incorrect authentication attempts", AptRefusal.NoPassword)]
    [InlineData(true, PackageRun.TimedOut, "", AptRefusal.Unended)]
    [InlineData(true, PackageRun.TimedOut, "Waiting for cache lock: Could not get lock /var/lib/dpkg/lock-frontend. It is held by process 1234 (unattended-upgr)...", AptRefusal.Unended)]
    [InlineData(true, 127, "Error getting authority: Error initializing authority: Could not connect: No such file or directory", AptRefusal.NoSession)]
    [InlineData(false, 127, "", AptRefusal.NoSession)]
    [InlineData(true, 127, "GDBus.Error: No session for cookie", AptRefusal.DialogFailed)]
    [InlineData(true, 100, "E: Could not get lock /var/lib/dpkg/lock-frontend. It is held by process 1234 (unattended-upgr)", AptRefusal.LockHeld)]
    [InlineData(true, 100, "E: Sub-process /usr/bin/dpkg returned an error code (1)", AptRefusal.Apt)]
    public void On_linux_apt_s_run_is_read_from_the_tools_own_words(bool asked, int exit, string said, AptRefusal refusal) =>
        Assert.Equal(refusal, new PackageRun(asked, exit, said).Refusal);

    /// <summary>How long apt is told to wait for the package lock, in the arguments it is given.</summary>
    internal static TimeSpan LockWait(IEnumerable<string> arguments) =>
        TimeSpan.FromSeconds(int.Parse(arguments.Single(argument => argument.StartsWith("DPkg::Lock::Timeout=", StringComparison.Ordinal))["DPkg::Lock::Timeout=".Length..], System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>
    /// The move's own look for an open Jason Desktop, on this machine: the deb's window under its rewritten title (one string,
    /// the program and its switch) and the name the system keeps of it, found as the uninstall's look finds it. Catches the
    /// move's refusal to install over an open window never firing on Linux.
    /// </summary>
    [Fact]
    public void On_linux_the_move_s_own_look_finds_jason_desktop_open()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The look is Linux's here.");
        using var tree = new TempPaths();
        var image = Path.Combine(tree.Paths.Root, "opt", "Jason", "jason-desktop-app");
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.Copy("/usr/bin/perl", image);
        var start = new System.Diagnostics.ProcessStartInfo(image) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-e", $"$0 = '{image} --'; open(my $c, '>', '/proc/self/comm'); print $c 'jason-desktop-app'; close $c; sleep 120" })
        {
            start.ArgumentList.Add(argument);
        }

        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            var seen = false;
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < until && !seen)
            {
                Thread.Sleep(100);
                seen = MoveSystem.Instance.RunsNamed("jason-desktop-app");
            }

            Assert.True(seen, "The move's look did not find the window's process.");
        }
        finally
        {
            child.Kill();
            child.WaitForExit();
        }
    }
}
