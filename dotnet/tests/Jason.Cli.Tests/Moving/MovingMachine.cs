using System.Net;
using System.Text.Json;
using Jason.Cli.Doctor;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Commands;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Harness;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Moving;

/// <summary>
/// One machine an installation is moved from or to, made up under the temporary directory: a data directory, a harness
/// root, a home and the folder the command is typed in, each its own, and a runtime that exists only as a process table
/// and a handler. Nothing here reads or writes the real home, starts a process or registers anything at logon.
/// </summary>
internal sealed class MovingMachine : IDisposable
{
    public const string Version = "0.33.0";

    private readonly Lock _sync = new();
    private int _instances;

    /// <param name="dataFolder">Where the data directory is under the machine's folder: <c>box/data</c> gives a folder,
    /// <c>box</c>, that holds the data directory and not the home.</param>
    public MovingMachine(string dataFolder = "data")
    {
        Root = Path.Combine(Path.GetTempPath(), "jason-cli-tests", Guid.NewGuid().ToString("N"));
        Data = Path.Combine([Root, .. dataFolder.Split('/')]);
        Harness = Path.Combine(Root, "harness");
        Home = Path.Combine(Root, "home");
        Here = Path.Combine(Root, "here");
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(Here);
        Paths = new JasonPaths(Data);
        Processes = new FakeProcessControl { OnLaunch = Launch };
        Handler = new StandIn(this);
    }

    public string Root { get; }

    public string Data { get; }

    public string Harness { get; }

    public string Home { get; }

    /// <summary>The folder the command is typed in.</summary>
    public string Here { get; }

    public JasonPaths Paths { get; }

    public FakeProcessControl Processes { get; }

    public RecordingRegistrar Registrar { get; set; } = new();

    public StringWriter Out { get; private set; } = new();

    public StringWriter Error { get; private set; } = new();

    /// <summary>Whether a launch brings a runtime up; one that does not exits at once.</summary>
    public bool StartsOnLaunch { get; set; } = true;

    /// <summary>Whether a shutdown it is asked for brings the runtime down; one that does not answers and keeps running.</summary>
    public bool StopsOnShutdown { get; set; } = true;

    /// <summary>What the runtime answers an operation other than <c>system.info</c> and <c>system.shutdown</c>; null is a 404.</summary>
    public Func<string, string, HttpResponseMessage?>? Answers { get; set; }

    /// <summary>Every operation the runtime was asked, with its body, in order.</summary>
    public List<(string Operation, string Body)> Calls { get; } = [];

    /// <summary>Run as each operation is asked, before it is answered.</summary>
    public Action<string>? OnCall { get; set; }

    /// <summary>A descriptor a runtime left that has since gone: it names a process that is not running.</summary>
    public void LeaveStaleDescriptor()
    {
        Directory.CreateDirectory(Paths.RunDirectory);
        File.WriteAllText(Paths.DescriptorFile, JsonSerializer.Serialize(RuntimeVerbs.Descriptor("rt_MOVINGGONE", 6999), JasonJson.Options));
    }

    public bool Running { get; private set; }

    public int Pid { get; private set; }

    /// <summary>The variables the CLI reads; the harness root's is set unless a test says not, and the home is made up, so nothing resolves to a real home.</summary>
    public Dictionary<string, string> Variables { get; } = [];

    /// <summary>Whether the harness root's variable is set; a machine without it resolves the root as the runtime would.</summary>
    public bool RootVariable { get; set; } = true;

    private StandIn Handler { get; }

    /// <summary>The environment a verb runs in, with fresh writers.</summary>
    public CliEnvironment Env()
    {
        Out = new StringWriter();
        Error = new StringWriter();
        if (RootVariable && !Variables.ContainsKey("JASON_Harness__Root"))
        {
            Variables["JASON_Harness__Root"] = Harness;
        }

        return new CliEnvironment(
            Out,
            Error,
            Paths,
            Handler,
            null,
            Processes,
            Autostart: Registrar,
            Identity: new IdentityFacts(Home, "pat", "BENCH-3", null, null),
            Variables: Variables,
            BuildVersion: Version,
            WorkingDirectory: Here,
            AccountName: "pat");
    }

