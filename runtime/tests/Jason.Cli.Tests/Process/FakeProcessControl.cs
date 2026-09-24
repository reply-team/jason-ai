using Jason.Cli.Process;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests.Process;

/// <summary>A child process that never existed: the tests decide whether it is alive and what it exited with.</summary>
public sealed class FakeProcessHandle(int id) : IProcessHandle
{
    public int Id { get; } = id;

    public bool HasExited { get; set; }

    public int ExitCode { get; set; }

    public string Said { get; set; } = string.Empty;

    public bool Disposed { get; private set; }

    public void Dispose() => Disposed = true;
}

/// <summary>
/// Stands in for the operating system: which pids are running, and what happens when the CLI launches the
/// runtime. Tests spawn nothing, so the suite never leaves a stray process behind.
/// </summary>
public sealed class FakeProcessControl : IRuntimeProcessControl
{
    /// <summary>What a launch does and hands back; the default is a child that starts and stays alive.</summary>
    public Func<JasonPaths, IProcessHandle>? OnLaunch { get; set; }

    public HashSet<int> RunningPids { get; } = [];

    /// <summary>The data directory of every launch, in order, so a test can assert both that and how many.</summary>
    public List<string> Launches { get; } = [];

    /// <summary>
    /// The executable of every launch, in order. Which program was started is the thing an applier gets wrong
    /// invisibly — the old build from the wrong path answers every health check the new one would — so it is
    /// recorded rather than assumed.
    /// </summary>
    public List<IReadOnlyList<string>> Executables { get; } = [];

    public IProcessHandle Launch(JasonPaths paths, IReadOnlyList<string> executable)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(executable);
        Launches.Add(paths.Root);
        Executables.Add(executable);
        return OnLaunch?.Invoke(paths) ?? new FakeProcessHandle(4242);
    }

    public bool IsRunning(int pid) => RunningPids.Contains(pid);
}
