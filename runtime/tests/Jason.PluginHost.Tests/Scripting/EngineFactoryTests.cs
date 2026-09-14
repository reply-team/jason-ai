using Jason.Contracts.Plugins;
using Jason.PluginHost.Scripting;
using Jason.PluginHost.Tests.Fixtures;
using Jint;
using Jint.Runtime;
using Jint.Runtime.Modules;

namespace Jason.PluginHost.Tests.Scripting;

public sealed class EngineFactoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-engine-tests", Guid.NewGuid().ToString("N"));

    public EngineFactoryTests() => InvocationFactory.WritePackage(_root, "export const x = 1;");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private Engine Create(Func<InvocationLimits, InvocationLimits>? adjust = null, CancellationToken? deadline = null)
    {
        var limits = InvocationFactory.DefaultLimits;
        return EngineFactory.Create(adjust is null ? limits : adjust(limits), new DefaultModuleLoader(_root, true), deadline ?? Ct);
    }

    [Fact]
    public void Compiling_a_string_at_runtime_is_not_possible()
    {
        var engine = Create();

        Assert.ThrowsAny<JavaScriptException>(() => engine.Evaluate("eval(\"1\")"));
        Assert.ThrowsAny<JavaScriptException>(() => engine.Evaluate("new Function(\"return 1\")"));
    }

    [Fact]
    public void A_runaway_loop_runs_out_of_statements()
    {
        var engine = Create(limits => limits with { MaxStatements = 1000 });

        Assert.Throws<StatementsCountOverflowException>(() => engine.Evaluate("while (true) { }"));
    }

    [Fact]
    public void A_slow_script_runs_out_of_time()
    {
        var engine = Create(limits => limits with { TimeoutMs = 50, MaxStatements = 1_000_000_000 });

        Assert.Throws<TimeoutException>(() => engine.Evaluate("while (true) { }"));
    }

    [Fact]
    public void Runaway_recursion_runs_out_of_depth()
    {
        var engine = Create(limits => limits with { MaxRecursion = 8 });

        Assert.Throws<RecursionDepthOverflowException>(() => engine.Evaluate("function f(n) { return f(n + 1); } f(0);"));
    }

    [Fact]
    public void A_cancelled_deadline_stops_the_engine()
    {
        using var cancelled = new CancellationTokenSource();
        var engine = Create(limits => limits with { MaxStatements = 1_000_000_000, TimeoutMs = 60_000 }, cancelled.Token);
        cancelled.CancelAfter(TimeSpan.FromMilliseconds(50));

        Assert.Throws<ExecutionCanceledException>(() => engine.Evaluate("while (true) { }"));
    }

    [Fact]
    public void There_is_no_way_to_reach_the_host_program()
    {
        var engine = Create();

        Assert.Equal("undefined", engine.Evaluate("typeof System").AsString());
        Assert.Equal("undefined", engine.Evaluate("typeof importNamespace").AsString());
        Assert.Equal("undefined", engine.Evaluate("typeof require").AsString());
        Assert.Equal("undefined", engine.Evaluate("typeof console").AsString());
        Assert.Equal("undefined", engine.Evaluate("typeof host").AsString());
    }

    [Fact]
    public void Everything_runs_in_strict_mode()
    {
        var engine = Create();

        var error = Assert.ThrowsAny<JavaScriptException>(() => engine.Evaluate("undeclared = 1;"));

        Assert.Contains("undeclared", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Modules_are_loaded_from_the_package_and_nowhere_else()
    {
        var engine = Create();

        var module = engine.Modules.Import("./main.js");

        Assert.Equal(1, module.Get("x").AsNumber());
        Assert.ThrowsAny<Exception>(() => engine.Modules.Import("./../escape.js"));
    }

    [Fact]
    public void Memory_is_bounded()
    {
        var engine = Create(limits => limits with { MemoryBytes = 2 * 1024 * 1024, MaxStatements = 1_000_000_000, TimeoutMs = 60_000 });

        Assert.Throws<MemoryLimitExceededException>(() =>
            engine.Evaluate("const a = []; for (let i = 0; i < 1000000; i++) { a.push('xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx'); }"));
    }
}
