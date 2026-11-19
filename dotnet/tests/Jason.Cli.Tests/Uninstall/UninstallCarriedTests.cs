using System.Runtime.Versioning;
using System.Text.Json;
using Jason.Cli.Installation;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Installation;
using Jason.Cli.Tests.Process;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;
using Jason.Runtime.Tests;
using Microsoft.Win32;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// <c>jason uninstall</c> run as the jason Jason Desktop carries: one uninstall, which on macOS, Windows and Linux takes off
/// what the application and <c>jason install</c> placed, an older command-line install, and the application itself, last, or
/// on Linux from the deb first, as its package; and which, where no system's places are known, still refuses before anything
/// is read or removed. Run against a scratch home, a scratch data directory, an application folder of the test's own, on
/// Linux XDG folders of its own away from that home and a stand-in for apt, and on Windows registry keys of its own.
/// </summary>
public sealed partial class UninstallCarriedTests : IDisposable
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
        Assert.Contains(UninstallCommand.RemovingTheApplication, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Trash", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Installed apps", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.True(File.Exists(executable), "the application's executable was removed");
        Assert.True(Directory.Exists(_dir.Paths.Root), "the data directory was removed");
    }

    /// <summary>
    /// The refusal above is reached only where no system's places are known: on a real machine the places are known on
    /// Windows, on macOS and on Linux, and unknown elsewhere.
    /// </summary>
    [Fact]
    public void A_systems_places_are_known_on_Windows_macOS_and_Linux_and_nowhere_else()
    {
        var places = SetupPlaces.ForThisMachine();

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            Assert.NotNull(places);
            Assert.Equal(OperatingSystem.IsWindows() ? SetupSystem.Windows : OperatingSystem.IsMacOS() ? SetupSystem.MacOs : SetupSystem.Linux, places.Platform);
        }
        else
        {
            Assert.Null(places);
        }
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

    /// <summary>
    /// A profile file goes with Jason's lines only where <c>jason install</c> recorded that it created it and nothing but
    /// whitespace is left once the lines are out; a file it found, one the person has written into since, and any where
    /// there is no record stay, without Jason's lines.
    /// </summary>
    [Theory]
    [InlineData("created and still empty", true, true, "", false)]
    [InlineData("created and written into by the person", true, true, "export EDITOR=vi\n", true)]
    [InlineData("there before", true, false, "", true)]
    [InlineData("no record", false, false, "", true)]
    public async Task A_profile_goes_only_where_jason_install_created_it_and_nothing_else_is_left(string row, bool record, bool recorded, string persons, bool stays)
    {
        var executable = MacApplication();
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "jason"), CommandFiles.Wrapper(executable, null));
        var zprofile = Path.Combine(Home, ".zprofile");
        PathEntry.AddToProfile(zprofile, bin);
        File.AppendAllText(zprofile, persons);
        if (record)
        {
            CreatedProfiles.Add(Home, recorded ? zprofile : Path.Combine(Home, ".zshrc"));
        }

        var (env, output, _) = Environment(MacPlaces(), executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, $"{row}: {output}");
        Assert.True(File.Exists(zprofile) == stays, $"{row}: the profile {(stays ? "went" : "stayed")}");
        if (stays)
        {
            Assert.DoesNotContain(PathEntry.Marker, File.ReadAllText(zprofile), StringComparison.Ordinal);
            Assert.Equal(persons, File.ReadAllText(zprofile).Trim('\n') + (persons.Length > 0 ? "\n" : string.Empty));
        }
    }

    /// <summary>
    /// What the application of this bundle identifier leaves under <c>~/Library</c> goes with it, just before it, the
    /// preferences through the preferences system first. The two folders named after the application and not its identifier
    /// hold nothing only Jason Desktop writes, so they are named and left, <c>--purge-data</c> or not.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task On_macOS_the_applications_own_state_goes_with_it_and_what_is_only_named_after_it_is_left(bool purge)
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var places = PlantLibrary();
        var named = DesktopRemoval.ElectronNamedPlaces(Home);
        foreach (var folder in named)
        {
            File.WriteAllText(Path.Combine(Plant(folder), "Local State"), "{}");
        }

        var (env, output, remover) = Environment(MacPlaces(), executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, purge, Yes: true, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.Equal(
            [
                Path.Combine(Home, "Library", "Preferences", "ai.jasonharness.desktop.plist"),
                Path.Combine(Home, "Library", "Saved Application State", "ai.jasonharness.desktop.savedState"),
                Path.Combine(Home, "Library", "Caches", "ai.jasonharness.desktop"),
                Path.Combine(Home, "Library", "HTTPStorages", "ai.jasonharness.desktop"),
                Path.Combine(Home, "Library", "HTTPStorages", "ai.jasonharness.desktop.binarycookies"),
                Path.Combine(Home, "Library", "WebKit", "ai.jasonharness.desktop"),
                Path.Combine(Home, "Library", "Application Support", "ai.jasonharness.desktop"),
            ],
            places);
        Assert.Equal([Path.Combine(Home, "Library", "Application Support", "Jason"), Path.Combine(Home, "Library", "Logs", "Jason")], named);
        foreach (var place in places)
        {
            Assert.False(File.Exists(place) || Directory.Exists(place), $"{place} is still there");
        }

        var desktop = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop");
        Assert.Equal(places, desktop.GetProperty("application_state").EnumerateArray().Select(place => place.GetString()!));
        foreach (var folder in named)
        {
            Assert.True(Directory.Exists(folder), $"{folder}, named after the application only, was removed (purge {purge})");
            Assert.Contains(desktop.GetProperty("left").EnumerateArray(), line => line.GetString()!.StartsWith(folder, StringComparison.Ordinal)
                && line.GetString()!.Contains("may be another application's", StringComparison.Ordinal));
        }

        var preferences = remover.Calls.IndexOf("preferences.delete ai.jasonharness.desktop");
        Assert.True(preferences >= 0 && preferences < remover.Calls.IndexOf($"file.remove {places[0]}"), string.Join(" | ", remover.Calls));
        Assert.Equal($"tree.remove {application}", remover.Calls[^1]);
    }

    /// <summary>
    /// The plan names each place under <c>~/Library</c> before anything goes: a dry run, and the plan a person is shown under
    /// <c>--human</c>, list the application's own state and what is left.
    /// </summary>
    [Fact]
    public async Task The_plan_names_the_applications_own_state_and_what_is_left_before_anything_goes()
    {
        var executable = MacApplication();
        var places = PlantLibrary();
        var named = Plant(DesktopRemoval.ElectronNamedPlaces(Home)[0]);
        var (env, output, remover) = Environment(MacPlaces(), executable);

        Assert.Equal(ExitCodes.Success, await UninstallCommand.RunAsync(env, new UninstallOptions(Human: true, DryRun: true, false, false, false), Ct));

        var plan = output.ToString();
        foreach (var place in places)
        {
            Assert.Contains($"the application's own state, {place}, just before the application", plan, StringComparison.Ordinal);
            Assert.True(File.Exists(place) || Directory.Exists(place), $"a dry run removed {place}");
        }

        Assert.Contains($"NOT touched: {named} is named after the application", plan, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);

        output.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await UninstallCommand.RunAsync(env, new UninstallOptions(false, DryRun: true, false, false, false), Ct));
        var desktop = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop");
        Assert.Equal(places, desktop.GetProperty("application_state").EnumerateArray().Select(place => place.GetString()!));
    }

    /// <summary>
    /// Where the home directory is <c>/</c>, whose Library is the system's, or is not the account's own, nothing under it is
    /// looked at or removed, and the plan says why.
    /// </summary>
    [Theory]
    [InlineData("root", true)]
    [InlineData("another account's", true)]
    [InlineData("the account's own", false)]
    [InlineData("an account home that cannot be read", false)]
    public void The_Library_steps_are_skipped_where_the_home_is_root_or_not_the_accounts_own(string row, bool skipped)
    {
        var (home, account) = row switch
        {
            "root" => ("/", "/"),
            "another account's" => (Home, Path.Combine(_tree.Paths.Root, "someone-else")),
            "the account's own" => (Home, Home),
            _ => (Home, (string?)null),
        };

        Assert.Equal(skipped, DesktopRemoval.LibraryRefusal(home, account) is not null);
    }

    [Fact]
    public async Task A_home_that_is_not_the_accounts_own_takes_nothing_under_its_Library()
    {
        var executable = MacApplication();
        var places = PlantLibrary();
        var other = Path.Combine(_tree.Paths.Root, "someone-else");
        var (env, output, remover) = Environment(MacPlaces() with { AccountHome = () => other }, executable);

        Assert.Equal(ExitCodes.Success, await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct));

        foreach (var place in places)
        {
            Assert.True(File.Exists(place) || Directory.Exists(place), $"{place} went under a home that is not the account's own");
        }

        var desktop = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop");
        Assert.Empty(desktop.GetProperty("application_state").EnumerateArray());
        Assert.Contains(desktop.GetProperty("left").EnumerateArray(), line => line.GetString()!.Contains("not this account's own", StringComparison.Ordinal));
        Assert.DoesNotContain(remover.Calls, call => call.StartsWith("preferences.delete", StringComparison.Ordinal));
    }

    /// <summary>
    /// An uninstall that keeps the data takes back the note that a start is owed: with the installation taken off none is,
    /// and a later install would otherwise start a runtime for an old stop. A dry run leaves it.
    /// </summary>
    [Fact]
    public async Task An_uninstall_that_keeps_the_data_takes_back_the_note_that_a_start_is_owed()
    {
        var executable = MacApplication();
        Directory.CreateDirectory(_dir.Paths.RunDirectory);
        File.WriteAllText(_dir.Paths.StartOwedFile, "{\"stopped_at\":\"2026-11-17T15:05:35Z\"}");
        var (env, output, _) = Environment(MacPlaces(), executable);

        Assert.Equal(ExitCodes.Success, await UninstallCommand.RunAsync(env, new UninstallOptions(false, DryRun: true, false, false, false), Ct));
        Assert.True(File.Exists(_dir.Paths.StartOwedFile), "a dry run took the note back");

        output.GetStringBuilder().Clear();
        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.False(File.Exists(_dir.Paths.StartOwedFile), "the note that a start is owed outlived the uninstall");
        Assert.True(Directory.Exists(_dir.Paths.Root), "the data directory went without the word");
        Assert.Contains(JsonDocument.Parse(output.ToString()).RootElement.GetProperty("done").EnumerateArray(), line => line.GetString()!.Contains("start-owed.json", StringComparison.Ordinal));
    }

    /// <summary>
    /// An uninstall run through the installer leaves no note that a start is owed: the installer's stop comes first, on the
    /// installer's behalf (<c>runtime stop --start-owed</c>, which writes the note), and the uninstall routine after it takes
    /// the note back where it keeps the data, or takes it with the data. Held through the routine's own seams: the stop
    /// through the verb, the uninstall through <see cref="UninstallCommand"/> with the remover that really removes.
    /// </summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task An_uninstall_after_the_installers_stop_leaves_no_note_that_a_start_is_owed(bool purge, bool keepApplication)
    {
        var executable = MacApplication();
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var stopping = new FakeHandler(_ =>
        {
            File.Delete(_dir.Paths.DescriptorFile);
            return RuntimeVerbs.Response(System.Net.HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"));
        });
        var (env, output, _) = Environment(MacPlaces(), executable);

        Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["runtime", "stop", "--start-owed"], env with { HttpHandler = stopping }, Ct));
        Assert.True(File.Exists(_dir.Paths.StartOwedFile), "the installer's stop wrote no note that a start is owed");

        output.GetStringBuilder().Clear();
        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, purge, Yes: true, false, KeepApplication: keepApplication), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.False(File.Exists(_dir.Paths.StartOwedFile), "the note that a start is owed outlived the uninstall");
        Assert.Equal(!purge, Directory.Exists(_dir.Paths.Root));
    }

    /// <summary>Plants every place the application's own state may be, as a file or a folder, and returns them in order.</summary>
    private IReadOnlyList<string> PlantLibrary()
    {
        var places = DesktopRemoval.ApplicationStatePlaces(Home);
        foreach (var place in places)
        {
            if (place.EndsWith(".plist", StringComparison.Ordinal) || place.EndsWith(".binarycookies", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(place)!);
                File.WriteAllText(place, "state");
            }
            else
            {
                Plant(place);
            }
        }

        return places;
    }

    /// <summary>
    /// Under <c>--keep-application</c> Open at Login's file goes but its job is not unloaded, since the window kept may be
    /// that job's own, and one line of the output says the job stays loaded until the next login.
    /// </summary>
    [Fact]
    public async Task Keep_application_says_the_open_at_login_job_stays_loaded_until_the_next_login()
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var agent = DesktopRemoval.LaunchAgentFile(MacPlaces());
        Directory.CreateDirectory(Path.GetDirectoryName(agent)!);
        File.WriteAllText(agent, $"<plist><dict><key>ProgramArguments</key>\n<array>\n<string>{Path.Combine(application, "Contents", "MacOS", "Jason")}</string>\n<string>--opened-at-login</string></array></dict></plist>");
        var (env, output, remover) = Environment(MacPlaces(), executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false, KeepApplication: true), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.False(File.Exists(agent), "Open Jason at Login's file is still there");
        Assert.DoesNotContain(remover.Calls, call => call.StartsWith("launchagent.unload", StringComparison.Ordinal));
        var done = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("done").EnumerateArray().Select(line => line.GetString()!).ToList();
        Assert.Single(done, line => line.Contains("stays loaded until the next login", StringComparison.Ordinal) && line.Contains("ai.jasonharness.desktop", StringComparison.Ordinal));
    }

    /// <summary><c>--keep-application</c> keeps all of the application's state: it stays where the application stays.</summary>
    [Fact]
    public async Task Keep_application_keeps_the_applications_own_state()
    {
        var executable = MacApplication();
        var preferences = DesktopRemoval.PreferencesFile(Home);
        Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);
        File.WriteAllText(preferences, "state");
        var saved = Plant(Path.Combine(Home, "Library", "Saved Application State", "ai.jasonharness.desktop.savedState"));
        var (env, output, remover) = Environment(MacPlaces(), executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false, KeepApplication: true), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.True(File.Exists(preferences));
        Assert.True(Directory.Exists(saved));
        Assert.DoesNotContain(remover.Calls, call => call.StartsWith("preferences.delete", StringComparison.Ordinal));
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
        // The bundle where it is, which need not be /Applications: the test's own folder here.
        Assert.Contains($"Move {application} to the Trash in the Finder", problem, StringComparison.Ordinal);
        Assert.DoesNotContain("from Applications", problem, StringComparison.Ordinal);
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

    /// <summary>
    /// On macOS a <c>jason</c> typed in Terminal is in the terminal's POSIX session, and Jason.app, started by the system,
    /// never is; .NET's <c>Process.SessionId</c> there is that POSIX session. So the application is stood in for by a process
    /// in a session of its own (<c>setsid</c>), running an image named and placed as the application's executable, and it
    /// has to be found.
    /// </summary>
    [Fact]
    public void On_macOS_the_application_running_in_another_session_is_found()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Process.SessionId is the POSIX session on macOS; CI's macOS leg runs this row.");

        var folder = Path.Combine(_tree.Paths.Root, "Running", "Jason.app", "Contents", "MacOS");
        Directory.CreateDirectory(folder);
        var image = Path.Combine(folder, "Jason");
        File.Copy("/bin/sleep", image);

        // Perl, which macOS carries, makes the child a session of its own and then becomes the image, keeping its id.
        var start = new System.Diagnostics.ProcessStartInfo("/usr/bin/perl")
        {
            ArgumentList = { "-MPOSIX", "-e", "POSIX::setsid() or die; exec { $ARGV[0] } @ARGV or die", image, "120" },
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            var remover = InstallationRemovers.ForThisMachine();
            IReadOnlyList<int> found = [];
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < until && !found.Contains(child.Id))
            {
                Thread.Sleep(100);
                found = remover.RunningFrom(image);
            }

            using (var seen = System.Diagnostics.Process.GetProcessById(child.Id))
            using (var self = System.Diagnostics.Process.GetCurrentProcess())
            {
                Assert.NotEqual(self.SessionId, seen.SessionId);
            }

            Assert.True(found.Contains(child.Id), $"The look did not find process {child.Id}: it was given '{image}' and read '{ImageReadFor(child.Id)}' for the process.");
        }
        finally
        {
            child.Kill();
            child.WaitForExit();
        }
    }

    /// <summary>
    /// On Linux, Jason Desktop as the system shows it: a name longer than the fifteen characters the system keeps of it, a
    /// title Chromium rewrites into one string with its switches ("/opt/Jason/jason-desktop-app --"), from which .NET reads
    /// the rest of a long name; and, once apt replaced the deb's files under the running window (dpkg renames the new file
    /// over the old one), an image reported as "<path> (deleted)". The application is open from that installation either
    /// way, and removing it from under itself is what the look is for, so it is found before the replacement and after.
    /// Catches a look by the name .NET reads, which never finds the window on Linux, and one that compares the replaced
    /// image's report with the path as it is; either lets an uninstall go on under an open window.
    /// </summary>
    [Fact]
    public void On_linux_the_application_is_found_under_its_rewritten_title_and_after_its_file_was_replaced()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The '(deleted)' report is Linux's; the rows beside this one hold the other systems' looks.");

        // Under the deb's name, longer than the fifteen characters the system keeps of a process's name. perl stands in for
        // the application, as in the row below.
        var folder = Path.Combine(_tree.Paths.Root, "opt", "Jason");
        Directory.CreateDirectory(folder);
        var image = Path.Combine(folder, "jason-desktop-app");
        File.Copy("/usr/bin/perl", image);
        var start = new System.Diagnostics.ProcessStartInfo(image)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // As the system shows Jason Desktop: the title one string, the program and its switch (perl's $0), and the name the
        // system keeps the program's own first fifteen characters, which perl's $0 would set from the title instead.
        foreach (var argument in new[] { "-e", $"$0 = '{image} --'; open(my $c, '>', '/proc/self/comm'); print $c 'jason-desktop-app'; close $c; sleep 120" })
        {
            start.ArgumentList.Add(argument);
        }

        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            var remover = InstallationRemovers.ForThisMachine();
            IReadOnlyList<int> Look()
            {
                IReadOnlyList<int> found = [];
                var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
                while (DateTime.UtcNow < until && !found.Contains(child.Id))
                {
                    Thread.Sleep(100);
                    found = remover.RunningFrom(image);
                }

                return found;
            }

            Assert.True(Look().Contains(child.Id), $"The look did not find process {child.Id} running '{image}'; it read '{ImageReadFor(child.Id)}'.");

            // As dpkg replaces a file: the new one written beside it, then renamed over it.
            File.Copy("/usr/bin/perl", image + ".dpkg-new");
            File.Move(image + ".dpkg-new", image, overwrite: true);

            Assert.False(child.HasExited, "the stand-in for the application ended before it was looked for");
            Assert.True(Look().Contains(child.Id), $"After its file was replaced, the look did not find process {child.Id}: it was given '{image}' and read '{ImageReadFor(child.Id)}'.");
        }
        finally
        {
            child.Kill();
            child.WaitForExit();
        }
    }

    /// <summary>
    /// On Linux a process's name keeps its file's extension: an AppImage the person keeps as <c>Jason.AppImage</c> runs as
    /// <c>Jason.AppImage</c>, and is found by the look as one running under the default name is. Catches a look by the name
    /// without its extension, which found no such window open and let an uninstall delete the AppImage under it.
    /// </summary>
    [Fact]
    public void On_linux_an_appimage_kept_under_a_short_name_of_its_own_is_found()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "A process's name keeps the extension on Linux; the rows beside this one hold the other systems.");
        var image = Path.Combine(_tree.Paths.Root, "My Apps", "Jason.AppImage");
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.Copy("/usr/bin/perl", image);
        var start = new System.Diagnostics.ProcessStartInfo(image) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "-e", "sleep 120" })
        {
            start.ArgumentList.Add(argument);
        }

        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            var remover = InstallationRemovers.ForThisMachine();
            IReadOnlyList<int> found = [];
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < until && !found.Contains(child.Id))
            {
                Thread.Sleep(100);
                found = remover.RunningFrom(image);
            }

            Assert.True(found.Contains(child.Id), $"The look did not find process {child.Id} running '{image}'; it read '{ImageReadFor(child.Id)}'.");
        }
        finally
        {
            child.Kill();
            child.WaitForExit();
        }
    }

    /// <summary>
    /// The application reached through a linked folder, as an application folder can be: the look is given the path through
    /// the link, the system reports the process at the path the link leads to, and the two are one file, so it is found. On
    /// macOS the temporary folder itself is reached through a link, <c>/var</c> to <c>/private/var</c>; this row adds one of
    /// its own on top, on macOS and on Linux, where the system reports a process's image at its real path too.
    /// </summary>
    [Fact]
    public void Off_Windows_the_application_reached_through_a_linked_folder_is_found()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows' own look reads the image by another call; this is the look macOS and Linux share.");

        var real = Path.Combine(_tree.Paths.Root, "Real");
        var folder = Path.Combine(real, "Jason.app", "Contents", "MacOS");
        Directory.CreateDirectory(folder);
        // A program that runs under any name stands in for the application: macOS's sleep, and on Linux perl, since a coreutils
        // built as one program for every tool picks the tool by its own file's name and knows no Jason.
        File.Copy(OperatingSystem.IsMacOS() ? "/bin/sleep" : "/usr/bin/perl", Path.Combine(folder, "Jason"));
        var linked = Path.Combine(_tree.Paths.Root, "Linked");
        Directory.CreateSymbolicLink(linked, real);
        var image = Path.Combine(linked, "Jason.app", "Contents", "MacOS", "Jason");

        // Started by the path through the link.
        var start = new System.Diagnostics.ProcessStartInfo(image)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in OperatingSystem.IsMacOS() ? ["120"] : new[] { "-e", "sleep 120" })
        {
            start.ArgumentList.Add(argument);
        }

        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            var remover = InstallationRemovers.ForThisMachine();
            IReadOnlyList<int> found = [];
            var until = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < until && !found.Contains(child.Id))
            {
                Thread.Sleep(100);
                found = remover.RunningFrom(image);
            }

            Assert.False(child.HasExited, "the stand-in for the application ended before it was looked for");
            Assert.True(found.Contains(child.Id), $"The look did not find process {child.Id}: it was given '{image}' and read '{ImageReadFor(child.Id)}' for the process.");
        }
        finally
        {
            child.Kill();
            child.WaitForExit();
        }
    }

    /// <summary>What the look reads as a process's image off Windows, for a failure to name: its main module's file.</summary>
    private static string? ImageReadFor(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return process.MainModule?.FileName;
        }
        catch (Exception unread) when (unread is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return $"nothing ({unread.GetType().Name})";
        }
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
        // The waiter is handed this data directory's log folder, for the one line it writes where the uninstaller refused.
        Assert.Equal(env.Paths.LogsDirectory, remover.WaiterLogs);
        // The report names the waiter's process and gives what follows its moment, in the help's and the page's wording.
        Assert.Contains(report.GetProperty("done").EnumerateArray(), line => line.GetString()!.Contains(
            $"which removes it once this command has ended: process {remover.Waiter} waits for that. {Documentation.PageClaimsTests.AfterTheWaiter} "
            + "Still listed then, a jason started from its folder in the meantime kept the uninstaller from removing it.",
            StringComparison.Ordinal));
        Assert.DoesNotContain(report.GetProperty("done").EnumerateArray(), line => line.GetString()!.Contains("afterwards", StringComparison.Ordinal));
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
        var places = new SetupPlaces(SetupSystem.Windows, Home, LocalAppData, keys.Environment, null) { Temporary = _tree.Paths.Root, RunKey = keys.Run, StartupApprovedKey = keys.StartupApproved, ClassesKey = keys.Classes };
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
        Assert.Contains("wait until it ends, then uninstall again", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("runtime stop", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
        Assert.True(File.Exists(Path.Combine(apps, "jason.cmd")), "the command was removed");
        Assert.True(Directory.Exists(application));

        // A dry run removes nothing and is never refused for it; nor is the uninstaller's own call, which leaves the folder.
        output.GetStringBuilder().Clear();
        Assert.Equal(ExitCodes.Success, await UninstallCommand.RunAsync(env with { Processes = processes }, new UninstallOptions(false, DryRun: true, false, false, false), Ct));
    }

    /// <summary>
    /// The runtime this uninstall stops is busy: its plugin hosts and an agent's <c>jason</c> calls for its work run the
    /// carried jason as its children and grandchildren, and its stop ends them, so they are not counted. A jason nothing of
    /// this data directory started (its parent cannot be told, or is another process) is, and the refusal names those alone
    /// and says what they are not.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_what_the_stopped_runtime_started_is_not_counted_and_what_it_did_not_start_is_named()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The application's uninstaller and its look are Windows'.");
        var executable = WindowsApplication();
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var (env, output, remover) = Environment(AbsentKeysPlaces(), executable);
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        remover.RunningInAnySession[executable] = [System.Environment.ProcessId, RuntimeVerbs.Pid, 9002, 9003, 9001, 9004];
        remover.Parents[9002] = RuntimeVerbs.Pid; // a plugin host
        remover.Parents[8000] = RuntimeVerbs.Pid; // an agent host the runtime launched for a work item...
        remover.Parents[9003] = 8000; // ...and the jason it calls
        remover.Parents[9004] = 777; // a jason another program started

        var exit = await UninstallCommand.RunAsync(env with { Processes = processes }, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal("jason_running", refusal.Code);
        Assert.Contains("(process 9001, 9004)", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"It is neither the runtime of {_dir.Paths.Root}, which this would stop, nor anything that runtime started.", refusal.Message, StringComparison.Ordinal);
        Assert.Contains($"any-session {executable}", remover.Reads);
        Assert.Empty(remover.Calls);
    }

    /// <summary>
    /// The application's uninstaller looks in every session by the image's path, so a carried jason in another session of
    /// this account (a task that runs whether the person is signed in or not) is refused here, up front, where this
    /// session's look alone would not have seen it and the uninstaller would have quit with nothing said.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_a_jason_in_another_session_is_refused_up_front()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The application's uninstaller and its look are Windows'.");
        var executable = WindowsApplication();
        var (env, output, remover) = Environment(AbsentKeysPlaces(), executable);
        remover.Running[executable] = [];
        remover.RunningInAnySession[executable] = [9005];

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal("jason_running", refusal.Code);
        Assert.Contains("(process 9005)", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("in another session", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(remover.Calls);
    }

    /// <summary>
    /// The look itself, on any system: only descendants of the runtime being stopped, at any depth, leave nothing to refuse
    /// for; a chain whose parent cannot be told, or that runs in a circle, is counted, and the look still ends.
    /// </summary>
    [Fact]
    public void The_look_leaves_out_every_descendant_of_the_runtime_it_stops_and_nothing_else()
    {
        var executable = MacApplication();
        var carried = CarriedExecutable.Of(executable)!;
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var (env, _, remover) = Environment(null, executable);
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        env = env with { Processes = processes };
        var plan = new DesktopPlan(null, ApplicationFates.AfterExit, [], null, [], string.Empty, null, [], [], []);
        remover.RunningInAnySession[carried.Executable] = [RuntimeVerbs.Pid, 9002, 9003];
        remover.Parents[9002] = RuntimeVerbs.Pid;
        remover.Parents[9003] = 8000;
        remover.Parents[8000] = 8001;
        remover.Parents[8001] = RuntimeVerbs.Pid;

        Assert.Empty(DesktopRemoval.OthersRunningTheCarriedJason(env, carried, plan));

        remover.Parents[8001] = 8000; // a circle that never reaches the runtime
        Assert.Equal([9003], DesktopRemoval.OthersRunningTheCarriedJason(env, carried, plan));
    }

    /// <summary>
    /// The real look, on Windows, against a child of this test's own: an image placed under a name of the test's own, so no
    /// other process on the machine answers to it, is found running in any session, and its parent is this process.
    /// </summary>
    [Fact]
    public void On_Windows_the_real_look_finds_a_child_by_its_image_and_its_parent()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The every-session look and the parent are read on Windows.");

        var folder = Path.Combine(_tree.Paths.Root, "Look");
        Directory.CreateDirectory(folder);
        var image = Path.Combine(folder, $"jason-look-{Guid.NewGuid():N}.exe");
        File.Copy(Path.Combine(System.Environment.SystemDirectory, "PING.EXE"), image);
        var start = new System.Diagnostics.ProcessStartInfo(image)
        {
            ArgumentList = { "-n", "120", "127.0.0.1" },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var child = System.Diagnostics.Process.Start(start)!;
        try
        {
            var remover = InstallationRemovers.ForThisMachine();

            Assert.Contains(child.Id, remover.RunningFromInAnySession(image));
            Assert.Contains(child.Id, remover.RunningFrom(image));
            Assert.Equal(System.Environment.ProcessId, remover.ParentOf(child.Id));
            Assert.DoesNotContain(child.Id, remover.RunningFromInAnySession(Path.Combine(folder, "elsewhere", Path.GetFileName(image))));
        }
        finally
        {
            child.Kill();
            child.WaitForExit();
        }
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

        // Said once, by the removal's sweep of the folder it was renamed into, which the same process still holds: a problem.
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        string Lines(string name) => string.Join("\n", report.GetProperty(name).EnumerateArray().Select(line => line.GetString()!));
        var aside = Assert.Single(Directory.GetDirectories(_tree.Paths.Root, "jason-retired-*"));
        Assert.Equal(1, Count(Lines("done") + "\n" + Lines("kept") + "\n" + Lines("problems"), aside));
        Assert.Contains(aside, Lines("problems"), StringComparison.Ordinal);
        Assert.DoesNotContain("renamed to", Lines("kept"), StringComparison.Ordinal);

        static int Count(string text, string part) => (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;
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
        var places = new SetupPlaces(SetupSystem.Windows, Home, LocalAppData, keys.Environment, null) { Temporary = _tree.Paths.Root, RunKey = keys.Run, StartupApprovedKey = keys.StartupApproved, ClassesKey = keys.Classes };
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
        Assert.Contains("$folder = $env:JASON_UNINSTALL_APPLICATION; $aside = $env:JASON_UNINSTALL_ASIDE; $logs = $env:JASON_UNINSTALL_LOGS; ", script, StringComparison.Ordinal);
        Assert.Contains("Copy-Item -LiteralPath (Join-Path $folder 'Uninstall Jason.exe') -Destination $copy", script, StringComparison.Ordinal);

        // _?= last and unquoted, a space in the folder included, and the uninstaller told the application's steps are done:
        // silently first, and once more without /S only where the silent run ended 3.
        Assert.Contains("$run = Start-Process -Wait -PassThru -FilePath $copy -ArgumentList ('/S --uninstalled-by-jason _?=' + $folder); ", script, StringComparison.Ordinal);
        Assert.Contains("if ($exit -eq 3) { $run = Start-Process -Wait -PassThru -FilePath $copy -ArgumentList ('--uninstalled-by-jason _?=' + $folder); ", script, StringComparison.Ordinal);
        Assert.Equal(2, script.Split("Start-Process", StringSplitOptions.None).Length - 1);
        // The one line is the template, whole, and written only into a log folder that is still there.
        Assert.Contains("'@mt' = '" + UninstallerHandOff.LogTemplate + "'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'", UninstallerHandOff.LogTemplate, StringComparison.Ordinal);
        Assert.Contains("if ($exit -ne 0 -and $logs -and (Test-Path -LiteralPath $logs -PathType Container)) { ", script, StringComparison.Ordinal);
        Assert.DoesNotContain("CreateDirectory($logs", script, StringComparison.Ordinal);
        Assert.EndsWith("Remove-Item -LiteralPath $aside -Recurse -Force", script, StringComparison.Ordinal);
    }

    /// <summary>A profile folder named with every quote PowerShell reads as one: the apostrophe and the four typographic ones.</summary>
    private const string Quoted = "O\u2019Brien \u2018a\u2019 \u201Cb\u201D \u201Bc\u201A d'e";

    [Fact]
    public void The_waiters_and_the_cleanups_paths_go_in_the_environment_and_never_in_the_script()
    {
        var application = Path.Combine(@"C:\Users", Quoted, @"AppData\Local\Programs\jason-desktop") + @"\";
        var temporary = Path.Combine(@"C:\Users", Quoted, @"AppData\Local\Temp");

        var logs = Path.Combine(@"C:\Users", Quoted, @".jason\logs") + @"\";

        var variables = UninstallerHandOff.Variables(application, temporary, logs);

        Assert.Equal(Path.TrimEndingDirectorySeparator(application), variables[UninstallerHandOff.ApplicationVariable]);
        Assert.StartsWith(Path.Combine(temporary, "jason-uninstall-"), variables[UninstallerHandOff.AsideVariable], StringComparison.Ordinal);
        Assert.Equal(Path.TrimEndingDirectorySeparator(logs), variables[UninstallerHandOff.LogsVariable]);
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

    /// <summary>
    /// The waiter as it ships, run by Windows PowerShell over a stand-in uninstaller that records how it was run and ends
    /// with the code a row gives a silent run and a run a person sees: 3 from the silent run runs it once more without /S;
    /// any other code but 0 at the end leaves the one line, in the desktop's own shape, in the desktop's log file of the day,
    /// which <c>jason logs --source desktop</c> reads and says; 0 leaves none; and a log folder that is gone is never made
    /// again.
    /// </summary>
    [Theory]
    [InlineData(3, 0, 2, null)]
    [InlineData(3, 5, 2, 5)]
    [InlineData(2, 0, 1, 2)]
    [InlineData(0, 0, 1, null)]
    [SupportedOSPlatform("windows")]
    public async Task The_waiter_asks_a_person_where_the_silent_uninstaller_ended_3_and_logs_any_other_refusal(int silent, int watched, int runs, int? logged)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The waiter is Windows PowerShell's.");
        var application = Path.Combine(_tree.Paths.Root, Quoted, "jason-desktop");
        Directory.CreateDirectory(application);
        File.WriteAllBytes(Path.Combine(application, DesktopRemoval.UninstallerName), UninstallerStandIn.Value);
        var logs = Directory.CreateDirectory(_tree.Paths.LogsDirectory).FullName;
        var calls = Path.Combine(_tree.Paths.Root, "calls.log");
        var variables = new Dictionary<string, string>(UninstallerHandOff.Variables(application, _tree.Paths.Root, logs))
        {
            ["JASON_STANDIN_CALLS"] = calls,
            ["JASON_STANDIN_SILENT_EXIT"] = silent.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["JASON_STANDIN_WATCHED_EXIT"] = watched.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        // It waits for a process id no process has, which it passes at once.
        var (ended, said) = PowerShell(UninstallerHandOff.Script(int.MaxValue - 1), variables);

        Assert.True(ended == 0, said);
        var made = File.ReadAllLines(calls);
        Assert.Equal(runs, made.Length);
        Assert.Equal($"/S --uninstalled-by-jason _?={application}", made[0]);
        if (runs == 2)
        {
            Assert.Equal($"--uninstalled-by-jason _?={application}", made[1]);
        }

        var files = Jason.Cli.Logs.LogFiles.In(logs, Jason.Cli.Logs.LogSource.Desktop);
        // Never the runtime's file, which the runtime's logger holds open while a runtime runs.
        Assert.Empty(Jason.Cli.Logs.LogFiles.In(logs));
        if (logged is { } code)
        {
            var file = Assert.Single(files);
            Assert.Equal($"desktop-{DateTime.Now:yyyyMMdd}.jsonl", file.Name);
            AssertTheLine(Assert.Single(File.ReadAllLines(file.Path)), code);
            Assert.NotEqual(0xEF, File.ReadAllBytes(file.Path)[0]);

            // jason logs reads it as the desktop's and says it in words, the code in its hole.
            var output = new StringWriter();
            var env = new CliEnvironment(output, new StringWriter(), _tree.Paths, RuntimeVerbs.NeverCalled());
            Assert.Equal(ExitCodes.Success, await CliApp.RunAsync(["logs", "--source", "desktop", "--human"], env, Ct));
            Assert.Contains(UninstallerHandOff.LogTemplate.Replace("{Exit}", code.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal), output.ToString(), StringComparison.Ordinal);
        }
        else
        {
            Assert.Empty(files);
        }

        Assert.False(Directory.Exists(variables[UninstallerHandOff.AsideVariable]), "the copy of the uninstaller is still there");
    }

    /// <summary>
    /// The day's newest desktop file, the one its writer rolled to, held by another writer for a moment: the waiter tries
    /// again a second later and its line lands there, beside what was there.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task The_waiters_line_lands_in_the_day_s_newest_desktop_file_once_another_writer_lets_it_go()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The waiter is Windows PowerShell's.");
        var application = Path.Combine(_tree.Paths.Root, "jason-desktop");
        Directory.CreateDirectory(application);
        File.WriteAllBytes(Path.Combine(application, DesktopRemoval.UninstallerName), UninstallerStandIn.Value);
        var logs = Directory.CreateDirectory(_tree.Paths.LogsDirectory).FullName;
        var day = DateTime.Now.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var first = Path.Combine(logs, $"desktop-{day}.jsonl");
        var rolled = Path.Combine(logs, $"desktop-{day}_001.jsonl");
        var earlier = "{\"@t\":\"2026-11-17T10:00:00.0000000Z\",\"@mt\":\"A line the desktop wrote\"}";
        File.WriteAllText(first, earlier + "\n");
        File.WriteAllText(rolled, earlier + "\n");
        var variables = new Dictionary<string, string>(UninstallerHandOff.Variables(application, _tree.Paths.Root, logs))
        {
            ["JASON_STANDIN_CALLS"] = Path.Combine(_tree.Paths.Root, "calls.log"),
            ["JASON_STANDIN_SILENT_EXIT"] = "2",
            ["JASON_STANDIN_WATCHED_EXIT"] = "0",
        };

        // Held with no write shared, as the runtime's logger holds its own file, for long enough that the first try fails.
        var held = new FileStream(rolled, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), Ct);
            await held.DisposeAsync();
        }, Ct);
        var (ended, said) = PowerShell(UninstallerHandOff.Script(int.MaxValue - 1), variables);
        await release;

        Assert.True(ended == 0, said);
        Assert.Equal([earlier], File.ReadAllLines(first));
        var lines = File.ReadAllLines(rolled);
        Assert.Equal(2, lines.Length);
        Assert.Equal(earlier, lines[0]);
        AssertTheLine(lines[1], 2);
    }

    /// <summary>The waiter's line, in the shape the desktop writes its own: the template, the level, the code alone, and its name.</summary>
    private static void AssertTheLine(string line, int code)
    {
        using var json = JsonDocument.Parse(line);
        var root = json.RootElement;
        Assert.Equal(["@t", "@mt", "@l", "Exit", "SourceContext", "EventName"], root.EnumerateObject().Select(property => property.Name));
        Assert.True(DateTimeOffset.TryParse(root.GetProperty("@t").GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _));
        Assert.Equal(UninstallerHandOff.LogTemplate, root.GetProperty("@mt").GetString());
        Assert.Equal("Warning", root.GetProperty("@l").GetString());
        Assert.Equal(code, root.GetProperty("Exit").GetInt32());
        Assert.Equal("Jason.Cli", root.GetProperty("SourceContext").GetString());
        Assert.Equal(UninstallerHandOff.LogEventName, root.GetProperty("EventName").GetString());
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void The_waiter_never_makes_a_log_folder_that_is_gone_again()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The waiter is Windows PowerShell's.");
        var application = Path.Combine(_tree.Paths.Root, "jason-desktop");
        Directory.CreateDirectory(application);
        File.WriteAllBytes(Path.Combine(application, DesktopRemoval.UninstallerName), UninstallerStandIn.Value);
        var data = Path.Combine(_tree.Paths.Root, "purged");
        var variables = new Dictionary<string, string>(UninstallerHandOff.Variables(application, _tree.Paths.Root, Path.Combine(data, "logs")))
        {
            ["JASON_STANDIN_CALLS"] = Path.Combine(_tree.Paths.Root, "calls.log"),
            ["JASON_STANDIN_SILENT_EXIT"] = "2",
            ["JASON_STANDIN_WATCHED_EXIT"] = "0",
        };

        var (ended, said) = PowerShell(UninstallerHandOff.Script(int.MaxValue - 1), variables);

        Assert.True(ended == 0, said);
        Assert.Single(File.ReadAllLines(variables["JASON_STANDIN_CALLS"]));
        Assert.False(Directory.Exists(data), "the waiter made the purged data directory again");
    }

    /// <summary>
    /// The uninstaller's stand-in, which the waiter starts with Start-Process, is a windowed program, as an NSIS
    /// uninstaller is, read from the header of the program built for it: starting it opens no console window.
    /// </summary>
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void The_uninstaller_stand_in_the_waiter_starts_is_a_windowed_program()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The waiter is Windows PowerShell; its stand-in is built by the .NET Framework's C# compiler.");

        Assert.Equal(System.Reflection.PortableExecutable.Subsystem.WindowsGui, StandInPrograms.SubsystemOf(UninstallerStandIn.Value));
    }

    /// <summary>
    /// A stand-in for the application's uninstaller, built by the .NET Framework's C# compiler every Windows carries as a
    /// program with no window, as an NSIS uninstaller is: it writes the arguments it was given, after its own name, as one
    /// line, and ends with the code a variable gives a run with /S or one without.
    /// </summary>
    private static readonly Lazy<byte[]> UninstallerStandIn = new(() =>
    {
        var csc = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "Microsoft.NET", "Framework64", "v4.0.30319", "csc.exe");
        Assert.SkipUnless(File.Exists(csc), "This Windows has no .NET Framework C# compiler to build the stand-in with.");
        var directory = Path.Combine(Path.GetTempPath(), "jason-cli-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var source = Path.Combine(directory, "uninstaller.cs");
            var output = Path.Combine(directory, "uninstaller.exe");
            File.WriteAllText(source, """
                using System; using System.IO;
                static class Program {
                  static int Main() {
                    var line = Environment.CommandLine; var self = line.StartsWith("\"") ? line.IndexOf('"', 1) + 1 : line.IndexOf(' ');
                    var given = self < 0 || self >= line.Length ? "" : line.Substring(self).Trim();
                    File.AppendAllText(Environment.GetEnvironmentVariable("JASON_STANDIN_CALLS"), given + "\n");
                    var silent = (" " + given + " ").Contains(" /S ");
                    return int.Parse(Environment.GetEnvironmentVariable(silent ? "JASON_STANDIN_SILENT_EXIT" : "JASON_STANDIN_WATCHED_EXIT"));
                  }
                }
                """);
            var start = Jason.Contracts.Discovery.ChildProcess.StartInfo(csc, Jason.Contracts.Discovery.ChildKind.SystemTool);
            foreach (var argument in new[] { "/nologo", "/target:winexe", "/out:" + output, source })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Jason.Contracts.Discovery.ChildProcess.Start(start)!;
            var said = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(60)) && process.ExitCode == 0, said);
            return File.ReadAllBytes(output);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    });

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

    // --- what the command-line removal took, taken by the carried jason too --------------------------------------

    /// <summary>
    /// The jason the application carries is a single file like any other, and its first run unpacks the build's native
    /// libraries the same way. The removal plans that directory in the same field the command-line removal does and takes
    /// it by the same routine, before the application goes; the other builds' beside it are counted and left elsewhere
    /// than Windows, and with DOTNET_BUNDLE_EXTRACT_BASE_DIR set they are not looked for.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task On_macOS_the_carried_jason_takes_what_its_build_unpacked_before_the_application_goes(bool baseMoved)
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var own = Unpacked("Xq7tzCSumrk");
        var other = Unpacked("L5GxzH7oZwh");
        var (env, output, remover) = Environment(MacPlaces(), executable);
        remover.ExtractedLibraries = own;
        if (baseMoved)
        {
            env = env with { Variables = new Dictionary<string, string> { ["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.GetDirectoryName(Path.GetDirectoryName(own))! } };
        }

        var plan = await HumanPlan(env);
        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        AssertUnpackedNamed(plan, JsonDocument.Parse(output.ToString()).RootElement, own, other, othersTaken: OperatingSystem.IsWindows() && !baseMoved, baseMoved);
        var taken = remover.Calls.IndexOf($"extracted.remove {own}");
        Assert.True(taken >= 0 && taken < remover.Calls.IndexOf($"tree.remove {application}"), string.Join(" | ", remover.Calls));
        Assert.Equal($"tree.remove {application}", remover.Calls[^1]);
    }

    /// <summary>
    /// And on Windows: the carried jason maps nothing of what its build unpacked, which only a runtime loads, so it takes
    /// its own build's directory and every other build's beside it during its run, by the same routine, before the folder
    /// is handed to the uninstaller.
    /// </summary>
    [Theory]
    [SupportedOSPlatform("windows")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task On_Windows_the_carried_jason_takes_what_its_build_and_the_others_unpacked_before_the_hand_off(bool baseMoved)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The application's uninstaller is Windows'.");
        var executable = WindowsApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", ".."));
        var own = Unpacked("Xq7tzCSumrk");
        var other = Unpacked("L5GxzH7oZwh");
        var (env, output, remover) = Environment(AbsentKeysPlaces(), executable);
        remover.ExtractedLibraries = own;
        if (baseMoved)
        {
            env = env with { Variables = new Dictionary<string, string> { ["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = Path.GetDirectoryName(Path.GetDirectoryName(own))! } };
        }

        var plan = await HumanPlan(env);
        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        AssertUnpackedNamed(plan, JsonDocument.Parse(output.ToString()).RootElement, own, other, othersTaken: !baseMoved, baseMoved);
        Assert.True(remover.Calls.IndexOf($"extracted.remove {own}") >= 0, string.Join(" | ", remover.Calls));
        Assert.Equal($"application.uninstaller {application}", remover.Calls[^1]);
    }

    /// <summary>The plan a person reads before anything goes, from a dry run that removes nothing.</summary>
    private static async Task<string> HumanPlan(CliEnvironment env)
    {
        var plan = new StringWriter();
        var exit = await UninstallCommand.RunAsync(env with { Out = plan }, new UninstallOptions(Human: true, DryRun: true, PurgeData: false, Yes: false, Force: false), Ct);
        Assert.True(exit == ExitCodes.Success, plan.ToString());
        return plan.ToString();
    }

    /// <summary>
    /// What the build unpacked is named where the command-line removal names it: in the plan a person reads, in the plan an
    /// agent reads, and in the report; the others' directories too, as taken or as left with the reason; and what is taken
    /// is gone and what is left is there.
    /// </summary>
    private static void AssertUnpackedNamed(string humanPlan, JsonElement report, string own, string other, bool othersTaken, bool baseMoved)
    {
        var why = baseMoved ? "DOTNET_BUNDLE_EXTRACT_BASE_DIR is set" : "is left, 1 directory";
        var parent = Path.GetDirectoryName(own)!;
        static IEnumerable<string> Lines(JsonElement report, string name) => report.GetProperty(name).EnumerateArray().Select(line => line.GetString()!);

        // The plan a person reads, before anything at all is removed.
        Assert.Contains($"the native libraries it unpacked, at {own}", humanPlan, StringComparison.Ordinal);
        Assert.Equal(othersTaken, humanPlan.Contains($"the native libraries another build of this program unpacked, at {other}", StringComparison.Ordinal));
        Assert.Equal(!othersTaken, humanPlan.Contains("NOT touched: ", StringComparison.Ordinal) && humanPlan.Contains(why, StringComparison.Ordinal));

        // The plan an agent reads.
        var plan = report.GetProperty("plan");
        Assert.Equal(own, plan.GetProperty("extracted_libraries").GetString());
        Assert.Equal(othersTaken ? [other] : [], plan.GetProperty("other_extracted_libraries").EnumerateArray().Select(entry => entry.GetString()!).ToArray());
        var left = plan.GetProperty("other_extracted_libraries_left");
        Assert.Equal(!othersTaken, left.ValueKind is JsonValueKind.String && left.GetString()!.Contains(parent, StringComparison.Ordinal) && left.GetString()!.Contains(why, StringComparison.Ordinal));

        // The report, and the disk.
        Assert.False(Directory.Exists(own), "what this build unpacked is still there");
        Assert.Contains(Lines(report, "done"), line => line.Contains(own, StringComparison.Ordinal));
        if (othersTaken)
        {
            Assert.False(Directory.Exists(other), "another build's unpacked libraries were left");
            Assert.Contains(Lines(report, "done"), line => line.Contains(other, StringComparison.Ordinal) && line.Contains("another build", StringComparison.Ordinal));
        }
        else
        {
            Assert.True(Directory.Exists(other), "another build's unpacked libraries were removed");
            Assert.Contains(Lines(report, "kept"), line => line.Contains(parent, StringComparison.Ordinal) && line.Contains(why, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The folders <c>jason install</c> or a move renamed a running executable into are swept by no later run once the
    /// installation is off the machine, so the removal sweeps them by the same rule: each goes once the process running it
    /// has ended, is said, and one still held is named with what to do. A folder of that name holding anything else is not
    /// the routine's, and is neither taken nor named.
    /// </summary>
    [Theory]
    [SupportedOSPlatform("windows")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task On_Windows_the_removal_sweeps_the_retired_folders_and_names_one_still_held(bool carried)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows renames a running executable aside.");
        var temporary = Directory.CreateDirectory(Path.Combine(_tree.Paths.Root, "temp")).FullName;
        var swept = Retired(temporary, "jason.exe");
        var held = Retired(temporary, "jason.exe");
        var notTheRoutines = Retired(temporary, "jason.exe", "notes.txt");
        string executable;
        if (carried)
        {
            executable = WindowsApplication();
        }
        else
        {
            executable = Path.Combine(_tree.Paths.Root, "cli", "jason.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
            File.WriteAllText(executable, "jason 0.48.5");
        }

        var places = AbsentKeysPlaces() with
        {
            Temporary = temporary,
            Delete = path =>
            {
                if (path.StartsWith(held, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"'{path}' is in use.");
                }

                File.Delete(path);
            },
        };
        var (env, output, _) = Environment(places, executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.True(exit == ExitCodes.ApiError, output.ToString());
        Assert.False(Directory.Exists(swept), "a retired folder whose process has ended is still there");
        Assert.Contains(report.GetProperty("done").EnumerateArray(), line => line.GetString()!.Contains(swept, StringComparison.Ordinal));
        Assert.True(Directory.Exists(held));
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), line => line.GetString()!.Contains(held, StringComparison.Ordinal)
            && line.GetString()!.Contains("once that process has ended", StringComparison.Ordinal));
        Assert.True(Directory.Exists(notTheRoutines));
        Assert.DoesNotContain(notTheRoutines, output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// An older command-line install the removal takes off, in a folder of the person's own that the last update recorded,
    /// whose executable a running process holds: it is renamed aside, and that folder is said once, by the sweep, with what
    /// to do, a problem since nothing sweeps it afterwards. And the sweep looks beside that install's folder too, where a
    /// rename aside on another volume puts its folder, so a retired folder an earlier run left there goes.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_Windows_an_older_install_set_aside_in_this_run_is_said_once_and_the_sweep_looks_beside_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows renames a running executable aside.");
        var temporary = Directory.CreateDirectory(Path.Combine(_tree.Paths.Root, "temp")).FullName;
        var tools = Path.Combine(_tree.Paths.Root, "tools");
        var old = Path.Combine(tools, "jason", "jason.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllText(old, "jason 0.48.5");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(old)!, "jason-LICENSE"), "MIT");
        var update = new Jason.Cli.Update.UpdatePaths(_dir.Paths);
        Directory.CreateDirectory(update.Root);
        new Jason.Contracts.Update.UpdateLedger(
            Jason.Contracts.Update.SemanticVersion.Parse("0.48.4"),
            Jason.Contracts.Update.SemanticVersion.Parse("0.48.5"),
            Jason.Contracts.Update.UpdateStep.Complete,
            DateTimeOffset.UtcNow.AddDays(-1),
            old,
            update.StagedExecutable(Jason.Contracts.Update.SemanticVersion.Parse("0.48.5")),
            update.PreviousExecutable).Write(update.Ledger);
        var besideIt = Path.Combine(tools, $".jason-retired-{Guid.NewGuid():N}");
        Directory.CreateDirectory(besideIt);
        File.WriteAllText(Path.Combine(besideIt, "jason.exe"), "jason 0.48.4");
        var executable = WindowsApplication();
        var places = AbsentKeysPlaces() with
        {
            Temporary = temporary,

            // The older install's executable, and the copy of it this run sets aside, are held by its running process.
            Delete = path =>
            {
                if (string.Equals(path, old, StringComparison.OrdinalIgnoreCase) || path.StartsWith(Path.Combine(temporary, "jason-retired-"), StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"'{path}' is in use.");
                }

                File.Delete(path);
            },
        };
        var (env, output, _) = Environment(places, executable);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.True(exit == ExitCodes.ApiError, output.ToString());
        Assert.False(File.Exists(old), "the older install's executable is still in its folder");
        var aside = Assert.Single(Directory.GetDirectories(temporary, "jason-retired-*"));
        string[] lines = [.. new[] { "done", "kept", "problems" }.SelectMany(name => report.GetProperty(name).EnumerateArray().Select(line => line.GetString()!))];
        var said = Assert.Single(lines, line => line.Contains(aside, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(said, report.GetProperty("problems").EnumerateArray().Select(line => line.GetString()!));
        Assert.Contains("once that process has ended", said, StringComparison.Ordinal);
        Assert.False(Directory.Exists(besideIt), "a retired folder beside the older install's folder was not swept");
        Assert.Contains(report.GetProperty("done").EnumerateArray(), line => line.GetString()!.Contains(besideIt, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Open Jason at Login is taken off after the runtime has stopped, so a runtime that will not stop leaves the
    /// installation as the refusal says it is, the application's own start at login included. Nothing starts the window
    /// between the stop and the take-off: an open window refuses the verb before anything, and a login of an account
    /// already signed in starts nothing again.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_will_not_stop_leaves_Open_Jason_at_Login_where_it_was()
    {
        var executable = MacApplication();
        var application = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, "..", "..", ".."));
        var agent = DesktopRemoval.LaunchAgentFile(MacPlaces());
        Directory.CreateDirectory(Path.GetDirectoryName(agent)!);
        File.WriteAllText(agent, $"<plist><dict><key>ProgramArguments</key>\n<array>\n<string>{Path.Combine(application, "Contents", "MacOS", "Jason")}</string>\n<string>--opened-at-login</string></array></dict></plist>");
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_STUBBORN"));
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        var (env, output, remover) = Environment(MacPlaces(), executable);
        env = env with
        {
            Processes = processes,
            HttpHandler = new FakeHandler(_ => RuntimeVerbs.Response(System.Net.HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_STUBBORN"))),
        };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains(CliErrors.RuntimeStillRunning, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("is as it was, its logon registration included", output.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(agent), "Open Jason at Login was taken off by an uninstall that refused");
        Assert.Empty(remover.Calls);
    }

    /// <summary>
    /// The pid file a carried update writes beside the carried jason, and leaves where that update was killed, is one of
    /// the application's own names there: it does not make the Path entry another program's.
    /// </summary>
    [Fact]
    public void The_pid_file_a_carried_update_writes_is_one_of_the_bundles_own()
    {
        var bundle = Path.GetDirectoryName(WindowsApplication())!;
        File.WriteAllText(Path.Combine(bundle, Jason.Cli.Update.CarriedUpdate.PidFileName), "4321 134000000000000000");

        Assert.True(PathEntry.CarriedBundle(bundle, windows: true), "a pid file a killed update left made the bundle another program's");

        File.WriteAllText(Path.Combine(bundle, "someone-elses.exe"), "x");
        Assert.False(PathEntry.CarriedBundle(bundle, windows: true));
    }

    /// <summary>A build's unpacked libraries of the test's own, under a base of the test's own.</summary>
    private string Unpacked(string id)
    {
        var directory = Path.Combine(_tree.Paths.Root, "net", "jason", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "e_sqlite3.dll"), "native");
        return directory;
    }

    /// <summary>A <c>jason-retired-&lt;id&gt;</c> folder of the test's own, holding the files named.</summary>
    private static string Retired(string temporary, params string[] files)
    {
        var folder = Path.Combine(temporary, $"jason-retired-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        foreach (var file in files)
        {
            File.WriteAllText(Path.Combine(folder, file), "x");
        }

        return folder;
    }

    // --- Linux -----------------------------------------------------------------------------------------------------

    private string XdgData => Path.Combine(_tree.Paths.Root, "xdg", "data");

    private string XdgConfig => Path.Combine(_tree.Paths.Root, "xdg", "config");

    private string XdgCache => Path.Combine(_tree.Paths.Root, "xdg", "cache");

    /// <summary>
    /// With <c>--purge-data</c> as without it: an AppImage's jason runs as a copy under the data directory's
    /// <c>desktop/bundle</c>, which is the data directory's own and goes with it, so that copy is not an installation the
    /// data directory holds and the purge is not refused for it. Catches the purge refused on every AppImage machine. And the
    /// folder the install line made for the file, <c>~/Applications</c>, goes once it holds nothing else, while a folder of the
    /// person's own choosing stays: catches an empty <c>~/Applications</c> left in the home. The menu entry and Open Jason at
    /// Login's entry are found by what their writers write, whatever the file's path holds: catches either left behind for a
    /// folder whose name holds a quote, a backslash or a dollar sign, the menu entry for one with a per-cent too.
    /// </summary>
    [Theory]
    [InlineData(false, false, """My "Apps" 100% \ $x""")]
    [InlineData(true, false, """My "Apps" \ $x""")]
    [InlineData(false, true, "")]
    [Critical(Family.I3, "on Linux one uninstall takes everything of this account's an AppImage placed, and the file last")]
    public async Task From_an_AppImage_it_takes_off_everything_of_this_accounts_and_deletes_the_file_last(bool purge, bool linePlace, string folder)
    {
        var places = LinuxPlaces();
        var (copy, file) = AppImage(linePlace, folder);
        var bin = places.LocalBin;
        var wrapper = Written(Path.Combine(bin, "jason"), CommandFiles.Wrapper(copy, null));
        var profile = Path.Combine(Home, ".profile");
        var bashrc = Written(Path.Combine(Home, ".bashrc"), "alias ll='ls -l'\n");
        PathEntry.AddToProfile(profile, bin);
        PathEntry.AddToProfile(bashrc, bin);
        CreatedProfiles.Add(places, profile);
        var skill = UninstallReceiptTests.Recorded(Path.Combine(Home, ".claude", "skills"), "operating-the-installation");
        // The application offers no Open Jason at Login for a launcher with a per-cent (login-item.ts, autostartExec); the other
        // special folder's entry is spelled as desktop/test/login.test.ts pins autostartEntry's.
        var atLogin = file.Contains('%', StringComparison.Ordinal)
            ? null
            : linePlace
                ? OpenAtLoginEntry(file)
                : OpenAtLoginEntry(Path.Combine(Home, """My "Apps" \\ $x""", "Jason.AppImage"), "\"" + Path.Combine(Home, """My \\"Apps\\" \\\\ \\$x""", "Jason.AppImage") + "\"");
        var menu = Written(Path.Combine(XdgData, "applications", "jason-desktop.desktop"), $"[Desktop Entry]\nTryExec={DesktopRemoval.DesktopEntryString(file)}\nName=Jason\nExec={DesktopRemoval.DesktopEntryArgument(file)} %U\nIcon=jason-desktop-app\n");
        string[] icons = [.. new[] { "256x256", "512x512" }.Select(size => Written(Path.Combine(XdgData, "icons", "hicolor", size, "apps", "jason-desktop-app.png"), "png"))];
        var anotherIcon = Written(Path.Combine(XdgData, "icons", "hicolor", "256x256", "apps", "another-app.png"), "png");
        var desktop = Path.Combine(_dir.Paths.Root, "desktop");
        Bundle(Path.Combine(desktop, "bundle", "0.33.0"), "jason");
        Plant(Path.Combine(desktop, "rollback", "0.33.0"));
        var settings = Written(Path.Combine(desktop, "settings.json"), "{}");
        var updater = Plant(Path.Combine(XdgCache, "jason-desktop-updater"));
        var harnessRoot = Plant(Path.Combine(Home, "Jason"));
        var (env, output, remover) = Environment(places, copy);

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, purge, purge, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        var plan = JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop");
        Assert.Equal("deleted", plan.GetProperty("application_fate").GetString());
        Assert.Equal(file, plan.GetProperty("application").GetString());
        foreach (var gone in new[] { wrapper, profile, skill, atLogin ?? menu, menu, Path.Combine(desktop, "appimage.json"), file }.Concat(icons))
        {
            Assert.False(File.Exists(gone), $"{gone} is still there");
        }

        foreach (var gone in new[] { bin, Path.Combine(desktop, "bundle"), Path.Combine(desktop, "rollback"), places.SupportFolder, updater })
        {
            Assert.False(Directory.Exists(gone), $"{gone} is still there");
        }

        Assert.Equal("alias ll='ls -l'\n", File.ReadAllText(bashrc));
        Assert.True(File.Exists(anotherIcon), "another application's icon went");
        Assert.Equal(!purge, File.Exists(settings));
        Assert.True(Directory.Exists(harnessRoot), "the harness root went");
        Assert.Equal(!linePlace, Directory.Exists(Path.GetDirectoryName(file)));

        // The file is the last thing removed, after the data directory where it is purged, and only the empty folder the
        // install line made goes after it: everything above ran from the copy of the jason it carries.
        Assert.Equal($"file.remove {file}", remover.Calls[linePlace ? ^2 : ^1]);
    }

    [Fact]
    public async Task From_the_deb_the_package_is_taken_off_once_the_runtime_has_stopped_and_before_anything_of_this_accounts()
    {
        var places = LinuxPlaces();
        var carried = Deb(places);
        var (atLogin, wrapper, skill, updater) = PlantForTheDeb(places, carried);
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var log = new StepLog();
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        var (env, output, remover) = Environment(places, carried, log);
        remover.Purges = _ =>
        {
            // As apt does: the package's files go, the jason this runs as among them.
            Directory.Delete(places.DebApplication, recursive: true);
            return new PackageRun(true, 0, string.Empty);
        };
        env = env with { Processes = processes, HttpHandler = StopsWhenAsked(log, processes) };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.Success, output.ToString());
        Assert.Equal("purged", JsonDocument.Parse(output.ToString()).RootElement.GetProperty("plan").GetProperty("desktop").GetProperty("application_fate").GetString());
        var stopped = log.Steps.IndexOf("system.shutdown");
        var purged = log.Steps.IndexOf("package.purge");
        var firstOfTheAccounts = log.Steps.FindIndex(step => step is "file.remove" or "tree.remove" or "directory.remove_if_empty");
        Assert.True(stopped >= 0 && purged > stopped && firstOfTheAccounts > purged, string.Join(" | ", log.Steps));
        // apt is waited for longer than the package lock it is told to wait for, so its own answer comes first.
        Assert.True(remover.PurgeBounds.Single() > Jason.Cli.Tests.Update.CommandLineMoveLinuxTests.LockWait(MoveSystem.PurgeArguments(DesktopRemoval.DebPackage)), $"the purge was waited for {remover.PurgeBounds.Single()}");
        foreach (var gone in new[] { atLogin, wrapper, skill })
        {
            Assert.False(File.Exists(gone), $"{gone} is still there");
        }

        Assert.False(Directory.Exists(updater), "the updater's cache is still there");
        Assert.False(File.Exists(_dir.Paths.StartOwedFile), "a start is owed after the installation went");
        Assert.True(Directory.Exists(_dir.Paths.Root), "the data directory went without the word");
    }

    [Theory]
    [Critical(Family.I3, "on Linux a package that stays leaves everything of this account's in place")]
    [InlineData("no password given", true, 126, "Error executing command as another user: Request dismissed")]
    [InlineData("no session to ask in", false, 127, "")]
    [InlineData("apt not ended", true, PackageRun.TimedOut, "")]
    public async Task From_the_deb_a_purge_refused_removes_nothing_of_this_accounts_and_starts_the_runtime_again_unless_apt_may_still_run(string row, bool asked, int code, string said)
    {
        var places = LinuxPlaces();
        var carried = Deb(places);
        var (atLogin, wrapper, skill, updater) = PlantForTheDeb(places, carried);
        _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var log = new StepLog();
        var processes = new FakeProcessControl();
        processes.RunningPids.Add(RuntimeVerbs.Pid);
        processes.OnLaunch = _ =>
        {
            _dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_AGAIN", pid: 5151));
            processes.RunningPids.Add(5151);
            return new FakeProcessHandle(5151);
        };
        var (env, output, remover) = Environment(places, carried, log);
        remover.Purges = _ => new PackageRun(asked, code, said);
        env = env with { Processes = processes, HttpHandler = StopsWhenAsked(log, processes) };

        var exit = await UninstallCommand.RunAsync(env, new UninstallOptions(false, false, false, false, false), Ct);

        Assert.True(exit == ExitCodes.ApiError, $"{row}: {output}");
        var report = JsonDocument.Parse(output.ToString()).RootElement;
        Assert.Equal(CliErrors.UninstallRefused, report.GetProperty("refusal_code").GetString());
        Assert.Contains("sudo apt purge jason-desktop", report.GetProperty("refusal").GetString(), StringComparison.Ordinal);
        Assert.Equal(["package.purge jason-desktop"], remover.Calls);
        foreach (var stays in new[] { atLogin, wrapper, skill, carried })
        {
            Assert.True(File.Exists(stays), $"{row}: {stays} went");
        }

        Assert.True(Directory.Exists(updater), $"{row}: the updater's cache went");

        // The runtime it stopped, started again from the application's jason on this data directory; but not while apt, which
        // had not ended, may still be taking that jason off: catches a runtime started from files apt is removing.
        if (row == "apt not ended")
        {
            Assert.Empty(processes.Launches);
            Assert.Contains("apt had not ended after 20 minutes", report.GetProperty("refusal").GetString(), StringComparison.Ordinal);
            Assert.Contains("was not started again", report.GetProperty("refusal").GetString(), StringComparison.Ordinal);
            Assert.Contains("where apt took it off, `jason` went with it", report.GetProperty("refusal").GetString(), StringComparison.Ordinal);
            Assert.True(File.Exists(_dir.Paths.StartOwedFile), "the note that a start is owed went");
        }
        else
        {
            Assert.Equal([_dir.Paths.Root], processes.Launches);
            Assert.Equal([carried], processes.Executables.Single());
        }
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

    private SetupPlaces MacPlaces() => new(SetupSystem.MacOs, Home, null, UserPathValue.EnvironmentKey, "/bin/zsh") { Temporary = _tree.Paths.Root, AccountHome = () => Home };

    /// <summary>
    /// Windows places whose registry keys are never made: a refusal reads them and finds nothing, and nothing writes under
    /// this account's registry.
    /// </summary>
    private SetupPlaces AbsentKeysPlaces()
    {
        var absent = $@"Software\jason-tests\absent-{Guid.NewGuid():N}";
        return new SetupPlaces(SetupSystem.Windows, Home, LocalAppData, $@"{absent}\Environment", null)
        {
            Temporary = _tree.Paths.Root,
            RunKey = $@"{absent}\Run",
            StartupApprovedKey = $@"{absent}\StartupApproved",
            ClassesKey = $@"{absent}\Classes",
        };
    }

    private (CliEnvironment Env, StringWriter Output, RecordingRemover Remover) Environment(SetupPlaces? places, string executable, StepLog? log = null)
    {
        // The removal sweeps the temporary directory the places name for retired folders: never the machine's own.
        Assert.True(
            places is null || !string.Equals(Path.GetFullPath(places.Temporary), Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase),
            "these places name the machine's own temporary directory");
        var output = new StringWriter();
        var remover = new RecordingRemover(log) { ReallyRemoves = true };
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            _dir.Paths,
            HttpHandler: RuntimeVerbs.NeverCalled(),
            Processes: new FakeProcessControl(),
            InstallPath: executable,
            Autostart: new RecordingRegistrar(places?.Platform switch
            {
                SetupSystem.Windows => Jason.Cli.Autostart.AutostartPlatform.Windows,
                SetupSystem.Linux => Jason.Cli.Autostart.AutostartPlatform.Linux,
                _ => Jason.Cli.Autostart.AutostartPlatform.MacOs,
            }),
            Harnesses: HarnessLocators.ForHome(Home),
            Removes: remover,
            Variables: new Dictionary<string, string>(),
            Setup: places);
        return (env, output, remover);
    }

    /// <summary>Linux places of the test's own: its home, XDG folders away from it, and an <c>/opt/Jason</c> of its own.</summary>
    private SetupPlaces LinuxPlaces() => new(SetupSystem.Linux, Home, null, UserPathValue.EnvironmentKey, "/bin/bash")
    {
        Temporary = _tree.Paths.Root,
        AccountHome = () => Home,
        DataHome = XdgData,
        ConfigHome = XdgConfig,
        CacheHome = XdgCache,
        DebApplication = Path.Combine(_tree.Paths.Root, "opt", "Jason"),
        SystemCommand = Path.Combine(_tree.Paths.Root, "usr", "bin", "jason"),
        AptSource = Path.Combine(_tree.Paths.Root, "etc", "apt", "sources.list.d", "jason-desktop.sources"),
    };

    /// <summary>
    /// An AppImage the person keeps in a folder of their own, which its start recorded in <c>appimage.json</c>, and the copy
    /// of its jason under the data directory, which is the jason that runs.
    /// </summary>
    private (string Copy, string File) AppImage(bool linePlace = false, string folder = "My Apps")
    {
        var file = Written(linePlace ? Path.Combine(Home, "Applications", "jason-desktop-linux-x64.AppImage") : Path.Combine(Home, folder, "Jason.AppImage"), "appimage");
        var copy = Bundle(Path.Combine(_dir.Paths.Root, "desktop", "bundle", "0.34.0"), "jason");
        Written(Path.Combine(_dir.Paths.Root, "desktop", "appimage.json"), JsonSerializer.Serialize(new { file }));
        return (copy, file);
    }

    /// <summary>The deb's application folder of the test's own, as the package installs it, and the jason it carries.</summary>
    private static string Deb(SetupPlaces places)
    {
        Written(Path.Combine(places.DebApplication, "jason-desktop-app"), "electron");
        Written(Path.Combine(places.DebApplication, "resources", "package-type"), "deb\n");
        return Bundle(Path.Combine(places.DebApplication, "resources", "bundle"), "jason");
    }

    /// <summary>What the deb's route leaves in this account: Open Jason at Login, a 0.49.0 wrapper, a skill by receipt, the updater's cache.</summary>
    private (string AtLogin, string Wrapper, string Skill, string Updater) PlantForTheDeb(SetupPlaces places, string carried) =>
        (OpenAtLoginEntry(Path.Combine(places.DebApplication, "jason-desktop-app")),
            Written(Path.Combine(places.LocalBin, "jason"), CommandFiles.Wrapper(carried, null)),
            UninstallReceiptTests.Recorded(Path.Combine(Home, ".claude", "skills"), "operating-the-installation"),
            Plant(Path.Combine(XdgCache, "jason-desktop-updater")));

    /// <summary>
    /// Open Jason at Login as the application writes it on Linux, an XDG autostart entry starting a launcher whose path needs
    /// no escape; or, for one that does, its TryExec and quoted Exec values as given, spelled by the test.
    /// </summary>
    private string OpenAtLoginEntry(string launcher, string? execValue = null) =>
        Written(
            Path.Combine(XdgConfig, "autostart", DesktopRemoval.Identifier + ".desktop"),
            $"[Desktop Entry]\n# Written by the Jason desktop application.\nType=Application\nName=Jason\nTryExec={launcher}\nExec={execValue ?? $"\"{launcher}\""} --opened-at-login\n");

    /// <summary>A runtime that goes when asked: its descriptor, then its process.</summary>
    private FakeHandler StopsWhenAsked(StepLog log, FakeProcessControl processes) => new(_ =>
    {
        log.Add("system.shutdown");
        File.Delete(_dir.Paths.DescriptorFile);
        processes.RunningPids.Remove(RuntimeVerbs.Pid);
        return RuntimeVerbs.Response(System.Net.HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"));
    });

    private static string Written(string file, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
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
