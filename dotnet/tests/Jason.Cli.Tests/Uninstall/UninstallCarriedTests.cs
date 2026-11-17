using System.Runtime.Versioning;
using System.Text.Json;
using Jason.Cli.Installation;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Tests.Process;
using Jason.Cli.Uninstall;
using Jason.Runtime.Tests;
using Microsoft.Win32;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// <c>jason uninstall</c> run as the jason Jason Desktop carries: one uninstall, which on macOS and Windows takes off what
/// the application and <c>jason install</c> placed, an older command-line install, and the application itself last; and
/// which, where no system's places are known, still refuses before anything is read or removed. Run against a scratch home,
/// a scratch data directory, an application folder of the test's own and, on Windows, registry keys of the test's own.
/// </summary>
public sealed class UninstallCarriedTests : IDisposable
{
    private readonly TempPaths _dir = new();
    private readonly TempPaths _tree = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Home => Path.Combine(_tree.Paths.Root, "home");

    private string LocalAppData => Path.Combine(_tree.Paths.Root, "local");

    public UninstallCarriedTests() => Directory.CreateDirectory(Home);

    public void Dispose()
    {
        _dir.Dispose();
        _tree.Dispose();
    }

    // --- where no system's places are known ---------------------------------------------------------------------

    [Theory]
    [Critical(Family.I3, "the desktop's jason is never deleted out of the application where no system's places are known")]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Without_a_systems_places_it_refuses_before_anything_is_read_or_removed(bool dryRun, bool purgeData)
    {
        var executable = MacApplication();
        var (env, output, remover) = Environment(null, executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(Human: false, DryRun: dryRun, PurgeData: purgeData, Yes: true, Force: false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal("uninstall_carried_by_desktop", refusal.Code);
        Assert.Contains(executable, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.True(File.Exists(executable), "the application's executable was removed");
        Assert.True(Directory.Exists(_dir.Paths.Root), "the data directory was removed");
    }

    // --- macOS -----------------------------------------------------------------------------------------------------

    [Fact]
    [Critical(Family.I3, "one uninstall takes what jason install and the application placed, and the application last")]
    public async Task On_macOS_it_takes_off_what_the_application_and_jason_install_placed_and_deletes_the_application_last()
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "jason"), CommandFiles.Wrapper(executable, null));
        File.WriteAllText(Path.Combine(bin, "jason-desktop"), CommandFiles.Wrapper(executable, null));
        var zprofile = Path.Combine(Home, ".zprofile");
        var zshrc = Path.Combine(Home, ".zshrc");
        File.WriteAllText(zprofile, "export EDITOR=vi\n");
        File.WriteAllText(zshrc, "alias ll='ls -l'\n");
        PathEntry.AddToProfile(zprofile, bin);
        PathEntry.AddToProfile(zshrc, bin);
        var agent = DesktopRemoval.LaunchAgentFile(MacPlaces());
        Directory.CreateDirectory(Path.GetDirectoryName(agent)!);
        File.WriteAllText(agent, $"<plist><dict><key>ProgramArguments</key>\n<array>\n<string>{Path.Combine(application, "Contents", "MacOS", "Jason")}</string>\n<string>--opened-at-login</string></array></dict></plist>");
        var updater = Plant(Path.Combine(Home, "Library", "Caches", "jason-desktop-updater"));
        var shipIt = Plant(Path.Combine(Home, "Library", "Caches", "ai.jasonharness.desktop.ShipIt"));
        var demoData = Demo(Path.TrimEndingDirectorySeparator(_dir.Paths.Root) + "-demo", marked: true);
        var demoRoot = Demo(Path.Combine(Home, "Jason-demo"), marked: true);
        var notTheDemos = Demo(Path.Combine(Home, "Jason-demo.removing-1"), marked: false);
        var harnessRoot = Plant(Path.Combine(Home, "Jason"));
        var (env, output, remover) = Environment(MacPlaces(), executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.Equal("deleted", report.GetProperty("plan").GetProperty("desktop").GetProperty("application_fate").GetString());
        Assert.False(File.Exists(Path.Combine(bin, "jason")), "the wrapper is still there");
        Assert.False(File.Exists(Path.Combine(bin, "jason-desktop")), "0.48's wrapper is still there");
        Assert.False(Directory.Exists(bin), "the folder that held nothing else is still there");
        Assert.Equal("export EDITOR=vi\n", File.ReadAllText(zprofile));
        Assert.Equal("alias ll='ls -l'\n", File.ReadAllText(zshrc));
        Assert.False(File.Exists(agent), "Open Jason at Login is still on");
        var unloaded = remover.Calls.IndexOf($"launchagent.unload {agent}");
        Assert.True(unloaded >= 0 && unloaded < remover.Calls.IndexOf($"file.remove {agent}"), string.Join(" | ", remover.Calls));
        Assert.False(Directory.Exists(updater), "the updater's cache is still there");
        Assert.False(Directory.Exists(shipIt), "ShipIt's state is still there");
        Assert.False(Directory.Exists(demoData), "the demo's data directory is still there");
        Assert.False(Directory.Exists(demoRoot), "the demo's harness root is still there");
        Assert.True(Directory.Exists(notTheDemos), "a folder without the demo's mark was removed");
        Assert.False(Directory.Exists(application), "the application is still there");
        Assert.True(Directory.Exists(_dir.Paths.Root), "the data directory went without the word");
        Assert.True(Directory.Exists(harnessRoot), "the harness root went");

        // The application is the last thing removed: everything above ran from the jason inside it.
        Assert.Equal($"tree.remove {application}", remover.Calls[^1]);
        Assert.DoesNotContain(remover.Calls, call => call.StartsWith("executable.remove", StringComparison.Ordinal));
    }

    [Fact]
    [Critical(Family.I3, "a purge runs before the application goes, never after it from inside it")]
    public async Task The_data_directory_goes_on_the_word_before_the_application_and_the_harness_root_never()
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        Directory.CreateDirectory(Path.Combine(_dir.Paths.Root, "state"));
        File.WriteAllText(Path.Combine(_dir.Paths.Root, "state", "jason.db"), "db");
        var harnessRoot = Plant(Path.Combine(Home, "Jason"));
        var (env, output, remover) = Environment(MacPlaces(), executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, PurgeData: true, Yes: true, Force: false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.False(Directory.Exists(_dir.Paths.Root), "the data directory is still there");
        Assert.True(Directory.Exists(harnessRoot), "the harness root went with the purge");
        var data = remover.Calls.IndexOf($"tree.remove {Path.Combine(_dir.Paths.Root, "state")}");
        var app = remover.Calls.IndexOf($"tree.remove {application}");
        Assert.True(data >= 0 && app > data, string.Join(" | ", remover.Calls));
    }

    [Theory]
    [Critical(Family.I3, "an open application is never removed from under itself")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_open_application_refuses_the_uninstall_before_anything_is_removed(bool human)
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.Running[Path.Combine(application, "Contents", "MacOS", "Jason")] = [5150];
        var error = new StringWriter();

        var exit = await UninstallCommand.RunAsync(env with { Error = error }, new UninstallOptions(human, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var said = human ? error.ToString() : RuntimeVerbs.Envelope(output.ToString()).Message;
        if (!human)
        {
            Assert.Equal("desktop_running", RuntimeVerbs.Envelope(output.ToString()).Code);
        }

        Assert.Contains("5150", said, StringComparison.Ordinal);
        Assert.Contains("Quit it", said, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.True(Directory.Exists(application), "the application was removed");
    }

    [Fact]
    public async Task A_dry_run_with_the_application_open_names_the_plan_and_removes_nothing()
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.Running[Path.Combine(application, "Contents", "MacOS", "Jason")] = [5150];

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, DryRun: true, false, false, false), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(application, JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop").GetProperty("application").GetString());
        Assert.Empty(remover.Calls);
    }

    [Fact]
    public async Task Wait_for_waits_until_the_application_that_asked_has_quit()
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var (env, output, remover) = Environment(MacPlaces(), executable);

        // The application quits while the uninstall waits for it: running at the first two looks, gone at the third.
        var quitting = new Quitting(7007, looksWhileRunning: 2);
        var exit = await UninstallCommand.RunAsync(env with { Processes = quitting }, new UninstallOptions(false, false, false, false, false, WaitFor: 7007), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.Equal(3, quitting.Looks);
        Assert.False(Directory.Exists(application));
        Assert.Contains($"tree.remove {application}", remover.Calls);
    }

    [Fact]
    public async Task Keep_application_leaves_the_folder_to_the_uninstaller_that_asked()
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.Running[Path.Combine(application, "Contents", "MacOS", "Jason")] = [5150];

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false, KeepApplication: true), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal("kept", report.GetProperty("plan").GetProperty("desktop").GetProperty("application_fate").GetString());
        Assert.True(Directory.Exists(application));
        Assert.DoesNotContain(remover.Calls, call => call.Contains(application + Path.DirectorySeparatorChar + "Contents", StringComparison.Ordinal) || call == $"tree.remove {application}");
    }

    [Fact]
    public async Task Keep_application_with_purge_data_is_refused_before_anything_is_read()
    {
        var executable = MacApplication();
        Directory.CreateDirectory(Path.Combine(_dir.Paths.Root, "state"));
        var (env, output, remover) = Environment(MacPlaces(), executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, PurgeData: true, Yes: true, Force: false, KeepApplication: true), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal("uninstall_refused", refusal.Code);
        Assert.Contains("--keep-application and --purge-data", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.Empty(remover.Reads);
        Assert.True(Directory.Exists(Path.Combine(_dir.Paths.Root, "state")));
    }

    [Fact]
    public async Task The_plan_is_read_after_the_application_that_asked_has_quit()
    {
        var executable = MacApplication();
        var (env, output, remover) = Environment(MacPlaces(), executable);
        var updater = Path.Combine(Home, "Library", "Caches", "jason-desktop-updater");

        // The application writes its updater's cache as it quits, while the uninstall waits for it.
        var quitting = new Quitting(7007, looksWhileRunning: 2, () => Plant(updater));
        var exit = await UninstallCommand.RunAsync(env with { Processes = quitting }, new UninstallOptions(false, false, false, false, false, WaitFor: 7007), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        var caches = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop").GetProperty("caches");
        Assert.Contains(caches.EnumerateArray(), cache => cache.GetString() == updater);
        Assert.False(Directory.Exists(updater), "what the application wrote as it quit is still there");
    }

    [Fact]
    public async Task A_bundle_that_does_not_name_Jasons_identifier_is_not_deleted()
    {
        var executable = MacApplication(identifier: "com.example.lookalike");
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var (env, output, remover) = Environment(MacPlaces(), executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        var desktop = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop");
        Assert.Equal("not_found", desktop.GetProperty("application_fate").GetString());
        Assert.Contains(desktop.GetProperty("left").EnumerateArray(), line => line.GetString()!.Contains("ai.jasonharness.desktop", StringComparison.Ordinal));
        Assert.True(Directory.Exists(application), "a bundle not known to be Jason's was deleted");
        Assert.DoesNotContain($"tree.remove {application}", remover.Calls);
    }

    [Fact]
    public async Task A_delete_of_the_application_the_system_refuses_is_reported_with_what_is_left_and_the_exit_says_so()
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.Denied.Add(application);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var problems = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("problems").EnumerateArray().Select(line => line.GetString()!).ToList();
        var problem = Assert.Single(problems);
        Assert.Contains($"{application} is still there", problem, StringComparison.Ordinal);
        Assert.Contains("Move Jason from Applications to the Bin", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrapper_of_another_applications_jason_and_an_unmarked_profile_line_are_left()
    {
        var executable = MacApplication();
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        var other = Path.Combine(_tree.Paths.Root, "elsewhere", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(other)!);
        File.WriteAllText(other, "another application's jason");
        File.WriteAllText(Path.Combine(bin, "jason"), CommandFiles.Wrapper(other, null));
        File.WriteAllText(Path.Combine(bin, "uv"), "another tool");
        var zprofile = Path.Combine(Home, ".zprofile");
        PathEntry.AddToProfile(zprofile, bin);
        var before = File.ReadAllText(zprofile);
        var (env, output, _) = Environment(MacPlaces(), executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.True(File.Exists(Path.Combine(bin, "jason")), "another application's wrapper was removed");
        Assert.Equal(before, File.ReadAllText(zprofile));
        var left = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop").GetProperty("left");
        Assert.Contains(left.EnumerateArray(), line => line.GetString()!.Contains("another application's jason", StringComparison.Ordinal));
    }

    // --- Windows ---------------------------------------------------------------------------------------------------

    [Fact]
    [SupportedOSPlatform("windows")]
    [Critical(Family.I3, "one uninstall on Windows hands the folder to the uninstaller after it ends")]
    public async Task On_Windows_it_takes_off_what_was_placed_and_hands_the_folder_to_the_uninstaller()
    {
        using var keys = WindowsKeys.Create();
        var executable = WindowsApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", ".."));
        var bundle = Path.GetDirectoryName(executable)!;
        var old = Path.Combine(LocalAppData, "Programs", "jason", "jason.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllBytes(old, new byte[2000]);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(old)!, "jason-LICENSE"), "MIT");
        keys.Set(keys.Environment, "Path", $@"C:\Tools;{Path.GetDirectoryName(old)};{bundle}", RegistryValueKind.ExpandString);
        keys.Set(keys.Run, "electron.app.Jason", $"\"{Path.Combine(application, "Jason.exe")}\" --opened-at-login", RegistryValueKind.String);
        keys.Set(keys.Run, "SomethingElse", @"""C:\Other\other.exe"" --tray", RegistryValueKind.String);
        keys.Set(keys.StartupApproved, "electron.app.Jason", new byte[12], RegistryValueKind.Binary);
        keys.Set($@"{keys.Classes}\jason-desktop\shell\open\command", string.Empty, $"\"{Path.Combine(application, "Jason.exe")}\" -- \"%1\"", RegistryValueKind.String);
        var apps = Path.Combine(LocalAppData, "Microsoft", "WindowsApps");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "jason.cmd"), $"@echo off\r\n\"{executable}\" %*\r\n");
        File.WriteAllText(Path.Combine(apps, "winget.exe"), "another program");
        var updater = Plant(Path.Combine(LocalAppData, "jason-desktop-updater"));
        var places = new SetupPlaces(SetupSystem.Windows, Home, LocalAppData, keys.Environment, null)
        {
            Temporary = _tree.Paths.Root,
            RunKey = keys.Run,
            StartupApprovedKey = keys.StartupApproved,
            ClassesKey = keys.Classes,
        };
        var (env, output, remover) = Environment(places, executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal("after_exit", report.GetProperty("plan").GetProperty("desktop").GetProperty("application_fate").GetString());
        Assert.Equal(@"C:\Tools", keys.Get(keys.Environment, "Path"));
        Assert.Equal(RegistryValueKind.ExpandString, keys.Kind(keys.Environment, "Path"));
        Assert.Null(keys.Get(keys.Run, "electron.app.Jason"));
        Assert.NotNull(keys.Get(keys.Run, "SomethingElse"));
        Assert.Null(keys.Get(keys.StartupApproved, "electron.app.Jason"));
        Assert.False(keys.Exists($@"{keys.Classes}\jason-desktop"), "the jason-desktop link is still registered");
        Assert.False(File.Exists(Path.Combine(apps, "jason.cmd")), "the WindowsApps command file is still there");
        Assert.True(File.Exists(Path.Combine(apps, "winget.exe")));
        Assert.False(Directory.Exists(Path.GetDirectoryName(old)), "the command-line install is still there");
        Assert.False(Directory.Exists(updater), "the updater's cache is still there");
        Assert.True(Directory.Exists(application), "the folder is the uninstaller's to remove, after this ends");
        Assert.Equal($"application.uninstaller {application}", remover.Calls[^1]);
        Assert.Contains(report.GetProperty("done").EnumerateArray(), line => line.GetString()!.Contains("once this command has ended", StringComparison.Ordinal)
            && line.GetString()!.Contains("If Jason is still listed in Installed apps in Settings afterwards", StringComparison.Ordinal));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_another_jason_running_from_the_application_refuses_before_anything_is_removed()
    {
        using var keys = WindowsKeys.Create();
        var executable = WindowsApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", ".."));
        var apps = Path.Combine(LocalAppData, "Microsoft", "WindowsApps");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "jason.cmd"), $"@echo off\r\n\"{executable}\" %*\r\n");
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var places = new SetupPlaces(SetupSystem.Windows, Home, LocalAppData, keys.Environment, null) { RunKey = keys.Run, StartupApprovedKey = keys.StartupApproved, ClassesKey = keys.Classes };
        var (env, output, remover) = Environment(places, executable);
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);

        // This process, the runtime this uninstall stops, and a runtime of another data directory, all from the carried jason.
        remover.Running[executable] = [System.Environment.ProcessId, RuntimeVerbs.Pid, 9001];

        var exit = await UninstallCommand.RunAsync(env with { Processes = processes }, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal("jason_running", refusal.Code);
        Assert.Contains("(process 9001)", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("runtime stop", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.True(File.Exists(Path.Combine(apps, "jason.cmd")), "the command was removed");
        Assert.True(Directory.Exists(application));

        // A dry run removes nothing and is never refused for it; nor is the uninstaller's own call, which leaves the folder.
        output.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await UninstallCommand.RunAsync(env with { Processes = processes }, new UninstallOptions(false, DryRun: true, false, false, false), Ct));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_an_older_install_held_by_a_running_process_is_said_once()
    {
        using var keys = WindowsKeys.Create();
        var executable = WindowsApplication();
        var old = Path.Combine(LocalAppData, "Programs", "jason", "jason.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllBytes(old, new byte[2000]);
        var places = new SetupPlaces(SetupSystem.Windows, Home, LocalAppData, keys.Environment, null)
        {
            Temporary = _tree.Paths.Root,
            RunKey = keys.Run,
            StartupApprovedKey = keys.StartupApproved,
            ClassesKey = keys.Classes,
            Delete = path => throw new IOException("held by a running process, the stand-in says"),
        };
        var (env, output, _) = Environment(places, executable);

        await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false, KeepApplication: true), Ct);

        var kept = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("kept").EnumerateArray().Select(line => line.GetString()!).ToList();
        Assert.Single(kept, line => line.Contains("renamed to", StringComparison.Ordinal));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_a_Path_entry_whose_folder_holds_more_than_the_application_carries_is_left()
    {
        using var keys = WindowsKeys.Create();
        var executable = WindowsApplication();
        var bundle = Path.GetDirectoryName(executable)!;
        File.WriteAllText(Path.Combine(bundle, "someone-elses.exe"), "x");
        keys.Set(keys.Environment, "Path", bundle, RegistryValueKind.ExpandString);
        var places = new SetupPlaces(SetupSystem.Windows, Home, LocalAppData, keys.Environment, null) { RunKey = keys.Run, StartupApprovedKey = keys.StartupApproved, ClassesKey = keys.Classes };
        var (env, output, _) = Environment(places, executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false, KeepApplication: true), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.Equal(bundle, keys.Get(keys.Environment, "Path"));
        Assert.Contains("holds more than the application carries there", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_waiter_runs_a_copy_of_the_uninstaller_in_place_once_the_uninstall_has_ended()
    {
        var script = UninstallerHandOff.Script(4321);

        Assert.StartsWith("$ErrorActionPreference = 'SilentlyContinue'; Wait-Process -Id 4321 -Timeout 600; ", script, StringComparison.Ordinal);
        Assert.Contains("$folder = $env:JASON_UNINSTALL_APPLICATION; $aside = $env:JASON_UNINSTALL_ASIDE; ", script, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -LiteralPath (Join-Path $folder 'Uninstall Jason.exe') -Destination $copy", script, StringComparison.Ordinal);

        // _?= last and unquoted, a space in the folder included, and the uninstaller told the application's steps are done.
        Assert.Contains("Start-Process -Wait -FilePath $copy -ArgumentList ('/S --uninstalled-by-jason _?=' + $folder) }", script, StringComparison.Ordinal);
        Assert.EndsWith("Remove-Item -LiteralPath $aside -Recurse -Force", script, StringComparison.Ordinal);
    }

    /// <summary>A profile folder named with every quote PowerShell reads as one: the apostrophe and the four typographic ones.</summary>
    private const string Quoted = "O\u2019Brien \u2018a\u2019 \u201Cb\u201D \u201Bc\u201A d'e";

    [Fact]
    public void The_waiters_and_the_cleanups_paths_go_in_the_environment_and_never_in_the_script()
    {
        var application = Path.Combine(@"C:\Users", Quoted, @"AppData\Local\Programs\jason-desktop") + @"\";
        var temporary = Path.Combine(@"C:\Users", Quoted, @"AppData\Local\Temp");

        var variables = UninstallerHandOff.Variables(application, temporary);

        Assert.Equal(Path.TrimEndingDirectorySeparator(application), variables[UninstallerHandOff.ApplicationVariable]);
        Assert.StartsWith(Path.Combine(temporary, "jason-uninstall-"), variables[UninstallerHandOff.AsideVariable], StringComparison.Ordinal);
        foreach (var script in new[] { UninstallerHandOff.Script(4321), AsideCleanup.Script(4321) })
        {
            Assert.DoesNotContain("Users", script, StringComparison.Ordinal);
            Assert.DoesNotContain("\u2019", script, StringComparison.Ordinal);
            Assert.Equal(0, ParseErrors(script));
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void The_cleanup_removes_a_folder_named_with_typographic_quotes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The cleanup is Windows PowerShell's.");
        var folder = Path.Combine(_tree.Paths.Root, Quoted, "jason-uninstall-1");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "jason.exe"), "the moved image");

        // It waits for a process id no process has, which it passes at once.
        var (cleaned, said) = PowerShell(AsideCleanup.Script(int.MaxValue - 1), new Dictionary<string, string> { [AsideCleanup.FolderVariable] = folder });

        Assert.True(cleaned == 0, said);
        Assert.False(Directory.Exists(folder), "the folder named with typographic quotes is still there");
        Assert.True(Directory.Exists(Path.Combine(_tree.Paths.Root, Quoted)), "the cleanup removed more than its folder");
    }

    /// <summary>How many errors Windows PowerShell's own parser finds in a script; zero elsewhere, where there is none.</summary>
    private static int ParseErrors(string script)
    {
        if (!OperatingSystem.IsWindows())
        {
            return 0;
        }

        var (exit, said) = PowerShell(
            "$errors = $null; [void][System.Management.Automation.Language.Parser]::ParseInput($env:JASON_TEST_SCRIPT, [ref]$null, [ref]$errors); "
            + "[Console]::Out.Write($errors.Count)",
            new Dictionary<string, string> { ["JASON_TEST_SCRIPT"] = script });
        Assert.True(exit == 0, said);
        return int.Parse(said.Trim(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Windows PowerShell, with no window and no profile, a script handed on its command line and its variables.</summary>
    private static (int Exit, string Output) PowerShell(string script, IReadOnlyDictionary<string, string> variables)
    {
        var start = Jason.Contracts.Discovery.ChildProcess.StartInfo(
            Path.Combine(System.Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Jason.Contracts.Discovery.ChildKind.SystemTool);
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var (name, value) in variables)
        {
            start.Environment[name] = value;
        }

        using var process = Jason.Contracts.Discovery.ChildProcess.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(Ct);
        var error = process.StandardError.ReadToEndAsync(Ct);
        Assert.True(process.WaitForExit(TimeSpan.FromSeconds(60)), "Windows PowerShell did not end");
        return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }

    // --- held to the application's own source ------------------------------------------------------------------

    [Fact]
    public void The_applications_placements_are_read_by_the_names_its_source_writes()
    {
        var root = Repository();
        string Source(params string[] parts) => File.ReadAllText(Path.Combine([root, .. parts])).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains($"export const demoMarkerName = '{DesktopRemoval.DemoMarkerName}';", Source("desktop", "src", "demo-folders.ts"), StringComparison.Ordinal);
        Assert.Contains($"export const demoMarkerText = '{DesktopRemoval.DemoMarkerText.TrimEnd('\n')}\\n';", Source("desktop", "src", "demo-folders.ts"), StringComparison.Ordinal);
        Assert.Contains($"const label = '{DesktopRemoval.Identifier}';", Source("desktop", "src", "login-item.ts"), StringComparison.Ordinal);
        Assert.Contains($"export const linkScheme = '{DesktopRemoval.LinkScheme}';", Source("desktop", "src", "deep-link.ts"), StringComparison.Ordinal);
        Assert.Contains($"'{DesktopRemoval.UpdaterCacheName}'", Source("desktop", "src", "uninstall.ts"), StringComparison.Ordinal);
        Assert.Contains($"appId: '{DesktopRemoval.Identifier}'", Source("desktop", "electron-builder.config.mjs"), StringComparison.Ordinal);
        Assert.Contains($"\"{DesktopRemoval.UninstalledByJasonFlag}\"", Source("desktop", "installer", "uninstall.nsh"), StringComparison.Ordinal);
        Assert.Contains("export const carriedUninstallArgs = ['uninstall', '--keep-application'] as const;", Source("desktop", "src", "uninstall.ts"), StringComparison.Ordinal);
    }

    // --- the fixture ---------------------------------------------------------------------------------------------

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>
    /// A Jason.app of the test's own: its executable, its Info.plist as electron-builder writes one, and the carried jason
    /// with its record.
    /// </summary>
    private string MacApplication(string identifier = DesktopRemoval.Identifier)
    {
        var application = Path.Combine(_tree.Paths.Root, "Applications", "Jason.app");
        Directory.CreateDirectory(Path.Combine(application, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(application, "Contents", "MacOS", "Jason"), "electron");
        File.WriteAllText(
            Path.Combine(application, "Contents", "Info.plist"),
            $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<plist version=\"1.0\">\n  <dict>\n    <key>CFBundleExecutable</key>\n    <string>Jason</string>\n"
            + $"    <key>CFBundleIdentifier</key>\n    <string>{identifier}</string>\n  </dict>\n</plist>\n");
        return Bundle(Path.Combine(application, "Contents", "Resources", "bundle"), "jason");
    }

    /// <summary>An installed folder of the test's own: Jason.exe, its uninstaller, and the carried jason.exe with its record.</summary>
    private string WindowsApplication()
    {
        var application = Path.Combine(LocalAppData, "Programs", "jason-desktop");
        Directory.CreateDirectory(application);
        File.WriteAllText(Path.Combine(application, "Jason.exe"), "electron");
        File.WriteAllText(Path.Combine(application, DesktopRemoval.UninstallerName), "nsis");
        return Bundle(Path.Combine(application, "resources", "bundle"), "jason.exe");
    }

    private static string Bundle(string bundle, string name)
    {
        Directory.CreateDirectory(Path.Combine(bundle, "skills"));
        var executable = Path.GetFullPath(Path.Combine(bundle, name));
        File.WriteAllText(executable, "jason 0.34.0");
        File.WriteAllText(Path.Combine(bundle, "LICENSE"), "MIT");
        File.WriteAllText(Path.Combine(bundle, "THIRD-PARTY-NOTICES"), "notices");
        CarriedRecord.Beside(executable);
        return executable;
    }

    private static string Plant(string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "something"), "x");
        return folder;
    }

    private static string Demo(string folder, bool marked)
    {
        Plant(folder);
        if (marked)
        {
            File.WriteAllText(Path.Combine(folder, DesktopRemoval.DemoMarkerName), DesktopRemoval.DemoMarkerText);
        }

        return folder;
    }

    private SetupPlaces MacPlaces() => new(SetupSystem.MacOs, Home, null, UserPathValue.EnvironmentKey, "/bin/zsh") { Temporary = _tree.Paths.Root };

    private (CliEnvironment Env, StringWriter Output, RecordingRemover Remover) Environment(SetupPlaces? places, string executable)
    {
        var output = new StringWriter();
        var remover = new RecordingRemover { ReallyRemoves = true };
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            _dir.Paths,
            HttpHandler: RuntimeVerbs.NeverCalled(),
            Processes: new FakeProcessControl(),
            InstallPath: executable,
            Autostart: new RecordingRegistrar(places?.Platform is SetupSystem.Windows ? Jason.Cli.Autostart.AutostartPlatform.Windows : Jason.Cli.Autostart.AutostartPlatform.MacOs),
            Harnesses: HarnessLocators.ForHome(Home),
            Removes: remover,
            Variables: new Dictionary<string, string>(),
            Setup: places);
        return (env, output, remover);
    }

    /// <summary>A process that is running for a number of looks, then gone; no runtime anywhere.</summary>
    private sealed class Quitting(int pid, int looksWhileRunning, Action? asItQuits = null) : Jason.Cli.Process.IRuntimeProcessControl
    {
        public int Looks { get; private set; }

        public Jason.Cli.Process.IProcessHandle Launch(Jason.Contracts.Discovery.JasonPaths paths, IReadOnlyList<string> executable, string? updateLaunch) =>
            throw new InvalidOperationException("nothing is started");

        public Task<Jason.Cli.Process.FinishedCommand> RunAsync(Jason.Contracts.Discovery.JasonPaths paths, IReadOnlyList<string> command, TextWriter error, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("nothing is run");

        public bool IsRunning(int id)
        {
            if (id != pid)
            {
                return false;
            }

            if (++Looks == looksWhileRunning)
            {
                asItQuits?.Invoke();
            }

            return Looks <= looksWhileRunning;
        }

        public DateTimeOffset? StartTime(int id) => null;
    }

    /// <summary>Keys of one test's own under <c>HKCU\Software\jason-tests</c>, standing for the account's.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class WindowsKeys : IDisposable
    {
        private const string Parent = @"Software\jason-tests";

        private WindowsKeys(string root) => Root = root;

        public string Root { get; }

        public string Environment => $@"{Root}\Environment";

        public string Run => $@"{Root}\Run";

        public string StartupApproved => $@"{Root}\StartupApproved";

        public string Classes => $@"{Root}\Classes";

        public static WindowsKeys Create()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "The Path, the login item and the link are registry values only on Windows.");
            return new($@"{Parent}\{Guid.NewGuid():N}");
        }

        public void Set(string key, string name, object value, RegistryValueKind kind)
        {
            using var opened = Registry.CurrentUser.CreateSubKey(key);
            opened.SetValue(name, value, kind);
        }

        public object? Get(string key, string name)
        {
            using var opened = Registry.CurrentUser.OpenSubKey(key);
            return opened?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        public RegistryValueKind Kind(string key, string name)
        {
            using var opened = Registry.CurrentUser.OpenSubKey(key)!;
            return opened.GetValueKind(name);
        }

        public bool Exists(string key)
        {
            using var opened = Registry.CurrentUser.OpenSubKey(key);
            return opened is not null;
        }

        public void Dispose()
        {
            Registry.CurrentUser.DeleteSubKeyTree(Root, throwOnMissingSubKey: false);
            try
            {
                Registry.CurrentUser.DeleteSubKey(Parent, throwOnMissingSubKey: false);
            }
            catch (InvalidOperationException)
            {
                // Another test's key is under it still.
            }
        }
    }
}
