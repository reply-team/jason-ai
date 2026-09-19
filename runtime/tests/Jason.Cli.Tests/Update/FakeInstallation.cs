using System.Net;
using System.Text;
using System.Text.Json;
using Jason.Cli;
using Jason.Cli.Process;
using Jason.Cli.Tests.Process;
using Jason.Cli.Update;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// A whole installation a test can update: an install directory with an executable in it, a data directory, a
/// release to update to, and a runtime that answers as whichever version is installed.
/// </summary>
/// <remarks>
/// Nothing here starts a process or opens a socket. The runtime is the handler below: it answers
/// <c>system.info</c> as the version whose file is at the install path, which is what makes the health check a
/// real check — an applier that swapped nothing gets the old version back and fails, exactly as it would on a
/// machine.
/// </remarks>
public sealed class FakeInstallation : HttpMessageHandler
{
    private readonly TempTree _tree = new();
    private readonly FakeRelease _release;

    public FakeInstallation(SemanticVersion? from = null, SemanticVersion? to = null)
    {
        From = from ?? SemanticVersion.Current;
        To = to ?? SemanticVersion.Parse("9.9.9");
        _release = new FakeRelease(To);

        Directory.CreateDirectory(InstallDirectory);
        Directory.CreateDirectory(Paths.RunDirectory);
        File.WriteAllText(InstallPath, $"jason {From}");

        Processes = new FakeProcessControl
        {
            OnLaunch = _ =>
            {
                // What a launch does here is what it does on a machine: a runtime comes up and publishes a
                // descriptor. Which version it is, is whatever file is at the install path.
                OnStart?.Invoke();

                // What a first start of a new version does before it listens: it backs the database up and
                // migrates it. The file is real here, because a rollback is going to copy it back.
                if (Migrates && Installed() == To.ToString())
                {
                    WriteBackup(BackupStamp);
                }

                // A version that migrates and then never listens publishes no descriptor: from outside, the
                // start happened and nothing answers.
                if (!StartsButNeverServes)
                {
                    Running = true;
                    Publish();
                }

                return new FakeProcessHandle(4242);
            },
        };

        Out = new StringWriter();
        Error = new StringWriter();
        Env = new CliEnvironment(Out, Error, Paths, this, null, Processes);
    }

    public SemanticVersion From { get; }

    public SemanticVersion To { get; }

    public JasonPaths Paths => new(Path.Combine(_tree.Root, "data"));

    public UpdatePaths Update => new(Paths);

    public string InstallDirectory => Path.Combine(_tree.Root, "install");

    public string InstallPath => Path.Combine(InstallDirectory, ReleaseAssets.ExecutableName);

    public CliEnvironment Env { get; }

    public StringWriter Out { get; }

    public StringWriter Error { get; }

    public FakeProcessControl Processes { get; }

    /// <summary>Whether a runtime is answering. A stopped installation is the ordinary case for an update.</summary>
    public bool Running { get; private set; }

    /// <summary>How many attempts the runtime says are still running, until a drain takes them away.</summary>
    public int RunningAttempts { get; set; }

    /// <summary>What the dispatcher says it is doing, so a test can see the drain and the resume land.</summary>
    public DispatcherState State { get; private set; } = DispatcherState.Running;

    /// <summary>Every operation the applier asked the runtime for, in order.</summary>
    public List<string> Operations { get; } = [];

    /// <summary>
    /// What the ledger on disk said at the moment each operation arrived. The ledger is written before the step
    /// it names is taken, so this is how a test sees that promise kept: the runtime is asked to drain only after
    /// the file says "drained".
    /// </summary>
    public List<(string Operation, UpdateStep? Step)> Witnessed { get; } = [];

    /// <summary>Something to do when the applier starts a runtime, for a test about a start going wrong.</summary>
    public Action? OnStart { get; set; }

    /// <summary>
    /// A new version that migrates the database and then never answers: the case a rollback exists for, and the
    /// one where nothing can be asked of the runtime because it never listened.
    /// </summary>
    public bool StartsButNeverServes { get; set; }

