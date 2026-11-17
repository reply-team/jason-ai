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

    /// <summary>Who signed the Windows installer, as its signature names it; null for one that carries no valid signature.</summary>
    public string? SignedBy { get; set; } = MoveSignatures.Publisher;

    /// <summary>What codesign says of the application, where it does not verify; null where it does.</summary>
    public string? MacRefusal { get; set; }

    /// <summary>The requirement each codesign was asked to hold an application to.</summary>
    public List<string> Requirements { get; } = [];

    /// <summary>What the setup does, given its arguments: by default it installs the application at the version <see cref="Installs"/>.</summary>
    public Func<IReadOnlyList<string>, Task<SetupRun>>? OnSetup { get; set; }

    /// <summary>The version an installer or a zip of this release carries.</summary>
    public string Installs { get; set; } = SemanticVersion.Current.ToString();

    /// <summary>Executables with a program running from them: an application's open window.</summary>
    public HashSet<string> Running { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsAppleSilicon()
    {
        acts.Add("sysctl");
        return AppleSilicon;
    }

    public string? WindowsSignatureRefusal(string file, string publisher)
    {
        acts.Add("signature");
        Assert.True(File.Exists(file), "the signature is read off the downloaded file");
        return SignedBy is null ? "it does not carry a valid signature" : SignedBy == publisher ? null : $"it is signed by '{SignedBy}', not by {publisher}";
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

    public Task<SetupRun> RunSetupAsync(string setup, IReadOnlyList<string> arguments, TimeSpan bound, CancellationToken cancellationToken)
    {
        acts.Add("setup " + string.Join(' ', arguments));
        Assert.True(File.Exists(setup));
        if (OnSetup is { } setUp)
        {
            return setUp(arguments);
        }

        var place = arguments.Single(argument => argument.StartsWith("/D=", StringComparison.Ordinal))[3..];
        MoveRig.PlantApplication(place, windows: true, Installs);
        return Task.FromResult(new SetupRun(true, 0));
    }

    public bool Runs(string executable) => Running.Contains(executable);

    /// <summary>The shared applications folder: one of the test's own, never the machine's.</summary>
    public string SharedApplications { get; set; } = Path.Combine(Path.GetTempPath(), "jason-tests-no-such-folder", Guid.NewGuid().ToString("N"));

    /// <summary>Whether this account may write into the shared folder.</summary>
    public bool SharedWritable { get; set; }

    public bool MayWriteInto(string folder) => SharedWritable;
}

/// <summary>
/// A command-line install whose update to this version is complete, on a Windows or a macOS machine of the test's own: the
/// installation's data directory, runtime and release (<see cref="FakeInstallation"/>), a home and a programs folder of its
/// own, the operating system stood in for (<see cref="FakeMoveSystem"/>), and the application's <c>jason install</c> stood in
/// for by what U2's routine does to a retired install.
/// </summary>
public sealed class MoveRig : IDisposable
{
    public MoveRig(bool windows)
    {
        Windows = windows;
        Installation = new FakeInstallation(SemanticVersion.Parse("0.48.2"), SemanticVersion.Current);
        System = new FakeMoveSystem(Acts) { SharedApplications = Path.Combine(Installation.Root, "Applications") };
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Temporary);
        Places = windows
            ? new SetupPlaces(SetupSystem.Windows, Home, LocalAppData, @"Software\jason-tests\never-written", null) { Temporary = Temporary }
            : new SetupPlaces(SetupSystem.MacOs, Home, null, @"Software\jason-tests\never-written", "/bin/zsh") { Temporary = Temporary };
        Registrar = new RecordingRegistrar(windows ? Jason.Cli.Autostart.AutostartPlatform.Windows : Jason.Cli.Autostart.AutostartPlatform.MacOs);
        Env = Installation.Env with { Setup = Places, Moves = System, Autostart = Registrar, Variables = new Dictionary<string, string>() };
        Installation.Machine = Env;
        Installation.Release.WithFile(windows ? CommandLineMove.WindowsAsset : CommandLineMove.MacAsset, Encoding.UTF8.GetBytes("Jason Desktop " + SemanticVersion.Current));

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

    public bool Windows { get; }

    /// <summary>Every act of the move's, the system's and the routine's, in order.</summary>
    public List<string> Acts { get; } = [];

    public string Home => Path.Combine(Installation.Root, "home");

    public string LocalAppData => Path.Combine(Installation.Root, "local");

    public string Temporary => Path.Combine(Installation.Root, "tmp");

    /// <summary>The shared applications folder of a macOS rig, which this account may not write into unless a row says.</summary>
    public string SystemApplications => Path.Combine(Installation.Root, "Applications");

    /// <summary>The command-line install's executable, at the path the update put this version.</summary>
    public string Old => Installation.InstallPath;

    /// <summary>Where a new application goes: this account's programs folder, or on macOS its own Applications folder.</summary>
    public string Application => Windows
        ? Path.Combine(LocalAppData, "Programs", "jason-desktop")
        : Path.Combine(Home, "Applications", "Jason.app");

    public string Carried => CarriedOf(Application, Windows);

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

    /// <summary>A move over this rig, its waits short.</summary>
    public CommandLineMove Move(Action<MoveRecord>? writes = null) => new(Env, Update, TimeProvider.System)
    {
        Poll = TimeSpan.FromMilliseconds(20),
        StartWait = TimeSpan.FromSeconds(5),
        Writes = writes,
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
