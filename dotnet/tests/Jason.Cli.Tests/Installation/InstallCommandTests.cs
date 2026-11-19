using System.Net;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Jason.Cli.Autostart;
using Jason.Cli.Installation;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Cli.Tests.Skills;
using Jason.Cli.Uninstall;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Harness;
using Jason.Contracts.Json;
using Jason.Contracts.Skills;
using Jason.Runtime.Tests;
using Microsoft.Win32;

namespace Jason.Cli.Tests.Installation;

/// <summary>
/// <c>jason install</c>, the machine setup, run against a scratch home, a scratch data directory, a carried folder of the
/// test's own and, on Windows, a registry key of the test's own: never the person's profiles, Path or skills.
/// </summary>
public sealed class InstallCommandTests : IDisposable
{
    private readonly TempPaths _data = new();
    private readonly TempPaths _tree = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Home => Path.Combine(_tree.Paths.Root, "home");

    private string LocalAppData => Path.Combine(_tree.Paths.Root, "local");

    public InstallCommandTests()
    {
        // The data directory as a person's first start finds it: not there at all.
        Directory.Delete(_data.Paths.Root);
        Directory.CreateDirectory(Home);
    }

    public void Dispose()
    {
        _data.Dispose();
        _tree.Dispose();
    }

    // --- refusals ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_jason_the_application_does_not_carry_is_refused_and_nothing_is_written()
    {
        var executable = Path.Combine(_tree.Paths.Root, "loose", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "jason");
        var (env, output) = Environment(MacPlaces(), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(CliErrors.InstallNotCarried, RuntimeVerbs.Envelope(output.ToString()).Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Home));
        Assert.False(Directory.Exists(_data.Paths.Root));
    }

