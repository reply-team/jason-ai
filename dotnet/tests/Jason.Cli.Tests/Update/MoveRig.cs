using System.Text;
using System.Text.Json;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Update;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// What the move asks of the operating system, stood in for: nothing is installed, unzipped, signed or run. Every act is
/// recorded in order with the rig's own, so a row can say what came before what.
/// </summary>
public sealed class FakeMoveSystem(List<string> acts) : IMoveSystem
{
    public bool AppleSilicon { get; set; } = true;

    /// <summary>The publisher a test signs everything with unless it says otherwise: a name of the test's own, never a real one.</summary>
    public const string Publisher = "The Publisher Of This Jason";

    /// <summary>Who signed the Windows installer and a found application's jason, as the signature names it; null for one that carries no valid signature.</summary>
    public string? SignedBy { get; set; } = Publisher;

    /// <summary>Who signed the jason that runs the move, as its verified signature names it; null for one unsigned or that does not verify.</summary>
    public string? SelfSignedBy { get; set; } = Publisher;

    /// <summary>The file the jason that runs the move runs from.</summary>
    public string RunningExecutable { get; set; } = Path.Combine(Path.GetTempPath(), "the-running-jason", "jason.exe");

    /// <summary>How many times the running jason's own signature was read.</summary>
    public int OwnSignatureReads { get; private set; }

    /// <summary>What codesign says of the application, where it does not verify; null where it does.</summary>
    public string? MacRefusal { get; set; }

    /// <summary>The requirement each codesign was asked to hold an application to.</summary>
    public List<string> Requirements { get; } = [];

    /// <summary>What the setup does, given how it was started: by default it installs the application at the version <see cref="Installs"/>.</summary>
    public Func<SetupCall, Task<SetupRun>>? OnSetup { get; set; }

    /// <summary>The environment of the process the move runs in, which the setup's is made from.</summary>
    public IReadOnlyDictionary<string, string> ProcessVariables { get; set; } = new Dictionary<string, string>();

    /// <summary>The version an installer or a zip of this release carries.</summary>
    public string Installs { get; set; } = SemanticVersion.Current.ToString();

    /// <summary>Executables with a program running from them: an application's open window.</summary>
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAppleSilicon()
    {
        acts.Add("sysctl");
        return AppleSilicon;
    }

    public (string? Signer, string? Refusal) WindowsSigner(string file)
    {
        if (string.Equals(file, RunningExecutable, StringComparison.OrdinalIgnoreCase))
        {
            OwnSignatureReads++;
            return SelfSignedBy is null ? (null, "it does not carry a valid signature (WinVerifyTrust answered 0x800B0100)") : (SelfSignedBy, null);
        }

        acts.Add("signature");
        Assert.True(File.Exists(file), "the signature is read off the downloaded file");
        return SignedBy is null ? (null, "it does not carry a valid signature") : (SignedBy, null);
    }

    public Task<string?> MacSignatureRefusalAsync(string application, string requirement, CancellationToken cancellationToken)
    {
        acts.Add("codesign");
        Requirements.Add(requirement);
        return Task.FromResult(MacRefusal);
    }

    public Task<string?> UnzipAsync(string zip, string folder, CancellationToken cancellationToken)
    {
        acts.Add("unzip");
        Assert.True(File.Exists(zip));
        MoveRig.PlantApplication(Path.Combine(folder, "Jason.app"), windows: false, Installs);
        return Task.FromResult<string?>(null);
    }

    public Task<SetupRun> RunSetupAsync(string setup, string place, string marker, TimeSpan bound, CancellationToken cancellationToken)
    {
        // Started as the real seam starts it: the raw command line and the environment its two functions make.
        var commandLine = MoveSystem.SetupCommandLine(place);
        var environment = ProcessVariables.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        MoveSystem.SetupEnvironment(environment, marker);
        acts.Add("setup " + commandLine);
        Assert.True(File.Exists(setup));
        var call = new SetupCall(commandLine, SetupCall.NsisPlace(commandLine), environment.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!));
        if (OnSetup is { } setUp)
        {
            return setUp(call);
        }

