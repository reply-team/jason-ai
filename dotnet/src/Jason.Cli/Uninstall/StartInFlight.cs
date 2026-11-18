using System.Globalization;
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
/// processes run from, and reported that it was done while they ran on. So, before the plan is read, the processes that run
/// this installation's <c>jason</c> are looked for by their image, and while one runs and no runtime answers, the uninstall
/// waits, a bounded time, for the runtime to come up; the plan then reads it, and the stop stops it as it stops any runtime.
/// </para>
/// <para>
/// "Answers" is <see cref="DescriptorProcess.Live"/>, the reading the plan and the stop make. "Another process" is
/// <see cref="Others"/>: one that is neither this one, a runtime that answers on this data directory or an earlier demo's,
/// nor anything such a runtime started, at any depth. One such process left at the bound, or still running once the runtime
/// has stopped, is <c>jason_running</c>.
/// </para>
/// </remarks>
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
    /// directory or on an earlier demo's (<paramref name="demo"/>), and every process any of those started, at any depth: a
    /// runtime's plugin hosts and an agent's <c>jason</c> calls for its work, which its stop ends.
    /// </summary>
    public static IReadOnlyList<int> Others(CliEnvironment env, string image, IEnumerable<string> demo)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(demo);

        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        var stopped = new HashSet<int> { Environment.ProcessId };
        foreach (var paths in demo.Select(folder => new JasonPaths(folder)).Prepend(env.Paths))
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
    /// Waits for a runtime of this installation that is still starting. Where a runtime answers at the first look, or nothing
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
            + $"runtime answers yet. Waiting up to {Seconds(Bound)} for it, so that it is stopped before anything is removed.");
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
            var left = Others(env, image, demo);
            if (left.Count == 0 || clock.GetUtcNow() >= until)
            {
                return left;
            }

            await Task.Delay(Poll, clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The refusal <c>jason_running</c> says, on every system and at every point it is reached: <paramref name="lead"/>, which
    /// says what was found when and what was not removed, then what such a process may be and what to do.
    /// <paramref name="runtime"/> follows the data directory's name: ", which this would stop" before the stop, nothing after it.
    /// </summary>
    public static string JasonRunning(CliEnvironment env, string image, string lead, string runtime) =>
        lead
        + $" It is neither the runtime of {env.Paths.Root}{runtime}, nor anything that runtime started. "
        + "It may be a runtime that never came up: end it, or wait for it to end, then uninstall again. "
        + $"Or it may be a runtime of another data directory, which `\"{image}\" runtime stop` with JASON_DATA_DIR set to that "
        + "directory stops, a `jason journal watch`, or a call an agent host you opened yourself makes, which ends by itself; in "
        + "another session of this account, it may be a task that runs whether you are signed in or not. Once none runs, run this again.";

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
