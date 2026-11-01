using Jason.Contracts.Discovery;

namespace Jason.Runtime.Plugins.Invocation;

/// <summary>One executable, several modes: the plugin host is this process, started again.</summary>
/// <remarks>
/// Started again is not the same as the process path. Under <c>dotnet jason.dll</c> the path is the muxer and the
/// assembly is not in it, so the path alone would start <c>dotnet plugin-host</c> and no plugin could ever run.
/// <see cref="SelfExecutable"/> is the one place that knows this, shared with the CLI, which starts a detached
/// runtime by asking the same question.
/// </remarks>
public sealed class ProcessPathLocator(string? processPath, string entryAssembly) : IPluginHostLocator
{
    public ProcessPathLocator()
        : this(Environment.ProcessPath, Environment.GetCommandLineArgs()[0])
    {
    }

    public IReadOnlyList<string> Command { get; } = SelfExecutable.Resolve(processPath, entryAssembly);
}
