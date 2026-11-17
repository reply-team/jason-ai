using System.Text.Json;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Cli.Uninstall;
using Jason.Cli.Update;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// Jason Desktop around a <see cref="FakeInstallation"/> whose executable is the jason it carries, laid out as a macOS
/// application is (<c>Jason.app/Contents/Resources/bundle/</c>) or as a Windows one is (<c>Jason/resources/bundle/</c>), with
/// its executable and its <c>version.json</c>. Its starts are answered here as <c>docs/release-and-update.md</c> §7 says the
/// real one answers them: the request's file under <c>&lt;data&gt;/update/</c>, the runtime it carries stopped, the
/// application kept and replaced by the version asked for.
/// </summary>
/// <remarks>
/// Nothing here starts a process. The macOS layout is the one used on every system the suite runs on, because the machine
/// setup it implies writes only under the test's own home; the Windows layout's machine setup writes a Path, so it is given
/// a registry key of the test's own (<see cref="PathKey"/>), never <c>HKCU\Environment</c>.
/// </remarks>
public sealed class FakeDesktop : IDisposable
{
    /// <summary>Where a macOS application carries its jason, under the test's tree.</summary>
    public static readonly string Bundle = Path.Combine("Jason.app", "Contents", "Resources", "bundle");

    /// <summary>Where a Windows application carries its jason, under the test's tree.</summary>
    public static readonly string WindowsBundle = Path.Combine("Jason", "resources", "bundle");

    private readonly FakeInstallation _installation;
    private readonly SetupSystem _system;

    public FakeDesktop(FakeInstallation installation, SetupSystem system = SetupSystem.MacOs)
    {
        _installation = installation ?? throw new ArgumentNullException(nameof(installation));
        _system = system;
        Executable = CarriedUpdate.ApplicationOf(installation.InstallPath, system);
        Directory.CreateDirectory(Path.GetDirectoryName(Executable)!);
        File.WriteAllText(Executable, "Jason Desktop");
        Names(installation.From);
        Home = Path.Combine(installation.Root, "home");
        Directory.CreateDirectory(Home);
        LocalAppData = Path.Combine(Home, "AppData", "Local");
        installation.Desktop = this;
    }

    /// <summary>A carried installation with its Desktop, and the environment its verbs run in, which knows the system.</summary>
    /// <param name="to">The version the release offers; 9.9.9 when not given, the installed one for the finishing step's own tests.</param>
    public static (FakeInstallation Installation, FakeDesktop Desktop) Carried(bool withRuntime = true, SetupSystem system = SetupSystem.MacOs, SemanticVersion? to = null)
    {
        var installation = new FakeInstallation(to: to, installedIn: system is SetupSystem.Windows ? WindowsBundle : Bundle);
        if (withRuntime)
        {
            installation.WithRuntime();
        }

        var desktop = new FakeDesktop(installation, system);
        installation.Machine = installation.Env with { Setup = desktop.Places };
        return (installation, desktop);
    }

    /// <summary>The account's local application data, under the test's home: the updater's store is in it.</summary>
    public string LocalAppData { get; }

    /// <summary>
    /// The registry key the Windows machine setup writes its Path to: a key of this test's own under
    /// <c>HKCU\Software\jason-tests</c>, named here and made only by a setup that writes it, so a row that never runs the
    /// machine setup writes nothing under HKCU; taken away with this Desktop.
    /// </summary>
    public string PathKey { get; } = $@"Software\jason-tests\{Guid.NewGuid():N}";

    /// <summary>
    /// What each start with <c>--update-apply</c> found when it was told to go — the moment the installer looks: whether the
    /// carried file was there, and the record beside it.
    /// </summary>
    public List<(bool CarriedThere, string? Pid)> Seen { get; } = [];

    /// <summary>Set to have a start with <c>--update-apply</c> never end once it is told to go, until it is cancelled.</summary>
    public bool Hangs { get; set; }

    /// <summary>
    /// What a start does before the update is downloaded, as a refusal of the feed's or a start that ends with no file does:
    /// given the request's id and the version, it returns the exit and the start goes no further. Null downloads.
    /// </summary>
    public Func<string, string, int>? Before { get; set; }

