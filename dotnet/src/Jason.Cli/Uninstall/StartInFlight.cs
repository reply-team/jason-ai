using System.Globalization;
using Jason.Cli.Autostart;
using Jason.Cli.Commands;
using Jason.Cli.Installation;
using Jason.Cli.Process;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Uninstall;

/// <summary>What the uninstall's wait for a runtime still starting ended with, before the plan was read.</summary>
public enum StartWait
{
    /// <summary>Nothing was waited for: a runtime answered at the first look, or nothing else ran this installation's jason.</summary>
    None,

    /// <summary>A runtime was starting, and it answered: the stop that follows is of a runtime that was starting.</summary>
    Answered,

    /// <summary>What ran this installation's jason ended by itself, and no runtime ever answered.</summary>
    Ended,

    /// <summary>The bound passed with a process of this installation's jason running and no runtime answering.</summary>
    Refused,
}

/// <summary>
/// A runtime of this installation that is still starting when the uninstall looks, and the other processes that run this
/// installation's <c>jason</c>.
/// </summary>
/// <remarks>
/// <para>
/// A runtime is known to the plan by its descriptor, and a runtime publishes its descriptor only once it is up. Between the
/// start of <c>jason runtime start</c> and that moment, the starter and the runtime it launched run this installation's
/// <c>jason</c> and nothing on disk names them: the application starts one at each start, a logon starts one, and so does a
/// person. An uninstall that read only the descriptor then said that no runtime was running, removed the files those
/// processes run from, and reported that it was done while they ran on. So, before the runtime is looked for and before
/// anything is removed, the processes that run this installation's <c>jason</c> are looked for by their image, and while one
/// runs and no runtime answers, the uninstall waits, a bounded time, for the runtime to come up; the plan then reads it, and
/// the stop stops it as it stops any runtime. A dry run never waits.
/// </para>
/// <para>
/// Not on Windows (<see cref="Windows"/>), which never had the hole: there the running image cannot be deleted, and the
/// application's uninstaller removes nothing while a process runs from its folder, so the verb refuses such a process at
/// once, as it always has, and neither waits nor looks again after the stop. Elsewhere, once the runtime has stopped, what
/// still runs is given <see cref="Settle"/> to end, and one more look comes just before the executable, or the application,
/// is deleted, for a jason started in the removal's own seconds.
/// </para>
/// <para>
/// "Answers" is <see cref="DescriptorProcess.Live"/>, the reading the plan and the stop make. "Another process" is
/// <see cref="Others"/>: one that is neither this one, a runtime that answers on this data directory or an earlier demo's,
/// nor, on Windows, anything such a runtime started, at any depth; elsewhere a process's parent is not told
/// (<see cref="IInstallationRemover.ParentOf"/>), so a runtime's children are counted until its stop ends them. One such
/// process left at the bound, or still running once the runtime has stopped and its children have had their moment, is
/// <c>jason_running</c>.
/// </para>
/// </remarks>
/// <summary>
/// What the last look before a deletion found: each runtime that answers, with its data directory, and the other processes
/// of the image.
/// </summary>
internal sealed record LateProcesses(IReadOnlyList<(int Pid, string Root)> Runtimes, IReadOnlyList<int> Others)
{
    /// <summary>Whether it found anything.</summary>
    public bool Any => Runtimes.Count > 0 || Others.Count > 0;
}

internal static class StartInFlight
{
    /// <summary>How long a start in flight is waited for: <c>jason runtime start</c>'s own wait for the runtime, and as long again.</summary>
    public static readonly TimeSpan Bound = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How long the starter, and anything else of this installation's <c>jason</c>, is given to leave once a runtime that was
    /// starting answers: <c>jason runtime start</c> ends a moment after the runtime it launched has published.
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

    /// <summary>How long the other processes of this installation's <c>jason</c> are given to end once the runtime has stopped.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);

    /// <summary>The deepest a chain of processes is followed up to one this uninstall stops.</summary>
    private const int DeepestChain = 64;