        MoveRig.PlantApplication(call.Place ?? throw new InvalidOperationException("NSIS found no /D= in " + commandLine), windows: true, Installs);
        return Task.FromResult(new SetupRun(true, 0));
    }

    public bool Runs(string executable) => Running.Contains(executable);

    /// <summary>The shared applications folder: one of the test's own, never the machine's.</summary>
    public string SharedApplications { get; set; } = Path.Combine(Path.GetTempPath(), "jason-tests-no-such-folder", Guid.NewGuid().ToString("N"));

    /// <summary>Whether this account may write into the shared folder.</summary>
    public bool SharedWritable { get; set; }

    public bool MayWriteInto(string folder) => SharedWritable;

    /// <summary>Linux: whether apt-get and dpkg are on the PATH.</summary>
    public bool Apt { get; set; } = true;

    public bool HasApt() => Apt;

    /// <summary>Linux: the program names a process of this account runs, from any folder.</summary>
    public HashSet<string> RunningNamed { get; } = new(StringComparer.Ordinal);

    public bool RunsNamed(string name) => RunningNamed.Contains(name);

    /// <summary>Linux: whether user namespaces may be made.</summary>
    public bool UserNamespaces { get; set; } = true;

    public bool UserNamespacesAllowed() => UserNamespaces;

    /// <summary>Linux: whether a setuid fusermount is there.</summary>
    public bool Fuse { get; set; } = true;

    public bool FuseMounts() => Fuse;

    /// <summary>Linux: the folder the package installs the application into.</summary>
    public string? PackageInstallsTo { get; set; }

    /// <summary>Linux: what apt does with the package; by default it installs the application at the version <see cref="Installs"/>.</summary>
    public Func<string, PackageRun>? OnPackage { get; set; }

    /// <summary>Linux: the bound each install by apt was given, in order.</summary>
    public List<TimeSpan> Bounds { get; } = [];

    public Task<PackageRun> InstallPackageAsync(string package, TimeSpan bound, Action<string> progress, CancellationToken cancellationToken)
    {
        acts.Add("apt");
        Bounds.Add(bound);
        Assert.True(File.Exists(package), "apt is handed the downloaded package");
        if (OnPackage is { } install)
        {
            return Task.FromResult(install(package));
        }

        MoveRig.PlantPackage(PackageInstallsTo ?? throw new InvalidOperationException("The rig names no folder the package installs into."), Installs);
        return Task.FromResult(new PackageRun(true, 0, string.Empty));
    }

    /// <summary>
    /// Linux: an AppImage's own runtime, stood in for. The file stands for Jason Desktop of the version its text names
    /// ("Jason Desktop &lt;version&gt;"), and what each pattern reads out of it is written as the real one writes it.
    /// </summary>
    public Task<bool> ExtractAppImageAsync(string appImage, string pattern, string folder, CancellationToken cancellationToken)
    {
        const string Named = "Jason Desktop ";
        var text = File.Exists(appImage) ? File.ReadAllText(appImage) : string.Empty;
        if (!text.StartsWith(Named, StringComparison.Ordinal))
        {
            return Task.FromResult(false);
        }

        var version = text[Named.Length..].Trim();
        var root = Path.Combine(folder, "squashfs-root");
        var bundle = Path.Combine(root, "resources", "bundle");
        switch (pattern)
        {
            case "resources/bundle/version.json":
                Directory.CreateDirectory(bundle);
                File.WriteAllText(Path.Combine(bundle, CarriedExecutable.RecordName), $"{{\"version\":\"{version}\"}}");
                break;
            case "resources/bundle/*":
                Directory.CreateDirectory(bundle);
                File.WriteAllText(Path.Combine(bundle, "jason"), "jason " + version);
                File.WriteAllText(Path.Combine(bundle, CarriedExecutable.RecordName), $"{{\"version\":\"{version}\"}}");
                break;
            case "jason-desktop-app.desktop":
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "jason-desktop-app.desktop"), "[Desktop Entry]\nName=Jason\nExec=AppRun --no-sandbox %U\nTryExec=AppRun\nType=Application\nIcon=jason-desktop-app\n");
                break;
            case "usr/share/icons/hicolor/*/apps/jason-desktop-app.png":
                foreach (var size in new[] { "256x256", "512x512" })
                {
                    var apps = Directory.CreateDirectory(Path.Combine(root, "usr", "share", "icons", "hicolor", size, "apps")).FullName;
                    File.WriteAllText(Path.Combine(apps, "jason-desktop-app.png"), "icon " + size);
                }

                break;
        }

        return Task.FromResult(true);
    }
}

