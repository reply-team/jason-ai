using System.Runtime.Versioning;
using Jason.Cli.Commands;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Tests.Process;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The update of the jason Jason Desktop carries on Linux, in its two forms. The deb's is the system's: `jason update
/// apply` and `rollback` replace nothing and say what to type. An AppImage's copy hands the update to the Desktop, whose
/// application is the AppImage file its start recorded, and once that file is the new version the copy of what it carries
/// is read out of it under that version, whole or not at all. Run against a data directory, a home and files of the test's
/// own; the AppImage is a shell script that answers --appimage-extract as the AppImage runtime does.
/// </summary>
public sealed class UpdateCarriedLinuxTests : IDisposable
{
    private readonly TempPaths _data = new();
    private readonly TempPaths _tree = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        _data.Dispose();
        _tree.Dispose();
    }

    /// <summary>
    /// The deb's jason refuses `jason update apply` and `rollback` with update_managed_by_package_manager before anything is
    /// read or staged, naming the commands that do it through apt; nothing is made under the data directory.
    /// </summary>
    [Theory]
    [InlineData("apply", "sudo apt update && sudo apt upgrade")]
    [InlineData("rollback", "sudo apt install --allow-downgrades ./jason-desktop-linux-x64.deb")]
    public async Task On_linux_the_deb_s_jason_leaves_its_update_to_apt_and_says_how(string verb, string named)
    {
        var resources = Path.Combine(_tree.Paths.Root, "opt", "Jason", "resources");
        Directory.CreateDirectory(Path.Combine(resources, "bundle"));
        File.WriteAllText(Path.Combine(resources, "package-type"), "deb\n");
        var executable = Path.Combine(resources, "bundle", "jason");
        File.WriteAllText(executable, "jason");
        CarriedRecord.Beside(executable);
        var (env, output) = Environment(executable, display: true);

        var exit = verb == "apply"
            ? await UpdateCommands.ApplyAsync(env, feed: null, version: null, drainSeconds: null, human: false, finish: false, Ct)
            : await UpdateCommands.RollBackAsync(env, human: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(UpdateCodes.ManagedByPackageManager, refusal.Code);
        Assert.Contains(named, refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_data.Paths.Root, "update")), "the refusal made the update's folder");
    }

    /// <summary>
    /// An AppImage's copy names the AppImage file its application's start recorded beside the copies, wherever the person
    /// keeps it; with no record, the install line's place in the home. The deb's jason names the deb's own program.
    /// </summary>
    [Fact]
    public void On_linux_the_application_is_the_file_an_appimage_s_start_recorded_or_the_deb_s_program()
    {
        var copy = Copy("0.49.1");
        Assert.Equal(
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), "Applications", "jason-desktop-linux-x64.AppImage"),
            CarriedUpdate.ApplicationOf(copy, SetupSystem.Linux));

        var kept = Path.Combine(_tree.Paths.Root, "elsewhere", "Jason.AppImage");
        File.WriteAllText(Path.Combine(_data.Paths.Root, "desktop", "appimage.json"), $"{{\"file\":\"{kept}\"}}\n");
        Assert.Equal(kept, CarriedUpdate.ApplicationOf(copy, SetupSystem.Linux));
        Assert.Equal(kept, CarriedUpdate.ApplicationFolderOf(copy, SetupSystem.Linux));

        var resources = Path.Combine(_tree.Paths.Root, "opt", "Jason", "resources");
        Directory.CreateDirectory(Path.Combine(resources, "bundle"));
        File.WriteAllText(Path.Combine(resources, "package-type"), "deb\n");
        Assert.Equal(Path.Combine(_tree.Paths.Root, "opt", "Jason", "jason-desktop-app"), CarriedUpdate.ApplicationOf(Path.Combine(resources, "bundle", "jason"), SetupSystem.Linux));
    }

    /// <summary>
    /// The AppImage whose own runtime is started is the file this names, or one staged beside it, so it is named by its full
    /// path whatever the record beside the copies says: a record naming a path that is not a full one is not taken.
    /// </summary>
    [Fact]
    public void The_appimage_is_named_by_its_full_path_whatever_its_record_says()
    {
        var copy = Copy("0.49.1");
        var home = Path.Combine(_tree.Paths.Root, "home");
        File.WriteAllText(Path.Combine(_data.Paths.Root, "desktop", "appimage.json"), "{\"file\":\"Applications/Jason.AppImage\"}\n");

        var named = AppImageCopy.FileOf(copy, home);

        Assert.Equal(Path.Combine(home, "Applications", AppImageCopy.FileName), named);
        Assert.True(Path.IsPathFullyQualified(named));
    }

    /// <summary>
    /// The copy of the new version is read out of the AppImage file by its own runtime only where the file carries that
    /// version, and renamed into place whole beside the other copies; a file still the old version leaves no copy and no
    /// folder of its reading behind.
    /// </summary>
    [Theory]
    [InlineData("0.49.2", true)]
    [InlineData("0.49.1", false)]
    [UnsupportedOSPlatform("windows")]
    public async Task On_linux_the_new_copy_is_read_out_of_the_appimage_only_once_it_carries_that_version(string carries, bool made)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "An AppImage is a Linux program; CI's Linux leg runs this row.");
        var copy = Copy("0.49.1");
        var bundles = AppImageCopy.BundlesOf(copy);
        var file = AppImage(carries);

        var held = await AppImageCopy.MakeAsync(file, bundles, SemanticVersion.Parse("0.49.2"), Ct);

        Assert.Equal(made, held);
        Assert.Equal(made ? ["0.49.1", "0.49.2"] : ["0.49.1"], Directory.EnumerateFileSystemEntries(bundles).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        if (made)
        {
            Assert.Equal(["jason", "version.json"], Directory.EnumerateFiles(Path.Combine(bundles, "0.49.2")).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        }
    }

    /// <summary>
    /// An AppImage's copies are a file each, under the version (<c>bundle/&lt;version&gt;/jason</c>). Once the AppImage is
    /// the new version, the record names the copy of that version as the installation's jason, the one the finish starts the
    /// runtime from and sets the machine up through; and any copy of the installation, the old one a terminal's wrapper still
    /// runs among them, carries the update on, while a copy under another data directory does not. Catches the new copy
    /// refusing the update as another jason's (update_in_progress, left at 'swapped'), and the finish starting the old copy's
    /// runtime (update_not_healthy, left at 'healthy'). And where the process carrying the update ends there, the way out of
    /// the process that started it starts the runtime the update stopped from the new copy, never from the old one the
    /// process itself is: catches the old version's runtime started beside the new application.
    /// </summary>
    [Fact]
    public async Task On_linux_the_appimage_s_record_names_the_new_copy_at_the_swap_and_every_copy_carries_it_on()
    {
        var old = Copy("0.49.1");
        var next = Copy("0.49.2");
        var kept = new UpdateLedger(SemanticVersion.Parse("0.49.1"), SemanticVersion.Parse("0.49.2"), UpdateStep.Kept, DateTimeOffset.UtcNow, old, old, old);

        var ledger = Handing(old).Swapped(kept);

        Assert.Equal(UpdateStep.Swapped, ledger.Step);
        Assert.Equal(next, ledger.InstallPath);
        var elsewhere = Path.Combine(_tree.Paths.Root, "other", "desktop", "bundle", "0.49.2", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(elsewhere)!);
        File.WriteAllText(elsewhere, "jason");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(elsewhere)!, CarriedExecutable.RecordName), "{\n  \"version\": \"0.49.2\"\n}\n");

        Assert.True(Handing(next).Carries(ledger), "the copy of the version the update goes to did not carry it on");
        Assert.True(Handing(old).Carries(ledger), "the copy that began the update did not carry it on");
        Assert.False(Handing(elsewhere).Carries(ledger), "another data directory's copy carried this installation's update on");

        var processes = new FakeProcessControl();
        await Handing(old, processes).RecoverAsync(wasLive: true, waits: false);
        Assert.Equal([next], processes.Executables.Single());
    }

    /// <summary>
    /// The hand-over's own way out once an AppImage's update has landed, here the record at the swap failing to be written: the
    /// runtime the update stopped is started from the new version's copy, never from the old copy this process is. Jason
    /// Desktop is stood in: it stops the runtime, hands the update to its installer, and the file is then the new version.
    /// Catches the old version's runtime started beside the new application on that way out.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task On_linux_an_appimage_s_hand_over_that_fails_once_it_landed_starts_the_runtime_from_the_new_copy()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "An AppImage is a Linux program; CI's Linux leg runs this row.");
        var old = Copy("0.49.1");
        var next = AppImageCopy.CopyOf(AppImageCopy.BundlesOf(old), SemanticVersion.Parse("0.49.2"));
        var file = Path.Combine(_tree.Paths.Root, "Jason.AppImage");
        File.Copy(AppImage("0.49.1"), file);
        File.WriteAllText(Path.Combine(_data.Paths.Root, "desktop", "appimage.json"), $"{{\"file\":\"{file}\"}}\n");
        var update = new UpdatePaths(_data.Paths);
        Directory.CreateDirectory(update.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        processes.OnRun = (_, command, _, _) =>
        {
            var id = command[command.ToList().IndexOf("--request") + 1];
            File.Delete(_data.Paths.DescriptorFile);
            processes.RunningPids.Remove(RuntimeVerbs.Pid);
            File.Copy(AppImage("0.49.2"), file, overwrite: true);
            File.WriteAllText(
                Path.Combine(update.Root, $"desktop-{id}.json"),
                $$"""{"request":"{{id}}","state":"installing","from":"0.49.1","to":"0.49.2","reason":null,"message":null,"offered":null,"window":false,"runtime_stopped":true,"kept":null,"updated_at":"2026-11-19T12:00:00.000Z"}""");
            return Task.FromResult(new FinishedCommand(0, string.Empty));
        };
        processes.OnLaunch = _ =>
        {
            _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_AGAIN", pid: 5151));
            processes.RunningPids.Add(5151);
            return new FakeProcessHandle(5151);
        };
        var handing = new CarriedUpdate(Environment(old, display: true).Env with { Processes = processes }, update, TimeProvider.System, CarriedExecutable.Of(old)!, SetupSystem.Linux)
        {
            Writes = ledger =>
            {
                if (ledger.Step == UpdateStep.Swapped)
                {
                    throw new IOException("There is not enough space on the disk.");
                }

                ledger.Write(update.Ledger);
            },
        };
        var staged = new UpdateLedger(SemanticVersion.Parse("0.49.1"), SemanticVersion.Parse("0.49.2"), UpdateStep.Staged, DateTimeOffset.UtcNow, old, old, old);

        var refused = await Assert.ThrowsAsync<UpdateException>(() => handing.InstallAsync(staged, new UpdateRequest(new Uri("https://example.test/manifest.json"), null, TimeSpan.Zero), Ct));

        Assert.Equal(UpdateCodes.FileRefused, refused.Code);
        Assert.Contains("The application is 0.49.2", refused.Message, StringComparison.Ordinal);
        Assert.Equal([next], processes.Executables.Single());
    }

    private CarriedUpdate Handing(string executable, FakeProcessControl? processes = null) =>
        new(Environment(executable, display: true).Env with { Processes = processes }, new UpdatePaths(_data.Paths), TimeProvider.System, CarriedExecutable.Of(executable)!, SetupSystem.Linux);

    /// <summary>
    /// From a terminal with no display (an ssh session, a service), the AppImage's update is refused before anything is
    /// read or staged: the Desktop it would start to take the update needs one.
    /// </summary>
    [Fact]
    [UnsupportedOSPlatform("windows")]
    public async Task On_linux_an_appimage_s_update_from_a_terminal_with_no_display_is_refused_and_changes_nothing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "An AppImage is a Linux program; CI's Linux leg runs this row.");
        var copy = Copy("0.49.1");
        var file = AppImage("0.49.1");
        File.WriteAllText(Path.Combine(_data.Paths.Root, "desktop", "appimage.json"), $"{{\"file\":\"{file}\"}}\n");
        var (env, output) = Environment(copy, display: false);

        var exit = await UpdateCommands.ApplyAsync(env, feed: null, version: null, drainSeconds: null, human: false, finish: false, Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(UpdateCodes.CarriedByDesktop, refusal.Code);
        Assert.Contains("No desktop session is running for this account to update Jason Desktop in", refusal.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_data.Paths.Root, "update")), "the refusal made the update's folder");
    }

    /// <summary>An AppImage's copy of a version under the data directory, with its version.json.</summary>
    private string Copy(string version)
    {
        var folder = Path.Combine(_data.Paths.Root, "desktop", "bundle", version);
        Directory.CreateDirectory(folder);
        var executable = Path.Combine(folder, "jason");
        File.WriteAllText(executable, "jason");
        File.WriteAllText(Path.Combine(folder, CarriedExecutable.RecordName), $"{{\n  \"version\": \"{version}\"\n}}\n");
        return executable;
    }

    /// <summary>An AppImage stand-in carrying a version: run with --appimage-extract it writes what the real one does.</summary>
    [UnsupportedOSPlatform("windows")]
    private string AppImage(string version)
    {
        var file = Path.Combine(_tree.Paths.Root, $"Jason-{version}.AppImage");
        File.WriteAllText(
            file,
            "#!/bin/sh\n"
            + "[ \"${1:-}\" = --appimage-extract ] || exit 1\n"
            + "mkdir -p squashfs-root/resources/bundle\n"
            + "echo jason > squashfs-root/resources/bundle/jason\n"
            + $"printf '{{\\n  \"version\": \"{version}\"\\n}}\\n' > squashfs-root/resources/bundle/version.json\n");
        File.SetUnixFileMode(file, (UnixFileMode)0b111_101_101);
        return file;
    }

    private (CliEnvironment Env, StringWriter Output) Environment(string executable, bool display)
    {
        var output = new StringWriter();
        var home = Path.Combine(_tree.Paths.Root, "home");
        Directory.CreateDirectory(home);
        var variables = new Dictionary<string, string>();
        if (display)
        {
            variables["WAYLAND_DISPLAY"] = "wayland-0";
        }

        var env = new CliEnvironment(
            output,
            new StringWriter(),
            _data.Paths,
            HttpHandler: RuntimeVerbs.NeverCalled(),
            InstallPath: executable,
            Variables: variables,
            Setup: new SetupPlaces(SetupSystem.Linux, home, null, UserPathValue.EnvironmentKey, "/bin/bash"));
        return (env, output);
    }
}
