using System.Runtime.Versioning;
using Jason.Cli.Installation;
using Jason.Cli.Status;
using Jason.Cli.Update;
using Jason.Contracts.Update;

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
    /// jason the package carries runs the routine under the move's lock, and the runtime it stopped is started from it.
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
    /// routine and starts the runtime.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task Without_apt_the_AppImage_is_put_in_place_by_one_rename_with_its_menu_entry_and_its_copy()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), LinuxRow);
        using var rig = new MoveRig(SetupSystem.Linux);
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
        Assert.Equal(["[Desktop Entry]", $"TryExec={rig.AppImage}"], entry[..2]);
        Assert.Contains($"Exec=\"{rig.AppImage}\" %U", entry);
        Assert.Single(entry, line => line.StartsWith("TryExec=", StringComparison.Ordinal));
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
        if (why is not ("no_password" or "no_session"))
        {
            Assert.DoesNotContain(rig.Installation.Release.Requested, path => path.EndsWith("/" + CommandLineMove.DebAsset, StringComparison.Ordinal) || path.EndsWith("/" + CommandLineMove.AppImageAsset, StringComparison.Ordinal));
        }

        var check = StatusChecks.Move(rig.Env);
        Assert.Contains(answer.Reason!, check.Fact, StringComparison.Ordinal);
        Assert.Equal(repaired ? CheckState.Failed : CheckState.Unknown, check.State);
        Assert.Equal(repaired ? "jason update apply" : null, check.Fix);
    }
}