/// <summary>A setup as the move started it: its raw command line, the place NSIS reads from it, and its environment.</summary>
public sealed record SetupCall(string CommandLine, string? Place, IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>
    /// The install folder NSIS takes from a command line: the text after the last " /D=", to the end, only where it is not
    /// quoted (NSIS's documentation, "Installer Usage", 3.2.1); null where NSIS would install into its default instead.
    /// </summary>
    public static string? NsisPlace(string commandLine)
    {
        var line = " " + commandLine;
        var at = line.LastIndexOf(" /D=", StringComparison.Ordinal);
        if (at < 0)
        {
            return null;
        }

        var place = line[(at + 4)..];
        return place.Length == 0 || place.Contains('"', StringComparison.Ordinal) ? null : place;
    }
}

/// <summary>
/// A command-line install whose update to this version is complete, on a Windows, a macOS or a Linux machine of the test's own: the
/// installation's data directory, runtime and release (<see cref="FakeInstallation"/>), a home and a programs folder of its
/// own, the operating system stood in for (<see cref="FakeMoveSystem"/>), and the application's <c>jason install</c> stood in
/// for by what U2's routine does to a retired install.
/// </summary>
public sealed class MoveRig : IDisposable
{
    /// <param name="windows">A Windows machine, else a macOS one.</param>
    /// <param name="localData">The name of the account's local application data folder, which may hold a space, as a profile's often does.</param>
    public MoveRig(bool windows, string localData = "local", IReadOnlyDictionary<string, string>? variables = null)
        : this(windows ? SetupSystem.Windows : SetupSystem.MacOs, localData, variables)
    {
    }

    /// <param name="system">A Windows, a macOS or a Linux machine; on Linux the deb's folder and the AppImage's place are the test's own.</param>
    /// <param name="home">The name of the account's home folder, which may hold any character a file name can.</param>
    public MoveRig(SetupSystem system, string localData = "local", IReadOnlyDictionary<string, string>? variables = null, string home = "home")
    {
        _localData = localData;
        _home = home;
        Platform = system;
        var windows = system is SetupSystem.Windows;
        Installation = new FakeInstallation(SemanticVersion.Parse("0.48.2"), SemanticVersion.Current);
        System = new FakeMoveSystem(Acts) { SharedApplications = Path.Combine(Installation.Root, "Applications"), PackageInstallsTo = DebApplication };
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Temporary);
        Places = system switch
        {
            SetupSystem.Windows => new SetupPlaces(SetupSystem.Windows, Home, LocalAppData, @"Software\jason-tests\never-written", null) { Temporary = Temporary },
            SetupSystem.Linux => new SetupPlaces(SetupSystem.Linux, Home, null, @"Software\jason-tests\never-written", "/bin/bash")
            {
                Temporary = Temporary,
                DebApplication = DebApplication,
                SystemCommand = Path.Combine(Installation.Root, "usr", "bin", "jason"),
                AptSource = Path.Combine(Installation.Root, "etc", "jason-desktop.sources"),
            },
            _ => new SetupPlaces(SetupSystem.MacOs, Home, null, @"Software\jason-tests\never-written", "/bin/zsh") { Temporary = Temporary },
        };
        Registrar = new RecordingRegistrar(system switch
        {
            SetupSystem.Windows => Jason.Cli.Autostart.AutostartPlatform.Windows,
            SetupSystem.Linux => Jason.Cli.Autostart.AutostartPlatform.Linux,
            _ => Jason.Cli.Autostart.AutostartPlatform.MacOs,
        });
        Env = Installation.Env with { Setup = Places, Moves = System, Autostart = Registrar, Variables = variables ?? new Dictionary<string, string>() };
        Installation.Machine = Env;
        if (system is SetupSystem.Linux)
        {
            Installation.Release.WithFile(CommandLineMove.DebAsset, Encoding.UTF8.GetBytes("Jason Desktop " + SemanticVersion.Current));
            Installation.Release.WithFile(CommandLineMove.AppImageAsset, Encoding.UTF8.GetBytes("Jason Desktop " + SemanticVersion.Current));
        }
        else
        {
            Installation.Release.WithFile(windows ? CommandLineMove.WindowsAsset : CommandLineMove.MacAsset, Encoding.UTF8.GetBytes("Jason Desktop " + SemanticVersion.Current));
        }

