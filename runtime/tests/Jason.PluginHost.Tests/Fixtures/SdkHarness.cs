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

    public SdkHarness(string packageRoot, Action<InvocationBuilder>? configure = null, TimeSpan? remaining = null)
    {
        Invocation = InvocationFactory.Create(packageRoot, "echo.run", configure: configure);
        Diagnostics = new HostDiagnostics(
            _stderr,
            GrantedSecrets(Invocation),
            Invocation.Limits.Log,
            Invocation.Plugin.Id,
            Invocation.InvocationId,
            TimeProvider.System);

        Budget = new CallBudget(Invocation.Limits.Exec.MaxCalls, Invocation.Limits.Http.MaxCalls);
        var left = remaining ?? TimeSpan.FromMilliseconds(Invocation.Limits.TimeoutMs);
        Services = new HostServices(Invocation, Diagnostics, Budget, () => left, packageRoot, CancellationToken.None);

        Engine = EngineFactory.Create(Invocation.Limits, new PackageModuleLoader(packageRoot), CancellationToken.None);
        Http = new HttpService(Services);
        Engine.SetValue("host", HostObject.Build(Engine, Services, new ExecService(Services), Http, new EnvService(Services), new LogService(Services)));
    }

    public Engine Engine { get; }

    public PluginInvocation Invocation { get; }

    public HostServices Services { get; }

    public HostDiagnostics Diagnostics { get; }

    public CallBudget Budget { get; }

    public HttpService Http { get; }

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
        _stderr.Dispose();
    }

    /// <summary>The same rule the mode applies: the values of granted variables are the host's known secrets.</summary>
    public static Redactor GrantedSecrets(PluginInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return new Redactor((invocation.Grants.Env?.Variables ?? []).Select(Environment.GetEnvironmentVariable));
    }

    public static HostDiagnostics DiagnosticsFor(TextWriter stderr, PluginInvocation invocation) =>
        new(stderr, GrantedSecrets(invocation), invocation.Limits.Log, invocation.Plugin.Id, invocation.InvocationId, TimeProvider.System);
}