    /// <summary>Set to have a start never end before it has downloaded anything, until it is cancelled.</summary>
    public bool HangsBeforeDownload { get; set; }

    /// <summary>Set to have the request taken by a running window: the start exits 3 at once, and the window does the rest.</summary>
    public bool TakenByAWindow { get; set; }

    /// <summary>Set to have a go of this request appear beside the request's file as soon as it names downloaded.</summary>
    public bool PlantsAGo { get; set; }

    /// <summary>How long the start waits for the go once it has written downloaded, as the Desktop's own wait does.</summary>
    public TimeSpan GoWait { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>The requests that waited for a go that never came, and ended at go_timed_out.</summary>
    public List<string> NoGo { get; } = [];

    /// <summary>The arguments the starts with <c>--update-apply</c> were made with, in order.</summary>
    public List<IReadOnlyList<string>> Requests => Starts.Where(start => start.Contains("--update-apply")).ToList();

    /// <summary>Set to have a start with <c>--update-settle</c> never end, until it is ended.</summary>
    public bool SettleHangs { get; set; }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(PathKey, throwOnMissingSubKey: false);
        }
    }

    /// <summary>The application's executable: <c>Jason.app/Contents/MacOS/Jason</c>.</summary>
    public string Executable { get; }

    /// <summary>The account's home, under the test's tree, where the machine setup writes.</summary>
    public string Home { get; }

    /// <summary>The places the machine setup writes into: this test's home, as the system lays them out.</summary>
    public SetupPlaces Places => _system is SetupSystem.Windows
        ? new(SetupSystem.Windows, Home, LocalAppData, PathKey, null) { Temporary = _installation.Root }
        : new(SetupSystem.MacOs, Home, null, UserPathValue.EnvironmentKey, "/bin/zsh");

    /// <summary>The application's folder, which its kept copy is put back in the place of.</summary>
    public string Folder => CarriedUpdate.ApplicationFolderOf(_installation.InstallPath, _system);

    /// <summary>Every start of the application, in order, with its arguments.</summary>
    public List<IReadOnlyList<string>> Starts { get; } = [];

    /// <summary>
    /// How a start with <c>--update-apply</c> is answered: given the request's id and the version asked for, it does what a
    /// test says and returns the exit. The default installs, as a start of its own does.
    /// </summary>
    public Func<string, string, int>? Answer { get; set; }

    /// <summary>What a start with <c>--update-settle</c> exits with.</summary>
    public int SettleExit { get; set; }

    /// <summary>Where the Desktop keeps the application it replaces.</summary>
    public string Kept => Path.Combine(_installation.Paths.Root, "desktop", "rollback", _installation.From.ToString());

    /// <summary>Whether the arguments are a start of the application.</summary>
    public bool IsStartOf(IReadOnlyList<string> command) =>
        string.Equals(Path.GetFullPath(command[0]), Path.GetFullPath(Executable), StringComparison.OrdinalIgnoreCase);

    public async Task<FinishedCommand> RunAsync(JasonPaths paths, IReadOnlyList<string> command, CancellationToken cancellationToken)
    {
        Starts.Add(command);
        if (command.Contains("--update-settle"))
        {
            if (SettleHangs)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return new FinishedCommand(SettleExit, string.Empty);
        }

        var arguments = command.ToList();
        var id = arguments[arguments.IndexOf("--request") + 1];
        var to = arguments[arguments.IndexOf("--to") + 1];
        if (TakenByAWindow)
        {
            // The window takes it at its own pace; the start that handed it on ends at once.
            Window = Task.Run(() => TakeAsync(id, to, command.Contains("--await-go"), CancellationToken.None), CancellationToken.None);
            return new FinishedCommand(3, string.Empty);
        }

        return new FinishedCommand(await TakeAsync(id, to, command.Contains("--await-go"), cancellationToken), string.Empty);
    }

    /// <summary>The window's taking of a request it was handed, while it runs.</summary>
    public Task<int>? Window { get; private set; }

    /// <summary>
    /// What the process that takes a request does: before the download what <see cref="Before"/> says; a request that waits
    /// for the go writes downloaded and waits for <c>desktop-&lt;id&gt;.go</c>, ending at go_timed_out where none comes; then, told
    /// to go, what <see cref="Answer"/> says.
    /// </summary>
    private async Task<int> TakeAsync(string id, string to, bool awaitsGo, CancellationToken cancellationToken)
    {
        if (HangsBeforeDownload)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        if (Before is { } before)
        {
            return before(id, to);
        }

        if (awaitsGo)
        {
            var go = Path.Combine(_installation.Paths.Root, "update", $"desktop-{id}.go");
            if (File.Exists(go))
            {
                Write(id, to, "refused", reason: "go_there", message: "A go for this request was there before the request was taken.");
                return 0;
            }

            Write(id, to, "downloaded", offered: to);
            if (PlantsAGo)
            {
                File.WriteAllText(go, string.Empty);
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            var waited = TimeSpan.Zero;
            while (!File.Exists(go))
            {
                if (waited >= GoWait)
                {
                    NoGo.Add(id);
                    Write(id, to, "refused", reason: "go_timed_out", message: "No go came.");
                    return 0;
                }

                await Task.Delay(10, cancellationToken);
                waited += TimeSpan.FromMilliseconds(10);
            }
        }

        var pid = Path.Combine(Path.GetDirectoryName(_installation.InstallPath)!, CarriedUpdate.PidFileName);
        Seen.Add((File.Exists(_installation.InstallPath), File.Exists(pid) ? File.ReadAllText(pid) : null));
        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return (Answer ?? Install)(id, to);
    }

    /// <summary>
    /// What a start of its own does with a request it can take: the runtime the application carries stopped, the
    /// application kept, the installer handed the update, and the application then the version asked for.
    /// </summary>
    public int Install(string id, string to)
    {
        var stopped = StopRuntime();
        Keep();
        Write(id, to, "installing", runtimeStopped: stopped, kept: Kept);
        Becomes(SemanticVersion.Parse(to));
        return 0;
    }

    /// <summary>The application copied whole to <see cref="Kept"/>, as the Desktop keeps it before the installer runs.</summary>
    public void Keep()
    {
        foreach (var file in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(Kept, Path.GetRelativePath(Folder, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: true);
        }

        Directory.CreateDirectory(Kept);
    }

    /// <summary>Stops the runtime the application carries, where it runs, as the Desktop does through <c>jason runtime stop</c>.</summary>
    public bool StopRuntime()
    {
        if (!_installation.Running)
        {
            return false;
        }

        _installation.RuntimeStopped();
        return true;
    }

    /// <summary>The application replaced by <paramref name="version"/>: its jason and its record.</summary>
    public void Becomes(SemanticVersion version)
    {
        File.WriteAllText(_installation.InstallPath, $"jason {version}");
        Names(version);
    }

    /// <summary>The application's <c>version.json</c> naming <paramref name="version"/>, as the preparation writes it.</summary>
    public void Names(SemanticVersion version) =>
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(_installation.InstallPath)!, CarriedExecutable.RecordName), $"{{\n  \"version\": \"{version}\"\n}}\n");

    /// <summary>A request's file, every member there, as the Desktop writes it.</summary>
    public void Write(string id, string? to, string state, string? reason = null, string? message = null, string? offered = null, bool window = false, bool runtimeStopped = false, string? kept = null)
    {
        var file = Path.Combine(_installation.Paths.Root, "update", $"desktop-{id}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["request"] = id,
                ["state"] = state,
                ["from"] = _installation.From.ToString(),
                ["to"] = to,
                ["reason"] = reason,
                ["message"] = message,
                ["offered"] = offered,
                ["window"] = window,
                ["runtime_stopped"] = runtimeStopped,
                ["kept"] = kept,
                ["updated_at"] = "2026-11-17T12:00:00.000Z",
            },
            JasonJson.Options));
    }
}