        // The application's own `jason install`, run by the move as its child: what U2's routine does to a retired install,
        // stood in for; every other command is the installation's own.
        var others = Installation.Processes.OnRunUnder!;
        Installation.Processes.OnRunUnder = (paths, command, error, updateLock, cancellationToken) =>
            command.Count > 1 && command[1] == "install"
                ? Task.FromResult((Routine ?? Retire)(command, updateLock))
                : others(paths, command, error, updateLock, cancellationToken);
        Installation.Processes.OnLaunch = WrapLaunch(Installation.Processes.OnLaunch!);
    }

    public FakeInstallation Installation { get; }

    public FakeMoveSystem System { get; }

    public SetupPlaces Places { get; }

    public RecordingRegistrar Registrar { get; }

    public CliEnvironment Env { get; }

    public SetupSystem Platform { get; }

    public bool Windows => Platform is SetupSystem.Windows;

    /// <summary>Every act of the move's, the system's and the routine's, in order.</summary>
    public List<string> Acts { get; } = [];

    public string Home => Path.Combine(Installation.Root, _home);

    private readonly string _home;

    public string LocalAppData => Path.Combine(Installation.Root, _localData);

    private readonly string _localData;

    public string Temporary => Path.Combine(Installation.Root, "tmp");

    /// <summary>The shared applications folder of a macOS rig, which this account may not write into unless a row says.</summary>
    public string SystemApplications => Path.Combine(Installation.Root, "Applications");

    /// <summary>The command-line install's executable, at the path the update put this version.</summary>
    public string Old => Installation.InstallPath;

    /// <summary>
    /// Where a new application goes: this account's programs folder, or on macOS its own Applications folder; on Linux the
    /// deb's folder where there is apt, else the AppImage's place.
    /// </summary>
    public string Application => Platform switch
    {
        SetupSystem.Windows => Path.Combine(LocalAppData, "Programs", "jason-desktop"),
        SetupSystem.Linux => System.Apt ? DebApplication : AppImage,
        _ => Path.Combine(Home, "Applications", "Jason.app"),
    };

    /// <summary>The jason the application carries; on Linux the deb's, or the copy of this version an AppImage's makes.</summary>
    public string Carried => Platform is SetupSystem.Linux
        ? System.Apt ? Path.Combine(DebApplication, "resources", "bundle", "jason") : AppImageCopy.CopyOf(Bundles, SemanticVersion.Current)
        : CarriedOf(Application, Windows);

    /// <summary>Linux: the folder the deb installs the application into, the test's own.</summary>
    public string DebApplication => Path.Combine(Installation.Root, "opt", "Jason");

    /// <summary>Linux: the AppImage's place in this account's home, as the install line puts it.</summary>
    public string AppImage => Path.Combine(Home, "Applications", AppImageCopy.FileName);

    /// <summary>Linux: the folder of the copies an AppImage's jason runs as, under the data directory.</summary>
    public string Bundles => AppImageCopy.BundlesUnder(Installation.Paths.Root);

    /// <summary>Linux: the deb's application of <paramref name="version"/> at <paramref name="application"/>, as apt installs it.</summary>
    public static void PlantPackage(string application, string version)
    {
        var bundle = Path.Combine(application, "resources", "bundle");
        Directory.CreateDirectory(bundle);
        File.WriteAllText(Path.Combine(application, "resources", "package-type"), "deb\n");
        File.WriteAllText(Path.Combine(application, "jason-desktop-app"), "Jason Desktop " + version);
        File.WriteAllText(Path.Combine(bundle, "jason"), "jason " + version);
        File.WriteAllText(Path.Combine(bundle, CarriedExecutable.RecordName), $"{{\"version\":\"{version}\"}}");
    }

    /// <summary>What the application's <c>jason install</c> does, where a row says; else <see cref="Retire"/>.</summary>
    public Func<IReadOnlyList<string>, string?, FinishedCommand>? Routine { get; set; }

    /// <summary>The nonce each routine was handed, and what the lock's holder file said at that moment.</summary>
    public List<(string? Handed, string? Holder)> RoutineLocks { get; } = [];

    public UpdatePaths Update => Installation.Update;

    public static string CarriedOf(string application, bool windows) =>
        windows
            ? Path.Combine(application, "resources", "bundle", "jason.exe")
            : Path.Combine(application, "Contents", "Resources", "bundle", "jason");

    /// <summary>An application of <paramref name="version"/> at <paramref name="application"/>, with the jason it carries and its record.</summary>
    public static void PlantApplication(string application, bool windows, string version)
    {
        var carried = CarriedOf(application, windows);
        Directory.CreateDirectory(Path.GetDirectoryName(carried)!);
        if (windows)
        {
            File.WriteAllText(Path.Combine(application, "Jason.exe"), "Jason Desktop " + version);
        }

        File.WriteAllText(carried, "jason " + version);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(carried)!, CarriedExecutable.RecordName), $"{{\"version\":\"{version}\"}}");
    }

    /// <summary>The update to this version complete, as the hand-over's child leaves it once its last step is done.</summary>
    public UpdateLedger Complete()
    {
        var ledger = new UpdateLedger(
            SemanticVersion.Parse("0.48.2"),
            SemanticVersion.Current,
            UpdateStep.Complete,
            FakeInstallation.Interrupted,
            Old,
            Update.StagedExecutable(SemanticVersion.Current),
            Update.PreviousExecutable);
        ledger.Write(Update.Ledger);
        Directory.CreateDirectory(Update.Previous);
        File.WriteAllText(Update.PreviousExecutable, "jason 0.48.2");
        return ledger;
    }

    /// <summary>A move over this rig, its waits short, its warm-up <paramref name="warmUp"/> or nothing.</summary>
    public CommandLineMove Move(Action<MoveRecord>? writes = null, Action? warmUp = null) => new(Env, Update, TimeProvider.System)
    {
        Poll = TimeSpan.FromMilliseconds(20),
        StartWait = TimeSpan.FromSeconds(5),
        Writes = writes,
        WarmUp = warmUp ?? (() => { }),
    };

    public MoveRecord? Record() => MoveRecord.Read(Update.Move);

    /// <summary>
    /// What U2's routine does to the install <c>--retire</c> names: a runtime running is stopped with the note that a start is
    /// owed left, and the executable renamed aside on Windows, a wrapper written over it on macOS; one report printed.
    /// </summary>
    public FinishedCommand Retire(IReadOnlyList<string> command, string? updateLock)
    {
        Acts.Add("routine");
        RoutineLocks.Add((updateLock, File.Exists(Update.LockHolder) ? File.ReadAllText(Update.LockHolder).Trim().Split(' ')[^1] : null));
        var old = command[command.ToList().IndexOf("--retire") + 1];
        var runtime = MachineSetup.RuntimeStates.NotRunning;
        if (Installation.Running)
        {
            StartOwed.Write(Installation.Paths, DateTimeOffset.UtcNow);
            Installation.RuntimeStopped();
            Acts.Add("stop");
            runtime = MachineSetup.RuntimeStates.Stopped;
        }

        string? aside = null;
        if (Windows)
        {
            aside = Path.Combine(Temporary, $"jason-retired-{Guid.NewGuid():N}", "jason.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(aside)!);
            File.Move(old, aside);
        }
        else
        {
            File.WriteAllText(old, CommandFiles.Wrapper(command[0], null));
        }

        var report = new InstallReport(
            SemanticVersion.Current.ToString(),
            command[0],
            new InstallCommandOutcome(Windows ? Path.GetDirectoryName(command[0])! : old, true, command[0], true, [], []),
            [],
            null,
            new InstallRetired(old, aside, [old], Reregistered: false),
            runtime,
            [$"Retired the command-line install at {old}."],
            [],
            []);
        return new FinishedCommand(0, JsonSerializer.Serialize(report, JasonJson.Options));
    }

    private Func<Jason.Contracts.Discovery.JasonPaths, IProcessHandle> WrapLaunch(Func<Jason.Contracts.Discovery.JasonPaths, IProcessHandle> launch) =>
        paths =>
        {
            Acts.Add("launch " + string.Join(' ', Installation.Processes.Executables[^1]));

            // A runtime's own start takes the note that a start is owed back.
            StartOwed.Remove(paths);
            return launch(paths);
        };

    public void Dispose() => Installation.Dispose();
}