    /// <summary>
    /// The file this installation's processes run: the <c>jason</c> the application carries, else the executable this
    /// uninstall runs as; null where there is none to look for, a build run from source.
    /// </summary>
    public static string? Image(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        if (CarriedExecutable.Of(env) is { } carried)
        {
            return carried.Executable;
        }

        return (env.InstallPath ?? SelfExecutable.InstalledImage) is { } installed ? Path.GetFullPath(installed) : null;
    }

    /// <summary>
    /// The processes running <paramref name="image"/>, in any session, besides this one, a runtime that answers on this data
    /// directory or on an earlier demo's (<paramref name="demo"/>), and, on Windows, every process any of those started, at any
    /// depth: a runtime's plugin hosts and an agent's <c>jason</c> calls for its work, which its stop ends. Elsewhere a
    /// process's parent is not told, and those are counted.
    /// </summary>
    public static IReadOnlyList<int> Others(CliEnvironment env, string image, IEnumerable<string> demo)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(demo);
        return Besides(env, image, demo.Select(folder => new JasonPaths(folder)).Prepend(env.Paths));
    }

    /// <summary>
    /// The processes running <paramref name="image"/>, in any session, besides this one, a runtime that answers on one of
    /// <paramref name="answering"/>, and, on Windows, every process any of those started, at any depth.
    /// </summary>
    private static IReadOnlyList<int> Besides(CliEnvironment env, string image, IEnumerable<JasonPaths> answering)
    {
        ArgumentNullException.ThrowIfNull(image);

        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        var stopped = new HashSet<int> { Environment.ProcessId };
        foreach (var paths in answering)
        {
            if (DescriptorProcess.Live(paths, processes) is { } runtime)
            {
                stopped.Add(runtime.Pid);
            }
        }

        var removes = env.Removes!;
        return [.. removes.RunningFromInAnySession(image).Where(pid => !stopped.Contains(pid) && !Descends(removes, pid, stopped)).Distinct().Order()];
    }

    /// <summary>
    /// After the stop: the processes running <paramref name="image"/> besides this one and a runtime of an earlier demo that
    /// still answers, which the demo's own step stops later. A runtime that answers on this data directory now is not the one
    /// this uninstall stopped but one started since, and is counted as any process of the image is.
    /// </summary>
    public static IReadOnlyList<int> AfterTheStop(CliEnvironment env, string image, IEnumerable<string> demo)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(demo);
        return Besides(env, image, demo.Select(folder => new JasonPaths(folder)));
    }

    /// <summary>
    /// Waits for a runtime of this installation that is still starting, or for what a runtime stopped a moment before left
    /// running. Where a runtime answers at the first look, or nothing
    /// else runs <paramref name="image"/>, it returns at once, having waited for nothing. Otherwise it says once on stderr what
    /// it waits for and how long, and looks again until a runtime answers — then gives the starter <see cref="Grace"/> to
    /// leave and returns, whatever remains — until what ran ends by itself, or until <see cref="Bound"/>, when it returns
    /// what still runs, for a refusal.
    /// </summary>
    public static async Task<(StartWait Outcome, IReadOnlyList<int> Left)> WaitAsync(CliEnvironment env, string image, IReadOnlyList<string> demo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        if (DescriptorProcess.Live(env.Paths, processes) is not null)
        {
            return (StartWait.None, []);
        }

        var others = Others(env, image, demo);
        if (others.Count == 0)
        {
            return (StartWait.None, []);
        }

        var clock = env.Clock ?? TimeProvider.System;
        env.Error.WriteLine(
            $"A runtime of {env.Paths.Root} may be starting: {Processes(others)} runs this installation's jason, at {image}, and no "
            + $"runtime answers yet; it may also be {ProviderCall("a runtime stopped a moment ago")}. Waiting up to {Seconds(Bound)} "
            + "for a runtime to answer, or for it to end, so that nothing is removed from under it.");
        var until = clock.GetUtcNow() + Bound;
        while (true)
        {
            await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
            if (DescriptorProcess.Live(env.Paths, processes) is not null)
            {
                var leaving = clock.GetUtcNow() + Grace;
                while (Others(env, image, demo).Count > 0 && clock.GetUtcNow() < leaving)
                {
                    await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
                }

                return (StartWait.Answered, []);
            }

            others = Others(env, image, demo);
            if (others.Count == 0)
            {
                return (StartWait.Ended, []);
            }

            if (clock.GetUtcNow() >= until)
            {
                return (StartWait.Refused, others);
            }
        }
    }

    /// <summary>
    /// The last look, just before the executable or the application is deleted: every process of <paramref name="image"/> but
    /// this one, each started in the removal's own seconds, since every runtime of this installation was stopped, an earlier
    /// demo's included, whose own step stopped it before; a runtime that answers on this data directory or on an earlier
    /// demo's is told apart, since it does not end by itself and is stopped instead. None on Windows, which never looks again.
    /// </summary>
    public static LateProcesses LastLook(CliEnvironment env, string image, IEnumerable<string> demo)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(demo);
        if (Windows(env))
        {
            return new LateProcesses([], []);
        }

        var running = Besides(env, image, []);
        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        var runtimes = new List<(int Pid, string Root)>();
        foreach (var paths in demo.Select(folder => new JasonPaths(folder)).Prepend(env.Paths))
        {
            if (DescriptorProcess.Live(paths, processes) is { } runtime && running.Contains(runtime.Pid) && runtimes.All(known => known.Pid != runtime.Pid))
            {
                runtimes.Add((runtime.Pid, paths.Root));
            }
        }

        return new LateProcesses(runtimes, [.. running.Where(pid => runtimes.All(known => known.Pid != pid))]);
    }

    /// <summary>
    /// What the last look found, as the problem says it: <paramref name="kept"/> names what was kept and where it is, and
    /// <paramref name="window"/> the window found open again, if any; then each runtime found, with the command that stops it
    /// by the <c>jason</c> that still stands (<paramref name="image"/>), and the other processes, which end by themselves.
    /// <paramref name="after"/> is what to do once they are gone.
    /// </summary>
    public static string LateProblem(CliEnvironment env, LateProcesses late, IReadOnlyList<int> window, string kept, string image, string after)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(late);
        ArgumentNullException.ThrowIfNull(window);

        var found = new List<string>();
        var todo = new List<string>();
        if (window.Count > 0)
        {
            found.Add($"Jason Desktop is open again ({Processes(window)})");
            todo.Add("quit Jason Desktop from the Jason menu");
        }

        foreach (var (pid, root) in late.Runtimes)
        {
            found.Add($"a runtime of {root} answers again ({Processes([pid])})");
            todo.Add(
                $"stop that runtime with `\"{image}\" runtime stop`"
                + (string.Equals(root, env.Paths.Root, StringComparison.Ordinal) ? string.Empty : $" with JASON_DATA_DIR set to {root}"));
        }

        if (late.Others.Count > 0)
        {
            found.Add($"another jason of this installation runs from it ({Processes(late.Others)})");
            todo.Add("wait until that jason ends");
        }

        var steps = string.Join("; ", todo);
        return $"{kept}: {string.Join(" and ", found)}, started since the runtime was stopped. "
            + $"{char.ToUpperInvariant(steps[0])}{steps[1..]}; then {after}.";
    }

    /// <summary>
    /// Once the runtime has stopped: looks for the other processes of <paramref name="image"/> until none runs, for up to
    /// <see cref="Settle"/>, and returns those still running then. A stopped runtime's process, and a starter, leave within
    /// moments, and a single look would have refused on them.
    /// </summary>
    public static async Task<IReadOnlyList<int>> SettleAsync(CliEnvironment env, string image, IReadOnlyList<string> demo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var clock = env.Clock ?? TimeProvider.System;
        var until = clock.GetUtcNow() + Settle;
        while (true)
        {
            var left = AfterTheStop(env, image, demo);
            if (left.Count == 0 || clock.GetUtcNow() >= until)
            {
                return left;
            }

            await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The refusal <c>jason_running</c> says, on every system and at every point it is reached: <paramref name="lead"/>, which
    /// says what was found when and what was not removed, then what such a process may be, and to wait for it, never to end
    /// it: a runtime migrating its database, a set-up or an update finishing, is not one to cut short. After the stop
    /// (<paramref name="afterTheStop"/>) the first case is a provider's call the stopped runtime left running; before it, a
    /// start in flight. On Windows, where a process's parent is told, it says first what the process is not.
    /// </summary>
    public static string JasonRunning(CliEnvironment env, string image, string lead, bool afterTheStop)
    {
        var windows = Windows(env);
        return lead
            + (windows ? $" It is neither the runtime of {env.Paths.Root}, which this would stop, nor anything that runtime started." : string.Empty)
            + (afterTheStop
                ? $" It may be {ProviderCall("the stopped runtime")}, or {StillFinishing}"
                : $" It may be {StillFinishing}, or {ProviderCall("a runtime stopped a moment ago")}")
            + ": wait until it ends, then uninstall again. It may also be a runtime of another data directory, which "
            + $"`\"{image}\" runtime stop` with JASON_DATA_DIR set to that directory stops, a `jason journal watch`, or a call "
            + "an agent host you opened yourself makes; "
            + (windows
                ? "in another session of this account, it may be a task that runs whether you are signed in or not."
                : $"{SharedComputer}.")
            + " Once none of them runs, run this again.";
    }

    /// <summary>What may hold a jason of this installation for a while yet, and must be let finish.</summary>
    private const string StillFinishing =
        "a runtime still starting or migrating its database, or a set-up or an update still finishing";

    /// <summary>
    /// A provider's call a stopped runtime left running: the stop drains for a while and cancels nothing, and a plugin host
    /// ends by itself within its operation's own time limit, the catalog's, which is why no number is said.
    /// </summary>
    private static string ProviderCall(string runtime) =>
        $"a call to a provider that {runtime} left running, which ends by itself within that operation's own time limit";

    /// <summary>
    /// The case a look that cannot tell a process's account names without claiming it: on a Mac, the one application in
    /// <c>/Applications</c> is every account's, and another account's Jason runs its <c>jason</c> by the same path.
    /// </summary>
    public const string SharedComputer =
        "on a computer more than one account uses, it may be Jason running under another account from this same application, which that account quits";

    /// <summary>
    /// Whether the installation is Windows', by the system's places where they are known, else by the logon registrar's
    /// system, else by this machine.
    /// </summary>
    internal static bool Windows(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (env.Setup is { } places)
        {
            return places.Platform is SetupSystem.Windows;
        }

        var platform = RuntimeAutostartCommands.Registrar(env).Platform;
        return platform is AutostartPlatform.Unsupported ? OperatingSystem.IsWindows() : platform is AutostartPlatform.Windows;
    }

    /// <summary>"process 9001" or "process 9001, 9004", as every refusal of this verb names processes.</summary>
    public static string Processes(IReadOnlyList<int> pids) =>
        "process " + string.Join(", ", pids.Select(id => id.ToString(CultureInfo.InvariantCulture)));

    /// <summary>A bound in words: "60 s".</summary>
    public static string Seconds(TimeSpan bound) => string.Create(CultureInfo.InvariantCulture, $"{bound.TotalSeconds:0} s");

    /// <summary>
    /// Whether <paramref name="pid"/> was started, through any number of processes, by one of <paramref name="ancestors"/>.
    /// A parent that cannot be told ends the chain, and the process is counted.
    /// </summary>
    private static bool Descends(IInstallationRemover removes, int pid, HashSet<int> ancestors)
    {
        var seen = new HashSet<int> { pid };
        var current = pid;
        for (var step = 0; step < DeepestChain; step++)
        {
            if (removes.ParentOf(current) is not { } parent || !seen.Add(parent))
            {
                return false;
            }

            if (ancestors.Contains(parent))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }
}
