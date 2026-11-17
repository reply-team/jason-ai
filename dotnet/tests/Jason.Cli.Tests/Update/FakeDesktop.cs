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
    public static (FakeInstallation Installation, FakeDesktop Desktop) Carried(bool withRuntime = true, SetupSystem system = SetupSystem.MacOs)
    {
        var installation = new FakeInstallation(installedIn: system is SetupSystem.Windows ? WindowsBundle : Bundle);
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
    /// <c>HKCU\Software\jason-tests</c>, made at the first read and taken away with this Desktop.
    /// </summary>
    public string PathKey => _pathKey ??= MakePathKey();

    private string? _pathKey;

    private static string MakePathKey()
    {
        var key = $@"Software\jason-tests\{Guid.NewGuid():N}";
        if (OperatingSystem.IsWindows())
        {
            Microsoft.Win32.Registry.CurrentUser.CreateSubKey(key).Dispose();
        }

        return key;
    }

    /// <summary>What each start with <c>--update-apply</c> found: whether the carried file was there, and the record beside it.</summary>
    public List<(bool CarriedThere, string? Pid)> Seen { get; } = [];

    /// <summary>Set to have a start with <c>--update-apply</c> never end, as a start that hangs does, until it is cancelled.</summary>
    public bool Hangs { get; set; }

    public void Dispose()
    {
        if (_pathKey is { } key && OperatingSystem.IsWindows())
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
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
            return new FinishedCommand(SettleExit, string.Empty);
        }

        var pid = Path.Combine(Path.GetDirectoryName(_installation.InstallPath)!, CarriedUpdate.PidFileName);
        Seen.Add((File.Exists(_installation.InstallPath), File.Exists(pid) ? File.ReadAllText(pid) : null));
        if (Hangs)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        var arguments = command.ToList();
        var id = arguments[arguments.IndexOf("--request") + 1];
        var to = arguments[arguments.IndexOf("--to") + 1];
        var exit = (Answer ?? Install)(id, to);
        return new FinishedCommand(exit, string.Empty);
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