    /// <summary>The newest line in the chronicle, as this runtime would report it.</summary>
    public string Chronicle { get; set; } = "jrn_01M2XVJ84TFRG54VKC291A53F2";

    /// <summary>What a restored backup contains, so a test can see that the file really came back.</summary>
    public const string BackupContent = "the database as it was before the update";

    /// <summary>A rollback over this installation.</summary>
    public UpdateRollback Rollback() => new(Env, Update, TimeProvider.System);

    /// <summary>Whatever the database is supposed to contain at this point in a test.</summary>
    public void WriteDatabase(string content)
    {
        Directory.CreateDirectory(Paths.StateDirectory);
        File.WriteAllText(Paths.DatabaseFile, content);
    }

    /// <summary>
    /// A backup as the migrator writes one: the name carries whole seconds, which is the point of the test that
    /// uses this — a backup written inside the same second as the stop still belongs to this update.
    /// </summary>
    public string WriteBackup(DateTimeOffset stamp)
    {
        Directory.CreateDirectory(Paths.BackupsDirectory);
        var name = $"jason-{stamp.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-before-20260920000000_Next.db";
        var path = Path.Combine(Paths.BackupsDirectory, name);
        File.WriteAllText(path, BackupContent);
        return path;
    }

    /// <summary>Every address the applier fetched from the feed, in order.</summary>
    public List<string> Fetched => _release.Requested;

    public Uri Feed => _release.Feed;

    /// <summary>The release this installation can update to, for tests that need its bytes or its digest.</summary>
    public FakeRelease Release => _release;

    /// <summary>Starts with a runtime already up, which is what an update usually finds.</summary>
    public FakeInstallation WithRuntime(int runningAttempts = 0)
    {
        Running = true;
        RunningAttempts = runningAttempts;
        Publish();
        return this;
    }

    /// <summary>An applier over this installation, on the system clock: the waits here are milliseconds.</summary>
    public UpdateApplier Applier() => new(Env, Update, TimeProvider.System, InstallPath);

    /// <summary>
    /// When a killed update began. A fixed moment in the past, so that a run which carries that update on can
    /// be told from a run which quietly started a new one: the second would stamp its own clock.
    /// </summary>
    public static DateTimeOffset Interrupted { get; } = new(2026, 9, 19, 7, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Puts this installation in the state a process dying at <paramref name="step"/> leaves behind: every step
    /// up to and including that one performed, and the ledger that names it on disk.
    /// </summary>
    /// <remarks>
    /// The ledger is written <em>before</em> the step it names, so "the ledger says <c>kept</c>" is what a
    /// process arriving after the kill sees whether or not the keeping finished. The effects below are the
    /// applier's own, performed here rather than mocked: what a test calls a kill point has to be a state a
    /// machine really reaches.
    /// </remarks>
    public UpdateLedger Killed(UpdateStep step)
    {
        var ledger = new UpdateLedger(
            SemanticVersion.Current,
            To,
            step,
            Interrupted,
            InstallPath,
            Update.StagedExecutable(To),
            Update.PreviousExecutable);

        if (step >= UpdateStep.Staged)
        {
            Directory.CreateDirectory(Update.StagedFor(To));
            File.WriteAllText(Update.StagedExecutable(To), $"jason {To}");
        }

        if (step >= UpdateStep.Drained && Running)
        {
            State = DispatcherState.Draining;
            RunningAttempts = 0;
        }

        if (step >= UpdateStep.Stopped)
        {
            if (Running)
            {
                Running = false;
                File.Delete(Paths.DescriptorFile);
            }

            ledger = ledger with { StoppedAt = Interrupted };
        }

        if (step >= UpdateStep.Kept)
        {
            Directory.CreateDirectory(Update.Previous);
            Directory.CreateDirectory(Update.Applier);
            File.Copy(InstallPath, Path.Combine(Update.Applier, ReleaseAssets.ExecutableName), overwrite: true);
            File.Move(InstallPath, Update.PreviousExecutable, overwrite: true);
        }

        if (step >= UpdateStep.Swapped)
        {
            File.Move(Update.StagedExecutable(To), InstallPath);
        }

        if (step >= UpdateStep.Started)
        {
            Running = true;
            Publish();
        }

        ledger.Write(Update.Ledger);
        return ledger;
    }

    /// <summary>The version the file at the install path claims to be, which is what a runtime here reports.</summary>
    public string Installed() => File.Exists(InstallPath)
        ? File.ReadAllText(InstallPath).Replace("jason ", string.Empty, StringComparison.Ordinal).Trim()
        : "nothing";

    public UpdateLedger? Ledger() => UpdateLedger.ReadFile(Update.Ledger);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        if (url.Port == 5000)
        {
            return Task.FromResult(Answer(url.AbsolutePath));
        }

        // Anything else is the release feed, which has its own rules about what it will serve.
        return Task.FromResult(_release.Answer(request));
    }

