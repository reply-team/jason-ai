using Jason.Contracts.Plugins;
using Jint;
using Jint.Runtime.Modules;

namespace Jason.PluginHost.Scripting;

/// <summary>
/// One engine per invocation, built to the envelope's limits. Everything here is a refusal: no string
/// compilation, no CLR, no <c>require</c>, no globals that outlive the process, and a budget on memory,
/// statements, time and recursion. This is interpreter isolation inside process isolation, not a sandbox — a
/// plugin is local code the user installed, and the honest claim is that it cannot reach the host program, not
/// that it cannot reach the machine.
/// </summary>
public static class EngineFactory
{
    private const int MaxArraySize = 1_000_000;
    private const int MaxJsonParseDepth = 64;

    public static Engine Create(InvocationLimits limits, IModuleLoader moduleLoader, CancellationToken deadline)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(moduleLoader);

        var timeout = TimeSpan.FromMilliseconds(limits.TimeoutMs);
        return new Engine(options =>
        {
            options.Strict(true);
            options.Host.StringCompilationAllowed = false;

            options.LimitMemory(limits.MemoryBytes);
            options.MaxStatements(limits.MaxStatements);
            options.LimitRecursion(limits.MaxRecursion);
            options.TimeoutInterval(timeout);
            options.CancellationToken(deadline);
            options.MaxArraySize(MaxArraySize);
            options.MaxJsonParseDepth(MaxJsonParseDepth);
            options.RegexTimeoutInterval(TimeSpan.FromSeconds(1));
            options.Constraints.PromiseTimeout = timeout;

            // Modules come from the package and nowhere else; require() does not exist at all.
            options.EnableModules(moduleLoader);
            options.Modules.RegisterRequire = false;

            // Interop stays at its defaults — no type access, no reflection — and the CLR is never allowed in.
        });
    }
}