    /// <summary>A runtime running on this data directory now.</summary>
    public void StartRuntime()
    {
        lock (_sync)
        {
            _instances++;
            Pid = 7000 + _instances;
            Running = true;
            Processes.RunningPids.Add(Pid);
            Directory.CreateDirectory(Paths.RunDirectory);
            File.WriteAllText(Paths.DescriptorFile, JsonSerializer.Serialize(RuntimeVerbs.Descriptor(InstanceId, Pid), JasonJson.Options));
        }
    }

    public string InstanceId => $"rt_MOVING{_instances}";

    /// <summary>Writes a file under the data directory.</summary>
    public string PlantData(string relative, string text = "made up") => Plant(Data, relative, text);

    /// <summary>Writes a file under the harness root.</summary>
    public string PlantHarness(string relative, string text = "made up") => Plant(Harness, relative, text);

    /// <summary>A folder of the harness with its marker.</summary>
    public void Mark(string relative, string kind, string? id)
    {
        var folder = relative.Length == 0 ? Harness : Path.Combine(Harness, relative);
        var marker = HarnessMarker.PathIn(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllBytes(marker, new HarnessMarker(kind, id).Bytes());
    }

    public static string Plant(string under, string relative, string text)
    {
        var path = Path.Combine(under, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public void Dispose()
    {
        try
        {
            // A link first, by itself, so the delete never reaches through it into what it leads to.
            Unlink(Root);
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Removes every link to a directory below <paramref name="folder"/>, the link alone, never going through one.</summary>
    private static void Unlink(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var directory in new DirectoryInfo(folder).EnumerateDirectories())
        {
            if (directory.LinkTarget is not null)
            {
                directory.Delete();
            }
            else
            {
                Unlink(directory.FullName);
            }
        }
    }

    private Jason.Cli.Process.IProcessHandle Launch(JasonPaths paths)
    {
        _ = paths;
        if (!StartsOnLaunch)
        {
            return new FakeProcessHandle(4242) { HasExited = true, ExitCode = 1 };
        }

        StartRuntime();
        return new FakeProcessHandle(Pid);
    }

    /// <summary>The runtime's loopback door, standing in: it answers only while the runtime runs.</summary>
    private sealed class StandIn(MovingMachine machine) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var operation = request.RequestUri!.AbsolutePath.Split('/')[^1];
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            lock (machine._sync)
            {
                machine.Calls.Add((operation, body));
                machine.OnCall?.Invoke(operation);
                if (!machine.Running)
                {
                    throw new HttpRequestException("connection refused");
                }

                switch (operation)
                {
                    case "system.info":
                        return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson(machine.InstanceId, machine.Pid));
                    case "system.shutdown" when !machine.StopsOnShutdown:
                        return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson(machine.InstanceId, machine.Pid));
                    case "system.shutdown":
                        machine.Running = false;
                        machine.Processes.RunningPids.Remove(machine.Pid);
                        File.Delete(machine.Paths.DescriptorFile);
                        return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson(machine.InstanceId, machine.Pid));
                }
            }

            return machine.Answers?.Invoke(operation, body)
                ?? (operation == "project.link" ? RuntimeVerbs.Response(HttpStatusCode.OK, "{\"id\":\"prj_made_up\"}") : null)
                ?? RuntimeVerbs.Response(
                    HttpStatusCode.NotFound,
                    JsonSerializer.Serialize(new ErrorResponse(new ErrorBody("not_found", "No such operation here.", false)), JasonJson.Options));
        }
    }
}

/// <summary>A clock stopped at one moment.</summary>
internal sealed class StoppedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