    private HttpResponseMessage Answer(string path)
    {
        var operation = path[(path.LastIndexOf('/') + 1)..];
        Operations.Add(operation);
        Witnessed.Add((operation, Ledger()?.Step));

        if (!Running)
        {
            throw new HttpRequestException("connection refused");
        }

        switch (operation)
        {
            case "system.drain":
                State = DispatcherState.Draining;

                // A drain is what ends the work in flight here, as the runtime's own enforcer would.
                RunningAttempts = 0;
                return Json(new DrainResponse(State, RunningAttempts));

            case "system.resume":
                State = DispatcherState.Running;
                return Json(new DrainResponse(State, RunningAttempts));

            case "system.shutdown":
                Running = false;
                File.Delete(Paths.DescriptorFile);
                return Json(new ShutdownResponse("rt_FAKE", 77, Stopping: true));

            case "system.info":
                return Json(Info());

            case "journal.list":
                return Json(new Page<JournalEntryDto>(
                    [new JournalEntryDto(Chronicle, DateTimeOffset.UnixEpoch, new ActorRef(ActorType.System), "work_item_created", null, null, null, null, null, null, null)],
                    null));

            default:
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
        }
    }

    private SystemInfoResponse Info() => new(
        Installed(),
        "v1",
        "rt_FAKE",
        77,
        DateTimeOffset.UnixEpoch,
        Paths.Root,
        new DatabaseInfo(["20260913225419_InitialCreate"], MigratedOnStart(), BackupOnStart()),
        new DispatcherInfo(State, 10, 4, RunningAttempts, null, 1, 0),
        new PluginsInfo(0, "snp_EMPTY", DateTimeOffset.UnixEpoch, true),
        new RoutesInfo("rts_EMPTY", DateTimeOffset.UnixEpoch, null, 0, 0),
        null);

    /// <summary>What the new version's first start applied, when this installation is set up to have migrated.</summary>
    public IReadOnlyList<string> MigratedOnStart() => Migrates && Installed() == To.ToString() ? ["20260920000000_Next"] : [];

    /// <summary>The moment in the backup's name, fixed so a test can name the file it expects.</summary>
    public static DateTimeOffset BackupStamp { get; } = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private string? BackupOnStart() => MigratedOnStart().Count == 0
        ? null
        : Path.Combine(Paths.BackupsDirectory, $"jason-{BackupStamp.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-before-20260920000000_Next.db");

    /// <summary>Whether the new version's first start migrates the database, as a real new version may.</summary>
    public bool Migrates { get; set; }

    private HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body, JasonJson.Options), Encoding.UTF8, "application/json"),
    };

    private void Publish()
    {
        Directory.CreateDirectory(Paths.RunDirectory);
        File.WriteAllText(
            Paths.DescriptorFile,
            JsonSerializer.Serialize(
                new RuntimeDescriptor("v1", Installed(), "rt_FAKE", 77, "http://127.0.0.1:5000", "the-token", DateTimeOffset.UnixEpoch),
                JasonJson.Options));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _release.Dispose();
            _tree.Dispose();
        }

        base.Dispose(disposing);
    }
}
