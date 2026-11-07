using System.Diagnostics;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Scripting;
using Jason.PluginHost.Sdk;
using Jint;
using Jint.Native;

namespace Jason.PluginHost.Tests.Fixtures;

/// <summary>
/// A live engine with the <c>host</c> object on it, over an envelope the test shapes — the same wiring the
/// runner does, so an SDK test exercises the function exactly as a plugin reaches it.
/// </summary>
public sealed class SdkHarness : IDisposable
{
    private readonly StringWriter _stderr = new();

    public SdkHarness(
        string packageRoot,
        Action<InvocationBuilder>? configure = null,
        TimeSpan? remaining = null,
        Func<Process, TimeSpan, bool>? endedWithin = null,
        Action<Process>? kill = null,
        Action<TimeSpan, TimeSpan>? spending = null,
        TimeSpan? sharingWait = null)
    {
        Invocation = InvocationFactory.Create(packageRoot, "echo.run", configure: configure);
        Diagnostics = new HostDiagnostics(
            _stderr,
            GrantedSecrets(Invocation),
            Invocation.Limits.Log,
            Invocation.Plugin.Id,
            Invocation.InvocationId,
            TimeProvider.System);

        Budget = new CallBudget(Invocation.Limits.Exec.MaxCalls, Invocation.Limits.Http.MaxCalls, Invocation.Door?.Limits.MaxCalls ?? 0, Invocation.Limits.Files.MaxCalls);
        var left = remaining ?? TimeSpan.FromMilliseconds(Invocation.Limits.TimeoutMs);
        Kills = new UnfinishedKills();
        Services = new HostServices(Invocation, Diagnostics, Budget, Kills, () => left, packageRoot, CancellationToken.None);

        Engine = EngineFactory.Create(Invocation.Limits, new PackageModuleLoader(packageRoot), CancellationToken.None);
        Http = new HttpService(Services);
        Door = Invocation.Door is null ? null : new DoorService(Services, Invocation.Door);
        var defaults = new ExecService(Services);
        var exec = new ExecService(Services) { EndedWithin = endedWithin ?? defaults.EndedWithin, Kill = kill ?? defaults.Kill, Spending = spending };
        var files = sharingWait is { } wait ? new FilesService(Services) { SharingWait = wait } : new FilesService(Services);
        Engine.SetValue("host", HostObject.Build(Engine, Services, exec, Http, new EnvService(Services), new ConfigService(Services), new LogService(Services), files, Door));
    }

    public Engine Engine { get; }

    public PluginInvocation Invocation { get; }

    public HostServices Services { get; }

    public HostDiagnostics Diagnostics { get; }

    public CallBudget Budget { get; }

    /// <summary>The kills a call stopped waiting for, as the host would be left to wait for them.</summary>
    public UnfinishedKills Kills { get; }

    public HttpService Http { get; }

    /// <summary>The door's client, where the envelope carries a door.</summary>
    public DoorService? Door { get; }

    /// <summary>Every JSON Lines record the host has written so far, in order.</summary>
    public IReadOnlyList<JsonObject> Lines =>
    [
        .. _stderr.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => (JsonObject)JsonNode.Parse(line)!)
    ];

    public string Stderr => _stderr.ToString();

    public JsValue Evaluate(string expression) => Engine.Evaluate(expression);

    /// <summary>Runs an expression and answers with the name of whatever it threw, or the value it produced.</summary>
    public string Caught(string expression) =>
        Engine.Evaluate($"(() => {{ try {{ {expression}; return 'no-throw'; }} catch (e) {{ return e.name; }} }})()").AsString();

    /// <summary>The same, answering with what the plugin author would read rather than the kind of the throw.</summary>
    public string Message(string expression) =>
        Engine.Evaluate($"(() => {{ try {{ {expression}; return 'no-throw'; }} catch (e) {{ return String(e.message); }} }})()").AsString();

    public void Dispose()
    {
        Http.Dispose();
        Door?.Dispose();
        _stderr.Dispose();
    }

    /// <summary>The rule the mode applies, the mode's own: the values of granted variables and a door's capability.</summary>
    public static Redactor GrantedSecrets(PluginInvocation invocation) => PluginHostMode.KnownSecrets(invocation);

    public static HostDiagnostics DiagnosticsFor(TextWriter stderr, PluginInvocation invocation) =>
        new(stderr, GrantedSecrets(invocation), invocation.Limits.Log, invocation.Plugin.Id, invocation.InvocationId, TimeProvider.System);
}