    [Fact]
    public async Task A_machine_with_no_places_to_set_up_is_refused()
    {
        var (env, output) = Environment(null, Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(CliErrors.InstallUnsupported, RuntimeVerbs.Envelope(output.ToString()).Code);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Home));
    }

    /// <summary>
    /// An elevated <c>jason install</c> - run as an administrator or as another account than the session's on Windows, as
    /// root on macOS - would write the terminal command, the skills and a logon registration as that account, into a
    /// profile the person's own jason cannot change: it refuses with install_elevated before anything is read, its own
    /// version.json included, and before the update's lock is looked at, which another jason holds here.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_elevated_setup_is_refused_before_anything_is_read(bool windows)
    {
        var executable = Path.Combine(_tree.Paths.Root, "loose", windows ? "jason.exe" : "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
        File.WriteAllText(executable, "jason");
        var update = new Jason.Cli.Update.UpdatePaths(_data.Paths);
        Directory.CreateDirectory(update.Root);
        var places = windows ? WindowsPlaces(@"Software\Jason.Tests\never-opened") : MacPlaces();
        var (env, output) = Environment(places with { Elevated = true }, executable);

        using (Jason.Cli.Update.UpdateLock.Take(update, "a test's update"))
        {
            var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

            Assert.Equal(ExitCodes.ApiError, exit);
            var refusal = RuntimeVerbs.Envelope(output.ToString());
            Assert.Equal(CliErrors.InstallElevated, refusal.Code);
            Assert.False(refusal.Retryable);
            Assert.Contains(
                windows
                    ? "This jason runs as an administrator, elevated above this account's ordinary rights, or as another account than the one signed in to this session"
                    : "This jason runs as root",
                refusal.Message,
                StringComparison.Ordinal);
            Assert.Contains("so nothing was read or written", refusal.Message, StringComparison.Ordinal);
            Assert.Contains(windows ? "not run as an administrator" : "without sudo", refusal.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Home));
        }
    }

    /// <summary>
    /// From a place that goes away - a copy macOS runs translocated, or a volume that is read-only, as a mounted disk image
    /// is - the setup refuses with install_vanishing_place before anything is stopped, written or retired: no update lock
    /// taken, so no holder file; the command-line install at ~/.local/bin/jason, its profile and its runtime as they were.
    /// The sentence names both Applications folders and says nothing was placed, retired or stopped.
    /// </summary>
    [Theory]
    [InlineData("translocated")]
    [InlineData("read_only_volume")]
    public async Task From_a_vanishing_place_the_setup_refuses_before_it_stops_writes_or_retires_anything(string place)
    {
        var translocated = Path.Combine(_tree.Paths.Root, "private", "var", "folders", "x", "T", "AppTranslocation", "6F1C2E0A", "d");
        var executable = place == "translocated" ? Carried(under: translocated) : Carried();
        var oldInstall = Path.Combine(Home, ".local", "bin", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(oldInstall)!);
        File.WriteAllText(oldInstall, "jason 0.48.5");
        File.WriteAllText(Path.Combine(Home, ".zprofile"), "export PATH=\"$HOME/.local/bin:$PATH\"\n");
        var update = new Jason.Cli.Update.UpdatePaths(_data.Paths);
        Directory.CreateDirectory(update.Root);
        var before = Snapshot(Home);
        var asked = new List<string>();
        var places = MacPlaces() with { ReadOnlyVolume = folder => { asked.Add(folder); return place == "read_only_volume"; } };
        var (env, output) = Environment(places, executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.InstallVanishingPlace, refusal.Code);
        Assert.False(refusal.Retryable);
        Assert.Contains("Nothing was placed, retired or stopped", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("Applications", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("~/Applications", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(before, Snapshot(Home));
        Assert.False(File.Exists(update.LockHolder), "the update's lock was taken before the gate");
        Assert.Equal([update.Root], Directory.EnumerateFileSystemEntries(_data.Paths.Root));
        Assert.Empty(Directory.EnumerateFileSystemEntries(update.Root));
        if (place == "read_only_volume")
        {
            Assert.Equal([Path.GetDirectoryName(executable)!], asked);
        }
    }

    /// <summary>
    /// A second disk a person keeps is no vanishing place by its path's spelling: a jason under a volume's folder whose
    /// volume is writable sets the machine up.
    /// </summary>
    [Fact]
    public async Task A_second_disk_a_person_keeps_is_not_refused_by_its_path()
    {
        var executable = Carried(under: Path.Combine(_tree.Paths.Root, "Volumes", "Jason 0.49.0"));
        var (env, output) = Environment(MacPlaces() with { ReadOnlyVolume = _ => false }, executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(Report(output).GetProperty("completed").GetBoolean());
    }

    /// <summary>
    /// Windows is not gated: nothing publishes a build of Jason Desktop that can sit on read-only media, so the look is
    /// never asked there, and a translocation mark means nothing on it.
    /// </summary>
    [Fact]
    public void Windows_is_never_asked_whether_its_place_goes_away()
    {
        var executable = Carried(under: Path.Combine(_tree.Paths.Root, "AppTranslocation", "d"));
        var asked = false;
        var places = WindowsPlaces("unused") with { ReadOnlyVolume = _ => asked = true };

        Assert.Null(VanishingPlace.Refusal(CarriedExecutable.Of(executable)!, places));
        Assert.False(asked);
        Assert.False(SetupPlacesDefaults().ReadOnlyVolume(Home), "the seam answers false on this machine's own places unless it is macOS with a read-only volume");
    }

    /// <summary>
    /// The update's door runs the routine in its own process with no InstallCommand before it: the routine refuses at its
    /// own entry with the same sentence as its one problem, nothing placed and nothing retired.
    /// </summary>
    [Fact]
    public async Task The_routine_itself_refuses_from_a_vanishing_place_for_every_door()
    {
        var executable = Carried();
        var oldInstall = Path.Combine(Home, ".local", "bin", "jason");
        Directory.CreateDirectory(Path.GetDirectoryName(oldInstall)!);
        File.WriteAllText(oldInstall, "jason 0.48.5");
        var before = Snapshot(Home);
        var places = MacPlaces() with { ReadOnlyVolume = _ => true };
        var (env, _) = Environment(places, executable);

        var report = await MachineSetup.RunAsync(env, CarriedExecutable.Of(executable)!, places, new InstallOptions(false, false, null), Ct);

        Assert.False(report.Completed);
        Assert.Null(report.Command);
        Assert.Null(report.Retired);
        Assert.Empty(report.Skills);
        Assert.Equal([VanishingPlace.Refusal(CarriedExecutable.Of(executable)!, places)!], report.Problems);
        Assert.Equal(before, Snapshot(Home));
    }

    /// <summary>
    /// The gate stands first in both places, so a later reorder goes red here: in the verb before the update's lock is
    /// read or taken, and in the routine before the sweep of what earlier runs renamed aside.
    /// </summary>
    [Fact]
    public void The_gate_stands_before_the_lock_and_before_the_sweep()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var installation = Path.Combine(root.FullName, "dotnet", "src", "Jason.Cli", "Installation");
        var verb = File.ReadAllText(Path.Combine(installation, "InstallCommand.cs"));
        var body = verb[verb.IndexOf("public static async Task<int> RunAsync(", StringComparison.Ordinal)..];
        Assert.True(body.IndexOf("VanishingPlace.Refusal(", StringComparison.Ordinal) is > 0 and var gate && gate < body.IndexOf("UpdateLock.Handed(", StringComparison.Ordinal), "the verb's gate stands before the lock");
        var routine = File.ReadAllText(Path.Combine(installation, "MachineSetup.cs"));
        var entry = routine[routine.IndexOf("public static async Task<InstallReport> RunAsync(", StringComparison.Ordinal)..];
        Assert.True(entry.IndexOf("VanishingPlace.Refusal(", StringComparison.Ordinal) is > 0 and var first && first < entry.IndexOf("RetiredSweep.Sweep(", StringComparison.Ordinal), "the routine's gate stands before the sweep");
    }

    private static SetupPlaces SetupPlacesDefaults() =>
        new(OperatingSystem.IsMacOS() ? SetupSystem.MacOs : SetupSystem.Windows, Path.GetTempPath(), null, UserPathValue.EnvironmentKey, null);

    /// <summary>
    /// One at a time with an update: while another jason holds the update's lock - an update whose finish sets the machine up
    /// itself, or the installer's own run beside it - the setup refuses with update_in_progress and sets nothing up; once the
    /// lock is let go it runs, and takes the lock while it does.
    /// </summary>
    [Fact]
    [Critical(Family.I3, "jason install and an update's finish never set the machine up at once")]
    public async Task While_an_update_holds_its_lock_the_setup_refuses_and_sets_nothing_up()
    {
        var executable = Carried();
        var update = new Jason.Cli.Update.UpdatePaths(_data.Paths);
        Directory.CreateDirectory(update.Root);
        var (env, output) = Environment(MacPlaces(), executable);

        using (Jason.Cli.Update.UpdateLock.Take(update, "a test's update"))
        {
            var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

            Assert.Equal(ExitCodes.ApiError, exit);
            var refusal = RuntimeVerbs.Envelope(output.ToString());
            Assert.Equal(Jason.Cli.Update.UpdateCodes.InProgress, refusal.Code);
            Assert.Contains("an update's own finish sets the machine up itself", refusal.Message, StringComparison.Ordinal);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Home));
        }

        var (again, said) = Environment(MacPlaces(), executable);
        var setting = Task.Run(() => InstallCommand.RunAsync(again, new InstallOptions(false, false, null), Ct), Ct);
        Assert.Equal(ExitCodes.Success, await setting);
        Assert.True(Report(said).GetProperty("completed").GetBoolean());
        using var free = Jason.Cli.Update.UpdateLock.Take(update, "the next update");
    }

    /// <summary>
    /// A second <c>jason install</c> beside one that holds the lock refuses in words of its own, install_in_progress, and
    /// names the process: no update is in flight, and the other setup takes seconds.
    /// </summary>
    [Fact]
    public async Task A_second_setup_beside_one_that_holds_the_lock_refuses_as_a_setup()
    {
        var executable = Carried();
        var update = new Jason.Cli.Update.UpdatePaths(_data.Paths);
        Directory.CreateDirectory(update.Root);
        var (env, output) = Environment(MacPlaces(), executable);

        using (Jason.Cli.Update.UpdateLock.Take(update, "a test's setup", holder: Jason.Cli.Update.UpdateLock.Holder.Install))
        {
            Assert.Equal(ExitCodes.ApiError, await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct));
        }

        var refusal = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.InstallInProgress, refusal.Code);
        Assert.Contains($"`jason install` is setting this machine up, process {System.Environment.ProcessId}", refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("updating", refusal.Message, StringComparison.Ordinal);
        Assert.True(refusal.Retryable);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Home));
    }

    /// <summary>
    /// The lock is the open file, not the file: one a process left when it ended, its holder still named beside it, refuses
    /// nobody, and the setup runs.
    /// </summary>
    [Fact]
    public async Task A_lock_file_a_dead_process_left_refuses_nobody()
    {
        var update = new Jason.Cli.Update.UpdatePaths(_data.Paths);
        Directory.CreateDirectory(update.Root);
        File.WriteAllText(update.Lock, string.Empty);
        File.WriteAllText(update.LockHolder, "install 999999 left-behind");
        var (env, output) = Environment(MacPlaces(), Carried());

        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct));
        Assert.True(Report(output).GetProperty("completed").GetBoolean());
        Assert.False(File.Exists(update.LockHolder), "the setup left its own name beside a lock it let go");
    }

    /// <summary>
    /// A setup the lock's holder runs as its waited child, handed the holder's nonce in JASON_UPDATE_LOCK as the update's own
    /// parts are, goes on under that hold and leaves it held; one handed no nonce, or another, is refused by what holds the
    /// lock and sets nothing up.
    /// </summary>
    [Theory]
    [InlineData("update", "handed")]
    [InlineData("install", "handed")]
    [InlineData("update", "none")]
    [InlineData("install", "none")]
    [InlineData("update", "wrong")]
    [InlineData("install", "wrong")]
    public async Task A_setup_handed_the_holders_nonce_runs_under_its_hold_and_no_other_does(string holder, string nonce)
    {
        var update = new Jason.Cli.Update.UpdatePaths(_data.Paths);
        Directory.CreateDirectory(update.Root);
        using var held = Jason.Cli.Update.UpdateLock.Take(
            update,
            "a test's holder",
            holder: holder == "install" ? Jason.Cli.Update.UpdateLock.Holder.Install : Jason.Cli.Update.UpdateLock.Holder.Update);
        var named = File.ReadAllText(update.LockHolder);
        var variables = new Dictionary<string, string>();
        if (nonce != "none")
        {
            variables[Jason.Cli.Update.UpdateLock.Variable] = nonce == "handed" ? held.HandOver() : "0123456789abcdef0123456789abcdef";
        }

        var (env, output) = Environment(MacPlaces(), Carried(), variables);
        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        if (nonce == "handed")
        {
            Assert.Equal(ExitCodes.Success, exit);
            Assert.True(Report(output).GetProperty("completed").GetBoolean());
        }
        else
        {
            Assert.Equal(ExitCodes.ApiError, exit);
            Assert.Equal(
                holder == "install" ? CliErrors.InstallInProgress : Jason.Cli.Update.UpdateCodes.InProgress,
                RuntimeVerbs.Envelope(output.ToString()).Code);
            Assert.Empty(Directory.EnumerateFileSystemEntries(Home));
        }

        Assert.Equal(named, File.ReadAllText(update.LockHolder));
        var second = Assert.Throws<Jason.Cli.Update.UpdateException>(() => Jason.Cli.Update.UpdateLock.Take(update, "a second update").Dispose());
        Assert.Equal(holder == "install" ? CliErrors.InstallInProgress : Jason.Cli.Update.UpdateCodes.InProgress, second.Code);
    }

    /// <summary>A data directory with no update folder: the setup takes no lock and makes none.</summary>
    [Fact]
    public async Task With_no_update_folder_the_setup_makes_no_lock()
    {
        Directory.CreateDirectory(_data.Paths.Root);
        var (env, _) = Environment(MacPlaces(), Carried());

        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct));

        Assert.False(Directory.Exists(new Jason.Cli.Update.UpdatePaths(_data.Paths).Root), "the setup made an update folder");
    }

    // --- macOS: the wrapper and the profile lines ---------------------------------------------------------

    [Fact]
    [Critical(Family.I3, "jason install writes nothing under the data directory without --roles")]
    public async Task On_macos_it_places_the_wrapper_and_the_install_scripts_lines_and_nothing_under_the_data_directory()
    {
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var wrapper = Path.Combine(Home, ".local", "bin", "jason");
        Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(wrapper));
        Assert.Equal($"#!/bin/sh\n# Written by the Jason desktop application.\nexec '{executable}' \"$@\"\n", File.ReadAllText(wrapper));
        var line = $"export PATH=\"{Path.Combine(Home, ".local", "bin")}:$PATH\"";
        foreach (var profile in new[] { ".zprofile", ".zshrc" })
        {
            Assert.Equal($"\n# added by jason install\n{line}\n", File.ReadAllText(Path.Combine(Home, profile)));
        }

        Assert.False(File.Exists(Path.Combine(Home, ".profile")), "zsh's account got a ~/.profile");
        Assert.False(Directory.Exists(_data.Paths.Root), "the data directory was made, which turns a first start's Demo into Live");

        var report = Report(output);
        Assert.Equal("0.34.0", report.GetProperty("version").GetString());
        Assert.Equal(wrapper, report.GetProperty("command").GetProperty("runs").GetString());
        Assert.Equal(wrapper, report.GetProperty("command").GetProperty("open_shell").GetString());
        Assert.True(report.GetProperty("command").GetProperty("changed").GetBoolean());
        Assert.Equal(2, report.GetProperty("command").GetProperty("profiles").GetArrayLength());
        Assert.True(report.GetProperty("command").GetProperty("placed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Equal("not_running", report.GetProperty("runtime").GetString());
        Assert.True(report.GetProperty("completed").GetBoolean());
    }

    /// <summary>
    /// The routine records each profile it brings into being, and none it finds there, so the uninstall can tell a file Jason
    /// made from one the person had (CreatedProfiles).
    /// </summary>
    [Fact]
    public async Task The_profiles_the_routine_creates_are_recorded_and_the_ones_it_finds_are_not()
    {
        var zshrc = Path.Combine(Home, ".zshrc");
        File.WriteAllText(zshrc, "alias ll='ls -l'\n");
        var (env, _) = Environment(MacPlaces(), Carried());

        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct));

        var created = CreatedProfiles.Read(Home);
        Assert.Contains(Path.GetFullPath(Path.Combine(Home, ".zprofile")), created);
        Assert.DoesNotContain(Path.GetFullPath(zshrc), created);
        Assert.Equal(Path.Combine(Home, "Library", "Application Support", "ai.jasonharness.desktop", "created-profiles.json"), CreatedProfiles.RecordFile(Home));
        Assert.False(Directory.Exists(_data.Paths.Root), "the record went under the data directory");

        // A second run finds the file it made and records nothing new, losing nothing.
        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct));
        Assert.Equal(created, CreatedProfiles.Read(Home));
    }

    [Fact]
    public async Task A_second_run_changes_nothing()
    {
        var executable = Carried();
        PlantHosts(claude: true, codex: true);
        var (env, _) = Environment(MacPlaces(), executable);
        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct));
        var before = Snapshot(Home);

        var (again, output) = Environment(MacPlaces(), executable);
        var exit = await InstallCommand.RunAsync(again, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(before, Snapshot(Home));
        var report = Report(output);
        Assert.False(report.GetProperty("command").GetProperty("changed").GetBoolean());
        Assert.Equal(0, report.GetProperty("command").GetProperty("profiles").GetArrayLength());
        Assert.True(report.GetProperty("command").GetProperty("placed").GetBoolean(), "a second run that finds the command in place reports it not placed");
        Assert.All(report.GetProperty("skills").EnumerateArray(), root => Assert.Equal(0, root.GetProperty("written").GetInt32()));
    }

    /// <summary>
    /// The gate <c>install.sh</c> keeps before it writes a profile, judged by the PATH a terminal has: the agent's script passes
    /// nothing, and the routine reads its own process's PATH, as the script does; the application passes its login shell's
    /// PATH with <c>--terminal-path</c>, or the empty value where the shell gave no answer, which names no folder. The
    /// process's own PATH is the seam's here, so a row that leaves the option out is judged by it and the rest are not.
    /// </summary>
    [Theory]
    [InlineData("on the terminal's PATH", "/usr/bin:{bin}", "/usr/bin:/bin", false)]
    [InlineData("only on the caller's own PATH", "/usr/bin:/bin", "/usr/bin:{bin}:/bin", true)]
    [InlineData("the terminal's PATH unknown", "", "{bin}:/usr/bin:/bin", true)]
    [InlineData("no option, on the routine's own PATH", null, "/usr/bin:{bin}", false)]
    [InlineData("no option, not on the routine's own PATH", null, "/usr/bin:/bin", true)]
    public async Task The_profiles_are_written_only_where_a_terminal_does_not_find_the_folder(string row, string? terminalPath, string ownPath, bool written)
    {
        var bin = Path.Combine(Home, ".local", "bin");
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);
        env = env with { SearchPath = ownPath.Replace("{bin}", bin, StringComparison.Ordinal) };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, TerminalPath: terminalPath?.Replace("{bin}", bin, StringComparison.Ordinal)), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(Path.Combine(bin, "jason")));
        var report = Report(output);
        var profiles = report.GetProperty("command").GetProperty("profiles");
        if (written)
        {
            var line = $"export PATH=\"{bin}:$PATH\"";
            foreach (var profile in new[] { ".zprofile", ".zshrc" })
            {
                Assert.Equal($"\n# added by jason install\n{line}\n", File.ReadAllText(Path.Combine(Home, profile)));
            }

            Assert.Equal(2, profiles.GetArrayLength());
        }
        else
        {
            Assert.False(File.Exists(Path.Combine(Home, ".zprofile")), $"{row}: a profile was written for a folder a terminal finds");
            Assert.False(File.Exists(Path.Combine(Home, ".zshrc")), $"{row}: a profile was written for a folder a terminal finds");
            Assert.Equal(0, profiles.GetArrayLength());
            Assert.Contains(report.GetProperty("done").EnumerateArray(), said => said.GetString() == $"{bin} is on the PATH a terminal has already, so no profile was written.");
        }
    }

    /// <summary>
    /// The option as a caller types it: the empty value reaches the routine as "the shell gave no answer", and a PATH as that
    /// PATH, with the routine's own PATH naming the folder in both, so only the option decides.
    /// </summary>
    [Theory]
    [InlineData("", true)]
    [InlineData("/usr/bin:{bin}", false)]
    public async Task The_terminal_path_option_is_read_as_the_caller_types_it(string value, bool written)
    {
        var bin = Path.Combine(Home, ".local", "bin");
        var (env, _) = Environment(MacPlaces(), Carried());
        env = env with { SearchPath = bin };

        var exit = await CliApp.RunAsync(["install", "--terminal-path", value.Replace("{bin}", bin, StringComparison.Ordinal)], env, Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(written, File.Exists(Path.Combine(Home, ".zprofile")));
    }

    /// <summary>
    /// What a new terminal finds, read once the Path entry is placed: the machine's Path comes before the account's, so a
    /// jason on it answers before the entry appended last. The entry is placed all the same; the report names the file it
    /// finds, a problem says what removes it - a command-line install's own update, or else removing it or taking its folder
    /// off the Path - and the file is left as it is.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_a_jason_first_on_the_machine_Path_is_named_with_what_removes_it_and_left(bool commandLineInstall)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows Path is Windows's.");
        var account = new AccountPathInMemory();
        var tools = Directory.CreateDirectory(Path.Combine(_tree.Paths.Root, "tools")).FullName;
        var decoy = Path.Combine(tools, "jason.exe");
        File.WriteAllText(decoy, "another jason");
        if (commandLineInstall)
        {
            File.WriteAllText(Jason.Contracts.Update.ReleaseAssets.NoticeBeside(decoy, Jason.Contracts.Update.ReleaseAssets.Notices[0]), "MIT");
        }

        var executable = Carried("jason.exe");
        var places = WindowsPlaces(NoKey) with { AccountPath = account, MachinePath = () => new StoredPath(tools, RegistryValueKind.ExpandString), PathExtensions = ".COM;.EXE;.BAT;.CMD" };
        var (env, output) = Environment(places, executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = Report(output);
        var command = report.GetProperty("command");
        Assert.True(command.GetProperty("placed").GetBoolean());
        Assert.Equal(decoy, command.GetProperty("finds").GetString());
        Assert.False(command.GetProperty("finds_this").GetBoolean(), "another jason first by name reads as this one, and the install scripts would end saying jason is in the terminal");
        var first = $"A terminal opened from now on finds {decoy} first by the name jason, not this jason ({executable})";
        Assert.Contains(
            commandLineInstall
                ? $"{first}: it is a command-line install of Jason, and its own `jason update apply` moves it into Jason Desktop. It was left as it is."
                : $"{first}. It is not a file of Jason's, so it was left as it is: remove it, or take its folder off the PATH.",
            report.GetProperty("problems").EnumerateArray().Select(said => said.GetString()));
        Assert.Equal("another jason", File.ReadAllText(decoy));
        Assert.True(PathEntry.Carries(account.Stored!.Raw, Path.GetDirectoryName(executable)!));
    }

    /// <summary>With nothing before it, the entry appended last is what a new terminal finds: this jason, and no problem.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_this_jason_is_what_a_new_terminal_finds_where_nothing_comes_before_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows Path is Windows's.");
        var account = new AccountPathInMemory(new StoredPath(Path.Combine(_tree.Paths.Root, "empty"), RegistryValueKind.ExpandString));
        var executable = Carried("jason.exe");
        var (env, output) = Environment(WindowsPlaces(NoKey) with { AccountPath = account, PathExtensions = ".COM;.EXE;.BAT;.CMD" }, executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var command = Report(output).GetProperty("command");
        Assert.Equal(executable, command.GetProperty("finds").GetString());
        Assert.True(command.GetProperty("finds_this").GetBoolean());
        Assert.Equal((Path.Combine(_tree.Paths.Root, "empty") + ";" + Path.GetDirectoryName(executable), RegistryValueKind.ExpandString), (account.Stored!.Raw, account.Stored.Kind));
    }

    /// <summary>
    /// A command-line install held in this run - its runtime would not stop - still answers as jason: on Windows, as on
    /// macOS, the terminal command is left as it is, the Path unwritten, placed false, and the problem says why.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_a_held_command_line_install_leaves_the_Path_as_it_is()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows Path is Windows's.");
        var before = Path.Combine(_tree.Paths.Root, "before");
        var account = new AccountPathInMemory(new StoredPath(before, RegistryValueKind.ExpandString));
        var (old, _, _, env, output) = OldRuntimeStopping(_ => RuntimeVerbs.Response(HttpStatusCode.InternalServerError, """{"error":{"code":"internal","message":"no","retryable":false}}"""), WindowsPlaces(NoKey) with { AccountPath = account });

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = Report(output);
        Assert.Equal(MachineSetup.RuntimeStates.WouldNotStop, report.GetProperty("runtime").GetString());
        var command = report.GetProperty("command");
        Assert.False(command.GetProperty("placed").GetBoolean());
        Assert.False(command.GetProperty("changed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, command.GetProperty("finds").ValueKind);
        Assert.False(command.GetProperty("finds_this").GetBoolean());
        Assert.Equal(new StoredPath(before, RegistryValueKind.ExpandString), account.Stored);
        Assert.Contains(
            $"The command-line install at {old} is not retired in this run, so this account's Path was left as it is: a new terminal still finds that jason.",
            report.GetProperty("problems").EnumerateArray().Select(said => said.GetString()));
        Assert.True(File.Exists(old));
    }

    /// <summary>
    /// macOS: where <c>~/.local/bin</c> is on the terminal's PATH already, no profile line is written to put it in front,
    /// and a jason earlier on that PATH is what a new terminal finds: named, with what removes it, and left. Where the lines
    /// are written they put it in front, and the wrapper is what it finds.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [UnsupportedOSPlatform("windows")]
    public async Task On_macos_a_jason_earlier_on_the_terminal_s_PATH_is_named_unless_the_lines_put_the_wrapper_in_front(bool onPathAlready)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A macOS PATH and an execute bit are not a Windows host's.");
        var bin = Path.Combine(Home, ".local", "bin");
        var tools = Directory.CreateDirectory(Path.Combine(_tree.Paths.Root, "usr-local-bin")).FullName;
        var decoy = Path.Combine(tools, "jason");
        File.WriteAllText(decoy, "another jason");
        File.SetUnixFileMode(decoy, (UnixFileMode)0b111_101_101);
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);
        var terminal = onPathAlready ? $"{tools}:{bin}:/usr/bin" : $"{tools}:/usr/bin";

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, TerminalPath: terminal), Ct);

        var report = Report(output);
        var command = report.GetProperty("command");
        Assert.True(command.GetProperty("placed").GetBoolean());
        if (onPathAlready)
        {
            Assert.Equal(ExitCodes.ApiError, exit);
            Assert.Equal(decoy, command.GetProperty("finds").GetString());
            Assert.False(command.GetProperty("finds_this").GetBoolean());
            Assert.Contains(
                $"A terminal opened from now on finds {decoy} first by the name jason, not this jason ({executable}). It is not a file of Jason's, so it was left as it is: remove it, or take its folder off the PATH.",
                report.GetProperty("problems").EnumerateArray().Select(said => said.GetString()));
            Assert.Equal("another jason", File.ReadAllText(decoy));
        }
        else
        {
            Assert.Equal(ExitCodes.Success, exit);
            Assert.Equal(Path.Combine(bin, "jason"), command.GetProperty("finds").GetString());
            Assert.True(command.GetProperty("finds_this").GetBoolean(), "the wrapper running this jason reads as another file");
        }
    }

    /// <summary>The gate is macOS's: on Windows a terminal PATH that names the folder already still leaves the Path entry written.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_the_terminal_path_changes_nothing()
    {
        using var key = ScratchKey.Create();
        key.SetPath(@"C:\Tools", RegistryValueKind.String);
        var executable = Carried("jason.exe");
        var bundle = Path.GetDirectoryName(executable)!;
        var (env, output) = Environment(WindowsPlaces(key.SubKey), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, TerminalPath: bundle), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal((@"C:\Tools;" + bundle, RegistryValueKind.String), key.ReadPath());
        Assert.True(Report(output).GetProperty("command").GetProperty("changed").GetBoolean());
    }

    [Fact]
    public async Task A_profile_that_holds_the_line_already_takes_nothing()
    {
        var line = $"export PATH=\"{Path.Combine(Home, ".local", "bin")}:$PATH\"";
        File.WriteAllText(Path.Combine(Home, ".zshrc"), $"alias ll='ls -l'\n{line}\n");
        var (env, _) = Environment(MacPlaces(), Carried());

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal($"alias ll='ls -l'\n{line}\n", File.ReadAllText(Path.Combine(Home, ".zshrc")));
        Assert.Contains("# added by jason install", File.ReadAllText(Path.Combine(Home, ".zprofile")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_wrapper_names_the_data_directory_where_one_is_set()
    {
        var executable = Carried();
        var (env, _) = Environment(MacPlaces(), executable);
        env = env with { Variables = new Dictionary<string, string> { ["JASON_DATA_DIR"] = _data.Paths.Root } };

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        var target = CommandFiles.WrapperTarget(File.ReadAllText(Path.Combine(Home, ".local", "bin", "jason")));
        Assert.Equal((executable, (string?)_data.Paths.Root), target);
    }

    /// <summary>
    /// The move's routine, <c>jason install --retire</c>, is started with <c>JASON_DATA_DIR</c> always set, so "set" does not
    /// mean the person named one. The wrapper names a data directory only where it is not the default one: with the variable
    /// naming <c>~/.jason</c> it is the wrapper every other door writes, and with a directory of the person's own it names it.
    /// </summary>
    [Theory]
    [InlineData("the default")]
    [InlineData("the default, cased otherwise")]
    [InlineData("another")]
    public async Task The_routine_s_wrapper_names_a_data_directory_only_where_it_is_not_the_default(string spelled)
    {
        // The default spelled in another case is the same folder where the platform's file systems ignore case, as the
        // tree's own folder comparison says, and another folder where they do not.
        var theDefault = spelled == "the default" || (spelled == "the default, cased otherwise" && FolderPaths.Comparison == StringComparison.OrdinalIgnoreCase);
        var old = PlantMacCommandLineInstall();
        var data = new JasonPaths(spelled switch
        {
            "the default" => Path.Combine(Home, JasonPaths.DefaultFolder),
            "the default, cased otherwise" => Path.Combine(Home, JasonPaths.DefaultFolder.ToUpperInvariant()),
            _ => _data.Paths.Root,
        });
        var executable = Carried();
        var (env, _) = Environment(MacPlaces(), executable, new Dictionary<string, string> { [JasonPaths.DataDirectoryVariable] = data.Root });
        env = env with { Paths = data };

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, old), Ct);

        var target = CommandFiles.WrapperTarget(File.ReadAllText(Path.Combine(Home, ".local", "bin", "jason")));
        Assert.Equal((executable, theDefault ? null : (string?)data.Root), target);
        if (theDefault)
        {
            Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(Path.Combine(Home, ".local", "bin", "jason")));
        }
    }

    [Fact]
    public async Task Another_programs_jason_in_local_bin_is_left_and_nothing_is_placed()
    {
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "jason"), "#!/bin/sh\necho somebody else's\n");
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal("#!/bin/sh\necho somebody else's\n", File.ReadAllText(Path.Combine(bin, "jason")));
        Assert.False(File.Exists(Path.Combine(Home, ".zprofile")), "the lines were written for a jason that is not Jason's");
        var report = Report(output);
        Assert.Contains("another program's", report.GetProperty("problems")[0].GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("command").ValueKind);
    }

    [Fact]
    public async Task An_older_wrapper_of_the_applications_is_pointed_at_this_jason()
    {
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "jason"), CommandFiles.Wrapper("/Applications/Old.app/Contents/Resources/bundle/jason", null));
        var executable = Carried();
        var (env, _) = Environment(MacPlaces(), executable);

        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct));

        Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(Path.Combine(bin, "jason")));
    }

    // --- macOS: retiring a command-line install --------------------------------------------------------------

    [Fact]
    public async Task On_macos_the_command_line_install_is_replaced_by_the_wrapper_its_notices_go_and_its_lines_stay()
    {
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "jason"), new byte[200_000]);
        File.WriteAllText(Path.Combine(bin, "jason-LICENSE"), "MIT");
        File.WriteAllText(Path.Combine(bin, "jason-THIRD-PARTY-NOTICES"), "notices");
        var line = $"export PATH=\"{bin}:$PATH\"";
        File.WriteAllText(Path.Combine(Home, ".zprofile"), $"\n# added by jason install\n{line}\n");
        File.WriteAllText(Path.Combine(Home, ".zshrc"), $"\n# added by jason install\n{line}\n");
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(Path.Combine(bin, "jason")));
        Assert.False(File.Exists(Path.Combine(bin, "jason-LICENSE")));
        Assert.False(File.Exists(Path.Combine(bin, "jason-THIRD-PARTY-NOTICES")));
        Assert.Equal($"\n# added by jason install\n{line}\n", File.ReadAllText(Path.Combine(Home, ".zprofile")));
        var retired = Report(output).GetProperty("retired");
        Assert.Equal(Path.Combine(bin, "jason"), retired.GetProperty("executable").GetString());
        Assert.Equal(2, retired.GetProperty("removed").GetArrayLength());
    }

    [Fact]
    public async Task The_logon_registration_that_named_the_retired_executable_is_made_again_naming_this_jason()
    {
        var old = PlantMacCommandLineInstall();
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [old, "runtime", "run"], null) };
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);
        env = env with { Autostart = registrar };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var applied = Assert.Single(registrar.Applied);
        Assert.Equal(executable, applied.Run[0]);
        Assert.True(Report(output).GetProperty("retired").GetProperty("reregistered").GetBoolean());
    }

    [Fact]
    public async Task A_registration_for_another_data_directory_is_left_as_it_is()
    {
        var old = PlantMacCommandLineInstall();
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [old, "runtime", "run", "--data-dir", Path.Combine(_tree.Paths.Root, "other")], null) };
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { Autostart = registrar };

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Empty(registrar.Applied);
        Assert.False(Report(output).GetProperty("retired").GetProperty("reregistered").GetBoolean());
    }

    [Fact]
    public async Task A_runtime_running_from_the_retired_executable_is_drained_and_stopped_and_none_is_started()
    {
        var old = PlantMacCommandLineInstall();
        var owedAtTheStop = false;
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD") with { InstalledImage = old });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var asked = new List<string>();
        var handler = new FakeHandler(request =>
        {
            var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
            asked.Add(operation);
            switch (operation)
            {
                case Operations.SystemDrain:
                    return RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(new DrainResponse(DispatcherState.Draining, 0), JasonJson.Options));
                case Operations.SystemShutdown:
                    owedAtTheStop = File.Exists(_data.Paths.StartOwedFile);
                    File.Delete(_data.Paths.DescriptorFile);
                    processes.RunningPids.Clear();
                    return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_OLD"));
                default:
                    return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_OLD"));
            }
        });
        var (env, output) = Environment(MacPlaces(), Carried());
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 17, 15, 5, 35, TimeSpan.Zero));
        env = env with { HttpHandler = handler, Processes = processes, Clock = clock };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(Operations.SystemDrain, asked[0]);
        Assert.Contains(Operations.SystemShutdown, asked);
        Assert.Empty(processes.Launches);
        var report = Report(output);
        Assert.Equal("stopped", report.GetProperty("runtime").GetString());
        Assert.Contains(report.GetProperty("left").EnumerateArray(), line => line.GetString()!.Contains("`jason runtime start`", StringComparison.Ordinal)
            && line.GetString()!.Contains(_data.Paths.StartOwedFile, StringComparison.Ordinal));

        // The stop owes a start, and the note says so from before the drain until one starts.
        Assert.True(owedAtTheStop, "the note that a start is owed was not on disk when the runtime was asked to stop");
        Assert.Equal(Path.Combine(_data.Paths.Root, "run", "start-owed.json"), _data.Paths.StartOwedFile);
        Assert.Equal("{\"stopped_at\":\"2026-11-17T15:05:35Z\"}", File.ReadAllText(_data.Paths.StartOwedFile));
        Assert.Equal((true, (DateTimeOffset?)clock.GetUtcNow()), StartOwed.Read(_data.Paths));
    }

    /// <summary>
    /// A routine killed or timed out during the stop's wait (its caller gives up while the runtime, which has acknowledged
    /// the shutdown, is still going) has started nothing and ends with the stop unfinished; the note written before the drain
    /// stands, for the next to find. The seam is the routine's own cancellation, taken while it polls for the runtime's exit,
    /// which the routine says as a problem of an incomplete report; a kill that ends the process says nothing, and leaves the
    /// same note.
    /// </summary>
    [Fact]
    public async Task A_routine_killed_during_the_stops_wait_leaves_the_start_owed()
    {
        var old = PlantMacCommandLineInstall();
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD") with { InstalledImage = old });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        using var killed = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var handler = new FakeHandler(request =>
        {
            var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
            if (operation == Operations.SystemShutdown)
            {
                // Acknowledged, and still running: the routine now waits for the exit, and is killed there.
                killed.CancelAfter(TimeSpan.FromMilliseconds(300));
                return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_OLD"));
            }

            return operation == Operations.SystemDrain
                ? RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(new DrainResponse(DispatcherState.Draining, 0), JasonJson.Options))
                : RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_OLD"));
        });
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { HttpHandler = handler, Processes = processes };

        var exit = await InstallCommand.RunAsync(
            env,
            new InstallOptions(false, false, null, StopTimeout: TimeSpan.FromSeconds(30), StopPoll: RuntimeVerbs.Poll),
            killed.Token);

        Assert.True(killed.IsCancellationRequested, "the routine ended before it was killed, so this row tested nothing");
        Assert.NotEqual(ExitCodes.Success, exit);
        var report = Report(output);
        Assert.False(report.GetProperty("completed").GetBoolean());
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("stopped part-way", StringComparison.Ordinal));
        Assert.Contains(RuntimeVerbs.Pid, processes.RunningPids);
        Assert.True(StartOwed.Read(_data.Paths) is (true, { }), "a routine killed during the stop's wait left no note that a start is owed");
        Assert.Empty(processes.Launches);
    }

    /// <summary>
    /// The Demo case. Jason Desktop's mode record says demo while a runtime a terminal started runs on that data directory, from
    /// the command-line install being retired. A person's install, or the window's own update, runs this routine, which stops
    /// that runtime; the application then comes back in the Demo, which starts no runtime. The note that a start is owed is
    /// what is left to say one is owed, and it stands, for the application to read at its start.
    /// </summary>
    [Fact]
    public async Task The_Demo_case_a_runtime_stopped_under_a_demo_mode_record_leaves_the_start_owed()
    {
        var old = PlantMacCommandLineInstall();
        Directory.CreateDirectory(_data.Paths.Root);
        var mode = Path.Combine(_data.Paths.Root, "desktop", "mode");
        Directory.CreateDirectory(Path.GetDirectoryName(mode)!);
        File.WriteAllText(mode, "demo\n");
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD") with { InstalledImage = old });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var handler = new FakeHandler(request =>
        {
            var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
            if (operation == Operations.SystemShutdown)
            {
                File.Delete(_data.Paths.DescriptorFile);
                processes.RunningPids.Clear();
                return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_OLD"));
            }

            return operation == Operations.SystemDrain
                ? RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(new DrainResponse(DispatcherState.Draining, 0), JasonJson.Options))
                : RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_OLD"));
        });
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { HttpHandler = handler, Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("stopped", Report(output).GetProperty("runtime").GetString());
        Assert.Empty(processes.Launches);
        Assert.Equal("demo\n", File.ReadAllText(mode));
        Assert.True(StartOwed.Read(_data.Paths) is (true, { }), "the Demo case: no note says a start is owed, and nothing in the Demo starts one");
    }

    [Fact]
    public async Task A_runtime_of_another_executable_is_left_running()
    {
        PlantMacCommandLineInstall();
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_ELSE") with { InstalledImage = Path.Combine(_tree.Paths.Root, "elsewhere", "jason") });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { HttpHandler = RuntimeVerbs.NeverCalled(), Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("running", Report(output).GetProperty("runtime").GetString());
        Assert.False(File.Exists(_data.Paths.StartOwedFile), "a start was said to be owed where nothing was stopped");
    }

    /// <summary>Called where nothing is to be retired, it touches no runtime: the runtime is not even asked anything.</summary>
    [Fact]
    public async Task Where_nothing_is_retired_no_runtime_is_asked_stopped_or_started()
    {
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE") with { InstalledImage = Path.Combine(_tree.Paths.Root, "elsewhere", "jason") });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { HttpHandler = RuntimeVerbs.NeverCalled(), Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(processes.Launches);
        Assert.Equal("running", Report(output).GetProperty("runtime").GetString());
        Assert.Equal(JsonValueKind.Null, Report(output).GetProperty("retired").ValueKind);
        Assert.False(File.Exists(_data.Paths.StartOwedFile), "a start was said to be owed where nothing was stopped");
    }

    // --- the skills ----------------------------------------------------------------------------------------

    [Fact]
    public async Task The_harness_skills_go_to_claude_code_and_codex_where_found_and_are_recorded()
    {
        PlantHosts(claude: true, codex: true);
        Directory.CreateDirectory(Path.Combine(Home, ".gemini", "antigravity-cli"));
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        foreach (var root in new[] { Path.Combine(Home, ".claude", "skills"), Path.Combine(Home, ".agents", "skills") })
        {
            Assert.True(File.Exists(Path.Combine(root, "operating", "SKILL.md")), $"no runtime pack in {root}");
            Assert.True(File.Exists(Path.Combine(root, "campaign-planning", "SKILL.md")), $"no business pack in {root}");
            Assert.NotNull(SkillsRecord.Read(root));
        }

        Assert.False(Directory.Exists(Path.Combine(Home, ".gemini", "antigravity-cli", "skills")), "a host other than Claude Code and Codex was deployed into");
        Assert.False(Directory.Exists(_data.Paths.Root), "the harness skills wrote under the data directory");
        Assert.Equal(2, Report(output).GetProperty("skills").GetArrayLength());
    }

    [Fact]
    public async Task With_no_host_found_no_skills_are_deployed_and_it_says_so()
    {
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var report = Report(output);
        Assert.Equal(0, report.GetProperty("skills").GetArrayLength());
        Assert.Contains(report.GetProperty("left").EnumerateArray(), line => line.GetString()!.Contains("No Claude Code", StringComparison.Ordinal));
        Assert.False(Directory.Exists(Path.Combine(Home, ".claude")));
    }

    [Fact]
    [Critical(Family.I3, "a person's own skill under a name Jason ships is left alone")]
    public async Task A_persons_own_skill_under_a_name_jason_ships_is_left_alone_and_named()
    {
        PlantHosts(claude: true, codex: false);
        var own = Path.Combine(Home, ".claude", "skills", "operating");
        Directory.CreateDirectory(own);
        File.WriteAllText(Path.Combine(own, "SKILL.md"), "mine");
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(own, "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(Home, ".claude", "skills", "campaign-planning", "SKILL.md")));
        var root = Assert.Single(Report(output).GetProperty("skills").EnumerateArray());
        Assert.Equal(["operating"], root.GetProperty("left_alone").EnumerateArray().Select(name => name.GetString()));
        var recorded = SkillsRecord.Read(Path.Combine(Home, ".claude", "skills"))!.Packs.SelectMany(pack => pack.Files).Select(file => file.Path);
        Assert.DoesNotContain(recorded, path => path.StartsWith("operating/", StringComparison.Ordinal));
    }

    /// <summary>
    /// The set-up's deployment is a door of the one rule like every other, into a host's skills and into the roles home: a
    /// folder of the person's under a name this release ships is left alone and named, whether a record of an earlier
    /// release is there or none is, and the uninstall after it keeps that folder and takes Jason's.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Critical(Family.I3, "the set-up leaves a person's own skill alone with a record there and without, and the uninstall keeps it")]
    public async Task A_folder_of_your_own_under_a_name_the_release_ships_is_left_alone_by_the_setup_and_kept_by_the_uninstall(bool recorded, bool roles)
    {
        PlantHosts(claude: true, codex: false);
        var executable = Carried();
        var bundle = Path.GetDirectoryName(executable)!;
        var root = roles ? _data.Paths.RoleSkillsDirectory : Path.Combine(Home, ".claude", "skills");
        var name = roles ? "planner" : "quick-start";
        if (recorded)
        {
            // An earlier release, which did not ship the name, deployed here first.
            var (first, _) = Environment(MacPlaces(), executable);
            Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(first, new InstallOptions(false, roles, null), Ct));
            Assert.NotNull(SkillsRecord.Read(root));
        }

        SkillsLeftAloneTests.Skill(Path.Combine(bundle, "skills", "runtime", roles ? Path.Combine("roles", name) : name), name, "Jason's");
        var own = SkillsLeftAloneTests.Plant(root, name);
        var before = SkillsLeftAloneTests.Snapshot(own);
        var (env, output) = Environment(MacPlaces(), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, roles, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(before, SkillsLeftAloneTests.Snapshot(own));
        var report = Report(output);
        var sentence = SkillsDeployer.LeftAloneSentence(new UnrecordedSkill(root, SkillPacks.Runtime, name, RolesHome: roles));
        Assert.Contains(report.GetProperty("left").EnumerateArray(), line => line.GetString() == sentence);
        var outcome = roles ? report.GetProperty("roles") : Assert.Single(report.GetProperty("skills").EnumerateArray());
        Assert.Equal([name], outcome.GetProperty("left_alone").EnumerateArray().Select(left => left.GetString()));
        var record = SkillsRecord.Read(root)!;
        Assert.DoesNotContain(record.Packs.SelectMany(pack => pack.Files), file => file.Path.StartsWith(name + "/", StringComparison.Ordinal));
        var jasons = Path.Combine(root, roles ? "researcher" : "operating", "SKILL.md");
        Assert.True(File.Exists(jasons));

        var (removed, _) = await SkillsLeftAloneTests.UninstallAsync(_data.Paths, HarnessLocators.ForHome(Home), Path.Combine(_tree.Paths.Root, "bin", "jason"));
        Assert.Equal(ExitCodes.Success, removed);
        Assert.Equal(before, SkillsLeftAloneTests.Snapshot(own));
        Assert.False(File.Exists(jasons), "a skill Jason deployed survived the uninstall");
    }

    [Fact]
    public async Task With_roles_the_role_skills_go_into_the_data_directory()
    {
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, true, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(File.Exists(Path.Combine(_data.Paths.RoleSkillsDirectory, "researcher", "SKILL.md")));
        Assert.Equal(_data.Paths.RoleSkillsDirectory, Report(output).GetProperty("roles").GetProperty("root").GetString());
    }

    // --- Windows ---------------------------------------------------------------------------------------------

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_the_bundle_folder_goes_on_the_path_once_and_the_applications_command_files_are_taken_back()
    {
        using var key = ScratchKey.Create();
        key.SetPath(@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps", RegistryValueKind.ExpandString);
        var executable = Carried("jason.exe");
        var apps = Path.Combine(LocalAppData, "Microsoft", "WindowsApps");
        Directory.CreateDirectory(apps);
        File.WriteAllText(Path.Combine(apps, "jason.cmd"), $"@echo off\r\n\"{executable}\" %*\r\n");
        File.WriteAllText(Path.Combine(apps, "jason"), CommandFiles.Wrapper(executable.Replace('\\', '/'), null));
        File.WriteAllText(Path.Combine(apps, "jason-desktop.cmd"), "@echo off\r\nsomebody else's\r\n");
        var (env, output) = Environment(WindowsPlaces(key.SubKey), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var bundle = Path.GetDirectoryName(executable)!;
        Assert.Equal((@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;" + bundle, RegistryValueKind.ExpandString), key.ReadPath());
        Assert.False(File.Exists(Path.Combine(apps, "jason.cmd")));
        Assert.False(File.Exists(Path.Combine(apps, "jason")));
        Assert.True(File.Exists(Path.Combine(apps, "jason-desktop.cmd")), "a file the application did not write was removed");
        var command = Report(output).GetProperty("command");
        Assert.Equal(bundle, command.GetProperty("runs").GetString());
        Assert.Equal(executable, command.GetProperty("open_shell").GetString());
        Assert.Equal(2, command.GetProperty("taken_back").GetArrayLength());
        Assert.True(command.GetProperty("placed").GetBoolean());

        var (again, second) = Environment(WindowsPlaces(key.SubKey), executable);
        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(again, new InstallOptions(false, false, null), Ct));
        Assert.Equal((@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;" + bundle, RegistryValueKind.ExpandString), key.ReadPath());
        Assert.False(Report(second).GetProperty("command").GetProperty("changed").GetBoolean());
        Assert.False(Directory.Exists(_data.Paths.Root));
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_the_command_line_install_goes_with_its_path_entry_and_its_folder()
    {
        using var key = ScratchKey.Create();
        var folder = Path.Combine(LocalAppData, "Programs", "jason");
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "jason.exe", "jason-LICENSE", "jason-THIRD-PARTY-NOTICES", "jason.previous.exe" })
        {
            File.WriteAllText(Path.Combine(folder, name), name);
        }

        key.SetPath(@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;" + folder, RegistryValueKind.ExpandString);
        var executable = Carried("jason.exe");
        var (env, output) = Environment(WindowsPlaces(key.SubKey), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(Directory.Exists(folder), "the command-line install's folder is still there");
        Assert.Equal((@"%USERPROFILE%\AppData\Local\Microsoft\WindowsApps;" + Path.GetDirectoryName(executable), RegistryValueKind.ExpandString), key.ReadPath());
        var retired = Report(output).GetProperty("retired");
        Assert.Equal(Path.Combine(folder, "jason.exe"), retired.GetProperty("executable").GetString());
        Assert.Equal(JsonValueKind.Null, retired.GetProperty("aside").ValueKind);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_an_executable_a_running_process_holds_is_renamed_aside_and_nothing_is_started_to_wait()
    {
        using var key = ScratchKey.Create();
        var folder = Path.Combine(LocalAppData, "Programs", "jason");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "jason.exe");
        File.WriteAllText(old, "running");
        key.SetPath(folder, RegistryValueKind.ExpandString);
        var processes = new FakeProcessControl();
        // What Windows answers for the image of a running process: a rename is allowed, a delete is not.
        var places = WindowsPlaces(key.SubKey) with { Delete = file => DeleteUnlessRunning(file, old) };
        var (env, output) = Environment(places, Carried("jason.exe"));
        env = env with { Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var aside = Report(output).GetProperty("retired").GetProperty("aside").GetString()!;
        Assert.True(File.Exists(aside));
        Assert.StartsWith(Path.Combine(_tree.Paths.Root, "jason-retired-"), aside, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(old));
        Assert.Empty(processes.Launches);
        Assert.Empty(processes.Runs);
    }

    // --- the critic's rows: what must never happen ------------------------------------------------------------

    /// <summary>
    /// B1, macOS: where the wrapper cannot be written (a folder stands at ~/.local/bin/jason) or another program's file
    /// stands there, the terminal command was not placed, so a command-line install elsewhere is not retired: its executable
    /// and the notice that marks it Jason's stay, and it says why.
    /// </summary>
    [Theory]
    [InlineData("folder")]
    [InlineData("foreign")]
    public async Task On_macos_nothing_is_retired_where_the_terminal_command_was_not_placed(string blocker)
    {
        var old = PlantElsewhere("jason");
        var target = Path.Combine(Home, ".local", "bin", "jason");
        if (blocker == "folder")
        {
            Directory.CreateDirectory(target);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, "#!/bin/sh\necho somebody else's\n");
        }

        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, old), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(old), "the old executable went though no terminal command replaced it");
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(old)!, "jason-LICENSE")), "the notice that marks the old file Jason's went");
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains($"{old} was not retired", StringComparison.Ordinal));
    }

    /// <summary>
    /// B1, Windows: a Path that cannot be written leaves the command-line install, its Path entry and the application's
    /// command files as they were. The account's Path is held in memory and refuses every write, as one a policy locks
    /// refuses them, so the row writes no registry key.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task On_windows_nothing_is_retired_or_taken_back_where_the_path_could_not_be_written()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The Windows Path is Windows's.");
        var folder = Path.Combine(LocalAppData, "Programs", "jason");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "jason.exe");
        File.WriteAllText(old, "old");
        var account = new AccountPathInMemory(new StoredPath(folder, RegistryValueKind.ExpandString)) { RefusesWrites = true };
        var executable = Carried("jason.exe");
        var apps = Path.Combine(LocalAppData, "Microsoft", "WindowsApps");
        Directory.CreateDirectory(apps);
        var shim = Path.Combine(apps, "jason.cmd");
        File.WriteAllText(shim, $"@echo off\r\n\"{executable}\" %*\r\n");
        var (env, output) = Environment(WindowsPlaces(NoKey) with { AccountPath = account }, executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(old), "jason.exe went though the bundle folder never reached the Path");
        Assert.True(File.Exists(shim), "the application's command file went though nothing else answers jason");
        Assert.Equal(new StoredPath(folder, RegistryValueKind.ExpandString), account.Stored);
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Equal(0, report.GetProperty("command").GetProperty("taken_back").GetArrayLength());
        Assert.False(report.GetProperty("command").GetProperty("placed").GetBoolean(), "a Path that was never written reads as placed, and the install scripts would end saying jason is in the terminal");
    }

    /// <summary>
    /// A scratch key whose writes a row denied is gone once the row ends: the deny rule once left one key behind for every
    /// run, silently. It writes a key under the account it runs as, so it runs on a hosted runner only.
    /// </summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public void A_scratch_key_whose_writes_were_denied_is_gone_once_the_row_ends()
    {
        Assert.SkipUnless(
            OperatingSystem.IsWindows() && OnHostedRunner,
            "It writes a key under the account it runs as: it runs on a hosted runner only, never on a person's machine.");
        string subKey;
        using (var key = ScratchKey.Create())
        {
            subKey = key.SubKey;
            key.SetPath(Path.Combine(_tree.Paths.Root, "locked"), RegistryValueKind.ExpandString);
            key.DenySetValue();
        }

        using var left = Registry.CurrentUser.OpenSubKey(subKey);
        Assert.Null(left);
    }

    /// <summary>A GitHub-hosted runner, whose account is thrown away with the machine; never a person's own.</summary>
    private static bool OnHostedRunner =>
        string.Equals(System.Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.Ordinal)
        && string.Equals(System.Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT"), "github-hosted", StringComparison.Ordinal);

    /// <summary>B2: on macOS the registration is moved on disk and takes effect at the next logon: no load, which would start a runtime now (RunAtLoad), and no unload.</summary>
    [Fact]
    public async Task On_macos_the_registration_is_moved_on_disk_and_nothing_is_loaded()
    {
        var old = PlantMacCommandLineInstall();
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [old, "runtime", "run", "--detached", "--data-dir", _data.Paths.Root], null) };
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);
        env = env with { Autostart = registrar };

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        var applied = Assert.Single(registrar.Applied);
        Assert.Equal(executable, applied.Run[0]);
        Assert.Empty(applied.Apply);
        Assert.Empty(applied.Remove);
        Assert.Contains("RunAtLoad", applied.Artifact, StringComparison.Ordinal);
        Assert.Contains(Report(output).GetProperty("done").EnumerateArray(), said => said.GetString()!.Contains("next logon", StringComparison.Ordinal));
    }

    /// <summary>B3: <c>--retire</c> takes only a regular file named jason with a sign of its own that it is Jason's; anything else is named and left.</summary>
    [Theory]
    [InlineData("node", true)]
    [InlineData("jason", false)]
    public async Task Retire_leaves_a_file_that_is_not_a_jason_of_jasons(string name, bool notices)
    {
        var folder = Path.Combine(_tree.Paths.Root, "tools");
        Directory.CreateDirectory(folder);
        var named = Path.Combine(folder, name);
        File.WriteAllBytes(named, new byte[1000]);
        if (notices)
        {
            File.WriteAllText(Path.Combine(folder, "jason-LICENSE"), "MIT");
        }

        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, named), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(named), $"--retire deleted {named}");
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.StartsWith($"--retire names {named}", StringComparison.Ordinal));
    }

    /// <summary>B4: a folder install.sh refuses to spell inside double quotes takes no profile line; the hand line, in single quotes, is given.</summary>
    [Theory]
    [InlineData("ho$me")]
    [InlineData("ho`me")]
    public async Task A_folder_the_script_cannot_spell_takes_no_profile_line_and_the_hand_line_is_given(string homeName)
    {
        var home = Path.Combine(_tree.Paths.Root, homeName);
        Directory.CreateDirectory(home);
        var bin = Path.Combine(home, ".local", "bin");
        var (env, output) = Environment(MacPlaces() with { Home = home }, Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.False(File.Exists(Path.Combine(home, ".zprofile")), "a line the shell reads as code was written");
        Assert.False(File.Exists(Path.Combine(home, ".zshrc")), "a line the shell reads as code was written");
        Assert.True(File.Exists(Path.Combine(bin, "jason")));
        Assert.Contains(Report(output).GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains($"export PATH='{bin}':\"$PATH\"", StringComparison.Ordinal));
    }

    /// <summary>B5: a Path of a kind that is not a string is not overwritten with the bundle folder alone; it is refused and said.</summary>
    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task A_path_value_of_another_kind_is_refused_and_left()
    {
        using var key = ScratchKey.Create();
        key.SetMulti("Path", [@"C:\Tools", @"C:\More"]);
        var (env, output) = Environment(WindowsPlaces(key.SubKey), Carried("jason.exe"));

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal([@"C:\Tools", @"C:\More"], key.ReadMulti("Path")!);
        Assert.Contains(Report(output).GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("MultiString", StringComparison.Ordinal));
    }

    /// <summary>B6: skills a deployment wrote before it was cut off, with no record yet, equal the bundle's: they are Jason's, adopted and recorded.</summary>
    [Fact]
    public async Task Skills_an_interrupted_deployment_left_unrecorded_are_adopted_and_recorded()
    {
        PlantHosts(claude: true, codex: false);
        var executable = Carried();
        var (first, _) = Environment(MacPlaces(), executable);
        Assert.Equal(ExitCodes.Success, await InstallCommand.RunAsync(first, new InstallOptions(false, false, null), Ct));
        var root = Path.Combine(Home, ".claude", "skills");
        File.Delete(Path.Combine(root, ".jason-skills.json"));
        Assert.Null(SkillsRecord.Read(root));

        var (env, output) = Environment(MacPlaces(), executable);
        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var deployed = Assert.Single(Report(output).GetProperty("skills").EnumerateArray());
        Assert.Equal(0, deployed.GetProperty("left_alone").GetArrayLength());
        var recorded = SkillsRecord.Read(root)!.Packs.SelectMany(pack => pack.Files).Select(file => file.Path).ToList();
        Assert.Contains(recorded, path => path.StartsWith("operating/", StringComparison.Ordinal));
    }

    /// <summary>
    /// Note 7, the stop's answer read as the stop that did not finish is: a runtime that refused the shutdown took nothing,
    /// so no start is owed beside it, and it is resumed, not left draining.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_refused_the_stop_is_resumed_and_owes_no_start()
    {
        var (old, asked, _, env, output) = OldRuntimeStopping(_ => RuntimeVerbs.Response(HttpStatusCode.InternalServerError, """{"error":{"code":"internal","message":"no","retryable":false}}"""));

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(MachineSetup.RuntimeStates.WouldNotStop, Report(output).GetProperty("runtime").GetString());
        Assert.Equal(Operations.SystemResume, asked[^1]);
        Assert.True(File.Exists(old));
        Assert.False(File.Exists(_data.Paths.StartOwedFile), "a runtime that refused the stop was said to owe a start");
    }

    /// <summary>
    /// A runtime that took the shutdown and had not ended within the stop's wait is going down: the note that a start is
    /// owed stays for when it has ended, nothing is resumed, and nothing of the install is retired.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_took_the_stop_and_had_not_ended_keeps_the_note_and_is_not_resumed()
    {
        var (old, asked, _, env, output) = OldRuntimeStopping(_ => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_OLD")));

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = Report(output);
        Assert.Equal(MachineSetup.RuntimeStates.WouldNotStop, report.GetProperty("runtime").GetString());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.DoesNotContain(Operations.SystemResume, asked);
        Assert.True(File.Exists(old));
        Assert.True(StartOwed.Read(_data.Paths) is (true, { }), "a runtime going down was left with no note that a start is owed");
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("took the stop", StringComparison.Ordinal));
    }

    /// <summary>
    /// A runtime that ended while its stop said otherwise, here going down as it refused: it runs no longer, so the note that
    /// a start is owed stays, the runtime is said stopped, nothing is resumed, and the install is still held for this run.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_ended_while_its_stop_said_otherwise_keeps_the_note_and_is_not_resumed()
    {
        FakeProcessControl? control = null;
        var (old, asked, processes, env, output) = OldRuntimeStopping(_ =>
        {
            control!.RunningPids.Remove(RuntimeVerbs.Pid);
            return RuntimeVerbs.Response(HttpStatusCode.ServiceUnavailable, """{"error":{"code":"internal","message":"going","retryable":false}}""");
        });
        control = processes;

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = Report(output);
        Assert.Equal(MachineSetup.RuntimeStates.Stopped, report.GetProperty("runtime").GetString());
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.DoesNotContain(Operations.SystemResume, asked);
        Assert.True(File.Exists(old));
        Assert.True(StartOwed.Read(_data.Paths) is (true, { }), "a runtime that ended was left with no note that a start is owed");
    }

    /// <summary>
    /// A command-line install at <c>~/.local/bin/jason</c> whose runtime runs from it, drains with nothing running, resumes,
    /// and answers the shutdown with <paramref name="shutdown"/>.
    /// </summary>
    private (string Old, List<string> Asked, FakeProcessControl Processes, CliEnvironment Env, StringWriter Output) OldRuntimeStopping(Func<HttpRequestMessage, HttpResponseMessage> shutdown, SetupPlaces? places = null)
    {
        var windows = places?.Platform is SetupSystem.Windows;
        var old = windows ? PlantWindowsCommandLineInstall(places!) : PlantMacCommandLineInstall();
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD") with { InstalledImage = old });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var asked = new List<string>();
        var handler = new FakeHandler(request =>
        {
            var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
            asked.Add(operation);
            return operation switch
            {
                Operations.SystemDrain => RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(new DrainResponse(DispatcherState.Draining, 0), JasonJson.Options)),
                Operations.SystemResume => RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(new DrainResponse(DispatcherState.Running, 0), JasonJson.Options)),
                Operations.SystemShutdown => shutdown(request),
                _ => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_OLD")),
            };
        });
        var (env, output) = Environment(places ?? MacPlaces(), Carried(windows ? "jason.exe" : "jason"));
        return (old, asked, processes, env with { HttpHandler = handler, Processes = processes }, output);
    }

    /// <summary>
    /// A stop that did not finish holds the install. Cancelled while the drain waits for a running attempt, as a Ctrl+C or
    /// an agent's timeout does: the old executable is not written over, nothing is retired, the runtime is said to run, the
    /// drain is taken off, and no start is said to be owed beside a runtime that answers.
    /// </summary>
    [Fact]
    public async Task A_cancel_while_the_drain_waits_holds_the_install()
    {
        using var cancel = new CancellationTokenSource();
        var drained = false;
        var (old, asked, env, output) = OldRuntimeAnswering(operation =>
        {
            drained |= operation == Operations.SystemDrain;
            if (drained && operation == Operations.SystemInfo)
            {
                cancel.Cancel();
            }

            return null;
        });

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), cancel.Token);

        AssertHeld(old, asked, output);
        Assert.DoesNotContain(Operations.SystemShutdown, asked);
    }

    /// <summary>A stop that did not finish holds the install: the drain's call fails with something no step expected.</summary>
    [Fact]
    public async Task A_drain_that_fails_holds_the_install()
    {
        var (old, asked, env, output) = OldRuntimeAnswering(operation =>
            operation == Operations.SystemDrain ? throw new InvalidOperationException("the drain failed") : null);

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        AssertHeld(old, asked, output);
        Assert.DoesNotContain(Operations.SystemShutdown, asked);
    }

    /// <summary>A stop that did not finish holds the install: the stop's call fails with something no step expected.</summary>
    [Fact]
    public async Task A_stop_that_fails_holds_the_install()
    {
        var (old, asked, env, output) = OldRuntimeAnswering(operation =>
            operation == Operations.SystemShutdown ? throw new InvalidOperationException("the stop failed")
            : operation == Operations.SystemInfo ? RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_OLD"))
            : null);

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), Ct);

        AssertHeld(old, asked, output, owed: true);
        Assert.Contains(Operations.SystemShutdown, asked);
        Assert.Contains(Report(output).GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("whether the runtime took it is not known", StringComparison.Ordinal));
    }

    /// <summary>
    /// A cancel inside the shutdown's send, between the runtime receiving it and its answer being read: whether the runtime
    /// took it is not known, so the note that a start is owed stays, inert beside a runtime that answers and taken back by a
    /// runtime's own start or a deliberate <c>jason runtime stop</c>; the resume is still tried, and the report says the
    /// stop's fate is not known.
    /// </summary>
    [Fact]
    public async Task A_cancel_inside_the_stops_send_keeps_the_note_and_says_the_stops_fate_is_unknown()
    {
        using var cancel = new CancellationTokenSource();
        var (old, asked, env, output) = OldRuntimeAnswering(operation =>
        {
            if (operation == Operations.SystemShutdown)
            {
                cancel.Cancel();
                throw new OperationCanceledException(cancel.Token);
            }

            return operation == Operations.SystemInfo ? RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson("rt_OLD")) : null;
        });

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, StopTimeout: RuntimeVerbs.ShortTimeout, StopPoll: RuntimeVerbs.Poll), cancel.Token);

        AssertHeld(old, asked, output, owed: true);
        Assert.Contains(Operations.SystemShutdown, asked);
        Assert.Contains(Report(output).GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("whether the runtime took it is not known", StringComparison.Ordinal));
    }

    /// <summary>
    /// A command-line install at <c>~/.local/bin/jason</c> whose runtime runs from it, answering <paramref name="answer"/> where
    /// that gives an answer: otherwise a drain that drains, one attempt still running, a resume that resumes and a stop that
    /// is taken.
    /// </summary>
    private (string Old, List<string> Asked, CliEnvironment Env, StringWriter Output) OldRuntimeAnswering(Func<string, HttpResponseMessage?> answer)
    {
        var old = PlantMacCommandLineInstall();
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD") with { InstalledImage = old });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var oneRunning = System.Text.Json.Nodes.JsonNode.Parse(RuntimeVerbs.InfoJson("rt_OLD"))!;
        oneRunning["dispatcher"]!["running_attempts"] = 1;
        var asked = new List<string>();
        var handler = new FakeHandler(request =>
        {
            var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
            asked.Add(operation);
            return answer(operation) ?? operation switch
            {
                Operations.SystemDrain => RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(new DrainResponse(DispatcherState.Draining, 1), JasonJson.Options)),
                Operations.SystemResume => RuntimeVerbs.Response(HttpStatusCode.OK, JsonSerializer.Serialize(new DrainResponse(DispatcherState.Running, 1), JasonJson.Options)),
                Operations.SystemShutdown => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_OLD")),
                _ => RuntimeVerbs.Response(HttpStatusCode.OK, oneRunning.ToJsonString()),
            };
        });
        var (env, output) = Environment(MacPlaces(), Carried());
        return (old, asked, env with { HttpHandler = handler, Processes = processes }, output);
    }

    /// <summary>
    /// What a held install is: its executable as it was, nothing retired, the runtime said, the drain taken off, and the note
    /// that a start is owed standing only where a shutdown was sent and its answer never came (<paramref name="owed"/>).
    /// </summary>
    private void AssertHeld(string old, List<string> asked, StringWriter output, bool owed = false)
    {
        Assert.Equal(new byte[200_000], File.ReadAllBytes(old));
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.NotEqual(MachineSetup.RuntimeStates.NotRunning, report.GetProperty("runtime").GetString());
        Assert.Contains(Operations.SystemDrain, asked);
        Assert.Equal(Operations.SystemResume, asked[^1]);
        Assert.True(owed == File.Exists(_data.Paths.StartOwedFile), owed ? "a stop whose fate is not known took back the note that a start is owed" : "a runtime that took no stop was said to owe a start");
    }

    /// <summary>Note 7: a runtime whose descriptor names no image may run from the file, so the file is not retired, and the report says why.</summary>
    [Fact]
    public async Task A_runtime_that_names_no_image_keeps_the_file_it_may_run_from()
    {
        var old = PlantElsewhere("jason");
        Directory.CreateDirectory(_data.Paths.Root);
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_ANON") with { InstalledImage = null });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { HttpHandler = RuntimeVerbs.NeverCalled(), Processes = processes };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, old), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.True(File.Exists(old), "the file a runtime may run from was retired under it");
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("does not name the executable it runs from", StringComparison.Ordinal));
    }

    /// <summary>Note 7: a registration that named no data directory is registered again naming none.</summary>
    [Fact]
    public async Task A_registration_without_a_data_directory_keeps_having_none()
    {
        var old = PlantMacCommandLineInstall();
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [old, "runtime", "run", "--detached"], null) };
        var executable = Carried();
        var (env, _) = Environment(MacPlaces(), executable);
        env = env with { Autostart = registrar };

        await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        var applied = Assert.Single(registrar.Applied);
        Assert.Equal([executable, "runtime", "run", "--detached"], applied.Run);
    }

    /// <summary>Note 8: where retiring would stop a runtime or move a registration, a data directory of a newer layout refuses that part, with or without --roles.</summary>
    [Fact]
    public async Task A_newer_layout_keeps_its_runtime_and_registration_and_the_install_is_not_retired()
    {
        var old = PlantMacCommandLineInstall();
        Directory.CreateDirectory(_data.Paths.Root);
        File.WriteAllText(Path.Combine(_data.Paths.Root, "layout.json"), """{"layout":2,"written_by":"0.43.0","at":"2026-11-20T10:00:00Z"}""");
        _data.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD") with { InstalledImage = old });
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var registrar = new RecordingRegistrar(AutostartPlatform.MacOs) { State = new AutostartState(true, [old, "runtime", "run", "--detached", "--data-dir", _data.Paths.Root], null) };
        var (env, output) = Environment(MacPlaces(), Carried());
        // The stamp is read when the paths are made, as at a real start.
        env = env with { Paths = new Jason.Contracts.Discovery.JasonPaths(_data.Paths.Root), HttpHandler = RuntimeVerbs.NeverCalled(), Processes = processes, Autostart = registrar };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(registrar.Applied);
        Assert.True(File.Exists(old), "the command-line install was retired over a newer layout");
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("layout_newer", StringComparison.Ordinal));
    }

    /// <summary>Note 9: a failure no step expected (here the home cannot be read for agent hosts) still ends in the one document, the failure named.</summary>
    [Fact]
    public async Task An_unexpected_input_output_failure_still_prints_the_one_report()
    {
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { Harnesses = new UnreadableHome() };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = Report(output);
        Assert.False(report.GetProperty("completed").GetBoolean());
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains("the disk went away", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(Home, ".local", "bin", "jason")), "the steps before the failure did not run");
    }

    /// <summary>
    /// One report always: a failure of any kind a step did not expect, an argument or a JSON one as much as an input/output
    /// one, still ends in the one document with <c>completed</c> false, the failure named and the steps before it run.
    /// </summary>
    [Theory]
    [InlineData("argument")]
    [InlineData("json")]
    public async Task A_failure_of_any_kind_still_prints_the_one_report(string kind)
    {
        var (env, output) = Environment(MacPlaces(), Carried());
        env = env with { Harnesses = new FailingHome(kind == "json" ? new JsonException("a record that is not JSON") : new ArgumentException("a name that is not a path")) };

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        var report = Report(output);
        Assert.False(report.GetProperty("completed").GetBoolean());
        Assert.Contains(report.GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains(kind == "json" ? "a record that is not JSON" : "a name that is not a path", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(Home, ".local", "bin", "jason")), "the steps before the failure did not run");
    }

    /// <summary>
    /// Where the command-line install sat at ~/.local/bin/jason, the wrapper has taken its place even when the profile lines
    /// could not be written: the report says so, and never that the old jason still answers.
    /// </summary>
    [Fact]
    public async Task Where_the_wrapper_took_the_old_place_the_report_says_so_and_not_that_the_old_jason_answers()
    {
        var old = PlantMacCommandLineInstall();
        // A folder where the login profile would be: the lines cannot be appended, so the command is not placed whole.
        Directory.CreateDirectory(Path.Combine(Home, ".zprofile"));
        var executable = Carried();
        var (env, output) = Environment(MacPlaces(), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, TerminalPath: "/usr/bin:/bin"), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(old));
        var report = Report(output);
        Assert.Equal(JsonValueKind.Null, report.GetProperty("retired").ValueKind);
        var problems = report.GetProperty("problems").EnumerateArray().Select(said => said.GetString()!).ToList();
        Assert.DoesNotContain(problems, said => said.Contains("still answers", StringComparison.Ordinal));
        Assert.Contains(problems, said => said.Contains($"The wrapper that runs {executable} has taken the place of the command-line install's executable at {old}", StringComparison.Ordinal));
    }

    /// <summary>
    /// A colon in the folder's name joins the refused set: it splits PATH's entries, so the line would never resolve. No
    /// profile is written, the old install is not retired, and the report says why. A folder name with a colon is a POSIX
    /// one; Windows cannot make it.
    /// </summary>
    [Fact]
    public async Task A_folder_with_a_colon_takes_no_profile_line()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A folder whose name holds a colon cannot be made on Windows.");
        var home = Path.Combine(_tree.Paths.Root, "ho:me");
        Directory.CreateDirectory(home);
        var bin = Path.Combine(home, ".local", "bin");
        var (env, output) = Environment(MacPlaces() with { Home = home }, Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, TerminalPath: "/usr/bin:/bin"), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.False(File.Exists(Path.Combine(home, ".zprofile")), "a line PATH splits in two was written");
        Assert.False(File.Exists(Path.Combine(home, ".zshrc")), "a line PATH splits in two was written");
        Assert.Contains(Report(output).GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains($"{bin} holds ':'", StringComparison.Ordinal));
    }

    private sealed class UnreadableHome : IHarnessLocator
    {
        public IReadOnlyList<HarnessRoot> Detect() => throw new IOException("the disk went away");
    }

    private sealed class FailingHome(Exception failure) : IHarnessLocator
    {
        public IReadOnlyList<HarnessRoot> Detect() => throw failure;
    }

    /// <summary>Note 10: on macOS 0.48's jason-desktop wrapper of the application's is taken back, as the Windows command files are; another program's stays.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task On_macos_the_applications_jason_desktop_wrapper_is_taken_back(bool ours)
    {
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        var desktop = Path.Combine(bin, "jason-desktop");
        File.WriteAllText(desktop, ours ? CommandFiles.Wrapper("/Applications/Jason.app/Contents/Resources/bundle/jason", null) : "#!/bin/sh\necho mine\n");
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(!ours, File.Exists(desktop));
        Assert.Equal(ours ? 1 : 0, Report(output).GetProperty("command").GetProperty("taken_back").GetArrayLength());
    }

    // --- Linux: the AppImage's copy, and the deb's /usr/bin/jason ---------------------------------------------

    /// <summary>
    /// From an AppImage's copy of the jason it carries, under the data directory, the routine does what it does on macOS:
    /// the wrapper at ~/.local/bin/jason and install.sh's two lines, here bash's (the login profile and ~/.bashrc), each
    /// profile it brings into being recorded in the application's own folder under ~/.local/share, so the uninstall takes
    /// away only a file Jason made.
    /// </summary>
    [Fact]
    public async Task On_linux_an_appimage_s_copy_gets_the_wrapper_and_the_install_scripts_lines()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "A Linux row: its terminal reads files by their mode, which CI's Linux leg runs.");
        var executable = Carried(under: Path.Combine(_tree.Paths.Root, "data", "desktop", "bundle", "0.34.0"), linux: "appimage");
        var (env, output) = Environment(LinuxPlaces(), executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, TerminalPath: "/usr/bin:/bin"), Ct);

        Assert.Equal(ExitCodes.Success, exit);
        var wrapper = Path.Combine(Home, ".local", "bin", "jason");
        Assert.Equal(CommandFiles.Wrapper(executable, null), File.ReadAllText(wrapper));
        var line = $"export PATH=\"{Path.Combine(Home, ".local", "bin")}:$PATH\"";
        foreach (var profile in new[] { ".profile", ".bashrc" })
        {
            Assert.Equal($"\n# added by jason install\n{line}\n", File.ReadAllText(Path.Combine(Home, profile)));
        }

        Assert.Equal(
            [Path.Combine(Home, ".bashrc"), Path.Combine(Home, ".profile")],
            CreatedProfiles.Read(LinuxPlaces()).Order(StringComparer.Ordinal));
        Assert.Equal(Path.Combine(Home, ".local", "share", "ai.jasonharness.desktop", "created-profiles.json"), CreatedProfiles.RecordFile(LinuxPlaces()));
        var command = Report(output).GetProperty("command");
        Assert.Equal(wrapper, command.GetProperty("runs").GetString());
        Assert.True(command.GetProperty("placed").GetBoolean());
        Assert.True(command.GetProperty("finds_this").GetBoolean());
    }

    /// <summary>
    /// From the deb, whose package links /usr/bin/jason to the jason it carries for every account, the routine writes no
    /// wrapper and no line: it reads that link back as the terminal command. What would answer before it in a new
    /// terminal is taken away: the wrapper the 0.48 and 0.49.0 application wrote at ~/.local/bin/jason (or as
    /// jason-desktop), which Ubuntu's profile puts first on the PATH, and a command-line install there, retired with its
    /// notices. Another program's file there is left, and named as what a new terminal finds first.
    /// </summary>
    [Theory]
    [InlineData("nothing")]
    [InlineData("the application's wrapper")]
    [InlineData("a command-line install")]
    [InlineData("another program")]
    public async Task On_linux_the_deb_s_usr_bin_jason_is_the_terminal_command_and_what_answers_before_it_goes(string before)
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "The deb's /usr/bin/jason is a link and its files are executable by their mode: a Linux row, which CI's Linux leg runs.");
        var executable = Carried(under: Path.Combine(_tree.Paths.Root, "opt", "Jason", "resources"), linux: "deb");
        var system = Path.Combine(_tree.Paths.Root, "usr", "bin");
        Directory.CreateDirectory(system);
        File.CreateSymbolicLink(Path.Combine(system, "jason"), executable);
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        var local = Path.Combine(bin, "jason");
        switch (before)
        {
            case "the application's wrapper":
                File.WriteAllText(local, CommandFiles.Wrapper(executable, null));
                File.WriteAllText(Path.Combine(bin, "jason-desktop"), CommandFiles.Wrapper(executable, null));
                break;
            case "a command-line install":
                PlantMacCommandLineInstall();
                break;
            case "another program":
                File.WriteAllText(local, "#!/bin/sh\necho mine\n");
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(local, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                }

                break;
        }

        var places = LinuxPlaces() with { SystemCommand = Path.Combine(system, "jason") };
        var (env, output) = Environment(places, executable);

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null, TerminalPath: $"{bin}:{system}:/bin"), Ct);

        var report = Report(output);
        var command = report.GetProperty("command");
        Assert.Equal(Path.Combine(system, "jason"), command.GetProperty("runs").GetString());
        Assert.True(command.GetProperty("placed").GetBoolean());
        Assert.False(command.GetProperty("changed").GetBoolean());
        Assert.Equal(0, command.GetProperty("profiles").GetArrayLength());
        Assert.False(File.Exists(Path.Combine(Home, ".profile")) || File.Exists(Path.Combine(Home, ".bashrc")), "the deb's route wrote a profile line");
        if (before == "another program")
        {
            Assert.Equal(ExitCodes.ApiError, exit);
            Assert.True(File.Exists(local));
            Assert.Equal(local, command.GetProperty("finds").GetString());
            Assert.False(command.GetProperty("finds_this").GetBoolean());
            return;
        }

        Assert.Equal(ExitCodes.Success, exit);
        Assert.True(command.GetProperty("finds_this").GetBoolean());
        Assert.Equal(Path.Combine(system, "jason"), command.GetProperty("finds").GetString());
        Assert.False(File.Exists(local), $"{local} would answer before /usr/bin/jason");
        Assert.False(File.Exists(Path.Combine(bin, "jason-desktop")));
        Assert.False(File.Exists(Path.Combine(bin, "jason-LICENSE")));
        Assert.Equal(before == "the application's wrapper" ? 2 : 0, command.GetProperty("taken_back").GetArrayLength());
        Assert.Equal(before == "a command-line install" ? JsonValueKind.Object : JsonValueKind.Null, report.GetProperty("retired").ValueKind);
    }

    /// <summary>Note 11: a profile that is a link to a regular file is appended through; a link to anything else is refused with the hand line.</summary>
    [Fact]
    public async Task A_profile_link_is_followed_only_to_a_regular_file()
    {
        var dotfiles = Path.Combine(_tree.Paths.Root, "dotfiles");
        Directory.CreateDirectory(dotfiles);
        var real = Path.Combine(dotfiles, "zshrc");
        File.WriteAllText(real, "# mine\n");
        var zshrc = Path.Combine(Home, ".zshrc");
        var zprofile = Path.Combine(Home, ".zprofile");
        try
        {
            File.CreateSymbolicLink(zshrc, real);
            Directory.CreateSymbolicLink(zprofile, dotfiles);
        }
        catch (Exception unsupported) when (unsupported is IOException or UnauthorizedAccessException)
        {
            Assert.Skip("This account cannot make symbolic links.");
        }

        var bin = Path.Combine(Home, ".local", "bin");
        var (env, output) = Environment(MacPlaces(), Carried());

        var exit = await InstallCommand.RunAsync(env, new InstallOptions(false, false, null), Ct);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.NotNull(new FileInfo(zshrc).LinkTarget);
        Assert.Equal($"# mine\n\n# added by jason install\nexport PATH=\"{bin}:$PATH\"\n", File.ReadAllText(real));
        Assert.NotNull(new DirectoryInfo(zprofile).LinkTarget);
        Assert.Contains(Report(output).GetProperty("problems").EnumerateArray(), said => said.GetString()!.Contains(zprofile, StringComparison.Ordinal) && said.GetString()!.Contains($"export PATH=\"{bin}:$PATH\"", StringComparison.Ordinal));
    }

    // --- the verb itself -----------------------------------------------------------------------------------

    [Fact]
    public async Task Its_help_states_the_contract_its_callers_are_written_against()
    {
        var output = new StringWriter();
        using var paths = new TempPaths();

        await CliApp.RunAsync(["install", "--help"], new CliEnvironment(output, new StringWriter(), paths.Paths), Ct);

        var help = output.ToString();
        foreach (var phrase in new[] { "--roles", "--retire", "install_not_carried", "install_elevated", "taken_back, finds (the", "finds_this (whether", "open_shell", "left_alone", "would_not_stop", "Exit codes: 0", "no flags", "# added by jason install", "WindowsApps", "--terminal-path", "the PATH a terminal has", "the empty value", "holds ':'", "the wrapper has taken its executable's place", "the old job stays loaded and idle", "placed (whether a new terminal", "run/start-owed.json", "{\"stopped_at\":\"<ISO-8601 UTC>\"}" })
        {
            Assert.Contains(phrase, help, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The wrapper is the application's own shape, so its Command Line item reads what this writes as its own: the marker,
    /// the lines and the shapes are held to <c>desktop/src/command-line.ts</c>.
    /// </summary>
    [Fact]
    public void The_command_files_are_the_applications_own_shapes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "desktop", "src", "command-line.ts")).Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains($"export const wrapperMarker = '{CommandFiles.WrapperMarker}';", source, StringComparison.Ordinal);
        Assert.Contains("return ['#!/bin/sh', wrapperMarker, `${environment}exec ${shellQuote(executable)} \"$@\"`, ''].join('\\n');", source, StringComparison.Ordinal);
        Assert.Contains("const environment = dataDirectory === null ? '' : `JASON_DATA_DIR=${shellQuote(dataDirectory)} `;", source, StringComparison.Ordinal);
        Assert.Contains("const shellQuote = (value: string) => `'${value.replaceAll(\"'\", `'\\\\''`)}'`;", source, StringComparison.Ordinal);
        Assert.Contains("return ['@echo off', ...environment, `\"${executable}\" %*`, ''].join('\\r\\n');", source, StringComparison.Ordinal);
        Assert.Equal(("/A/it's/jason", (string?)"/d"), CommandFiles.WrapperTarget(CommandFiles.Wrapper("/A/it's/jason", "/d")));
        Assert.Equal(@"C:\J\jason.exe", CommandFiles.ShimTarget("@echo off\r\nsetlocal\r\nset \"JASON_DATA_DIR=C:\\d\"\r\n\"C:\\J\\jason.exe\" %*\r\n"));
    }

    // --- the fixture ---------------------------------------------------------------------------------------

    /// <summary>A carried folder of the test's own: the executable, its version.json, and the skills beside it.</summary>
    private string Carried(string name = "jason", string? under = null, string? linux = null)
    {
        // Linux: the deb's /opt/Jason/resources/bundle, beside package-type saying deb; an AppImage's copy, the folder itself.
        var bundle = linux switch
        {
            "deb" => Path.Combine(under!, "bundle"),
            "appimage" => under!,
            _ => Path.Combine(under ?? _tree.Paths.Root, "Jason.app", "Contents", "Resources", "bundle"),
        };
        Directory.CreateDirectory(bundle);
        if (linux == "deb")
        {
            File.WriteAllText(Path.Combine(under!, "package-type"), "deb\n");
        }

        var executable = Path.GetFullPath(Path.Combine(bundle, name));
        File.WriteAllText(executable, "jason 0.34.0");
        if (linux is not null && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherExecute);
        }

        CarriedRecord.Beside(executable);
        foreach (var (file, text) in SkillsPackage.Files("v0.34.0").Where(entry => entry.Name.StartsWith("skills/", StringComparison.Ordinal)))
        {
            var path = Path.Combine(bundle, file.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        return executable;
    }

    private static void DeleteUnlessRunning(string file, string running)
    {
        if (string.Equals(file, running, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("held by a running process");
        }

        File.Delete(file);
    }

    /// <summary>A command-line install outside ~/.local/bin, with the notice that marks it Jason's.</summary>
    private string PlantElsewhere(string name)
    {
        var folder = Path.Combine(_tree.Paths.Root, "opt", "jason");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, name);
        File.WriteAllBytes(old, new byte[200_000]);
        File.WriteAllText(Path.Combine(folder, "jason-LICENSE"), "MIT");
        return old;
    }

    /// <summary>A command-line install of an earlier release in its Windows installer's place, which is Jason's by that place.</summary>
    private static string PlantWindowsCommandLineInstall(SetupPlaces places)
    {
        var old = places.DefaultCommandLineExecutable;
        Directory.CreateDirectory(Path.GetDirectoryName(old)!);
        File.WriteAllBytes(old, new byte[200_000]);
        return old;
    }

    private string PlantMacCommandLineInstall()
    {
        var bin = Path.Combine(Home, ".local", "bin");
        Directory.CreateDirectory(bin);
        var old = Path.Combine(bin, "jason");
        File.WriteAllBytes(old, new byte[200_000]);
        File.WriteAllText(Path.Combine(bin, "jason-LICENSE"), "MIT");
        return old;
    }

    private void PlantHosts(bool claude, bool codex)
    {
        if (claude)
        {
            Directory.CreateDirectory(Path.Combine(Home, ".claude"));
        }

        if (codex)
        {
            Directory.CreateDirectory(Path.Combine(Home, ".codex"));
        }
    }

    private SetupPlaces MacPlaces() => new(SetupSystem.MacOs, Home, null, UserPathValue.EnvironmentKey, "/bin/zsh") { Temporary = _tree.Paths.Root };

    /// <summary>Linux places with bash, whose /usr/bin/jason, the deb's, is a path no row names unless it plants one.</summary>
    private SetupPlaces LinuxPlaces() => new(SetupSystem.Linux, Home, null, UserPathValue.EnvironmentKey, "/bin/bash")
    {
        Temporary = _tree.Paths.Root,
        SystemCommand = Path.Combine(_tree.Paths.Root, "no-usr-bin", "jason"),
    };

    /// <summary>A key no row makes: places given an account Path of their own in memory name it, and nothing opens it.</summary>
    private const string NoKey = @"Software\Jason.Tests\never-opened";

    private SetupPlaces WindowsPlaces(string pathKey) => new(SetupSystem.Windows, Home, LocalAppData, pathKey, null) { Temporary = _tree.Paths.Root };

    private (CliEnvironment Env, StringWriter Output) Environment(SetupPlaces? places, string executable, Dictionary<string, string>? variables = null)
    {
        var output = new StringWriter();
        var env = new CliEnvironment(
            output,
            new StringWriter(),
            _data.Paths,
            HttpHandler: RuntimeVerbs.NeverCalled(),
            Processes: new FakeProcessControl(),
            InstallPath: executable,
            Autostart: new RecordingRegistrar(places?.Platform switch { SetupSystem.Windows => AutostartPlatform.Windows, SetupSystem.Linux => AutostartPlatform.Linux, _ => AutostartPlatform.MacOs }),
            Harnesses: HarnessLocators.ForHome(Home),
            Variables: variables ?? new Dictionary<string, string>(),
            SearchPath: "/usr/bin:/bin",
            Setup: places);
        return (env, output);
    }

    private static JsonElement Report(StringWriter output) => JsonDocument.Parse(output.ToString()).RootElement.Clone();

    private static string Snapshot(string root) =>
        string.Join('\n', Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetRelativePath(root, file)} {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)))} {File.GetLastWriteTimeUtc(file).Ticks}"));

    /// <summary>A key of one test's own under <c>HKCU\Software\jason-tests</c>, never <c>HKCU\Environment</c>.</summary>
    [SupportedOSPlatform("windows")]
    private sealed class ScratchKey : IDisposable
    {
        private const string Parent = @"Software\jason-tests";

        private ScratchKey(string subKey) => SubKey = subKey;

        public string SubKey { get; }

        public static ScratchKey Create()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "An account's Path is a registry value only on Windows.");
            var subKey = $@"{Parent}\{Guid.NewGuid():N}";
            Registry.CurrentUser.CreateSubKey(subKey).Dispose();
            return new ScratchKey(subKey);
        }

        public void SetPath(string raw, RegistryValueKind kind)
        {
            using var key = Registry.CurrentUser.CreateSubKey(SubKey);
            key.SetValue("Path", raw, kind);
        }

        public void SetMulti(string name, string[] values)
        {
            using var key = Registry.CurrentUser.CreateSubKey(SubKey);
            key.SetValue(name, values, RegistryValueKind.MultiString);
        }

        public string[]? ReadMulti(string name)
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey);
            return key?.GetValue(name) as string[];
        }

        /// <summary>This account may no longer set a value on the key, as on a Path a policy locks; it may still delete the key.</summary>
        public void DenySetValue()
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true)!;
            var security = key.GetAccessControl();
            security.AddAccessRule(new System.Security.AccessControl.RegistryAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.RegistryRights.SetValue,
                System.Security.AccessControl.AccessControlType.Deny));
            key.SetAccessControl(security);
        }

        public (string Raw, RegistryValueKind Kind)? ReadPath()
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey);
            return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string raw
                ? (raw, key.GetValueKind("Path"))
                : null;
        }

        /// <summary>
        /// Takes the key off, and fails the row where it is still there. A deny rule a row set (<see cref="DenySetValue"/>)
        /// refused the writable open <see cref="RegistryKey.DeleteSubKeyTree(string, bool)"/> makes first, and with
        /// <c>throwOnMissingSubKey</c> false that refusal returned silently and left the key behind: the account's explicit
        /// deny rules on it come off first, and the key is read back gone, so a leak is never silent again.
        /// </summary>
        public void Dispose()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(
                SubKey,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                System.Security.AccessControl.RegistryRights.ReadPermissions | System.Security.AccessControl.RegistryRights.ChangePermissions))
            {
                if (key is not null)
                {
                    var security = key.GetAccessControl(System.Security.AccessControl.AccessControlSections.Access);
                    var denied = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(System.Security.Principal.SecurityIdentifier))
                        .OfType<System.Security.AccessControl.RegistryAccessRule>()
                        .Where(rule => rule.AccessControlType == System.Security.AccessControl.AccessControlType.Deny)
                        .ToList();
                    if (denied.Count > 0)
                    {
                        denied.ForEach(security.RemoveAccessRuleSpecific);
                        key.SetAccessControl(security);
                    }
                }
            }

            Registry.CurrentUser.DeleteSubKeyTree(SubKey, throwOnMissingSubKey: false);
            using (var left = Registry.CurrentUser.OpenSubKey(SubKey))
            {
                Assert.True(left is null, $@"HKCU\{SubKey} is still there after the row: a scratch key is never left behind under this account.");
            }

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
