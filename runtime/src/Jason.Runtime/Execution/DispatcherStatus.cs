using Jason.Contracts.Api;
using Jason.Runtime.Configuration;

namespace Jason.Runtime.Execution;

/// <summary>
/// What the dispatcher is doing, written by the loop and read by <c>system.info</c>. Cheap on purpose: it is how
/// an operator, a test and the end-to-end run all tell that the loop is alive.
/// </summary>
public sealed class DispatcherStatus
{
    public DispatcherState State { get; set; } = DispatcherState.Stopped;

    public DateTimeOffset? LastScanAt { get; set; }

    public long Scans { get; set; }

    /// <summary>The pool's size, fixed when the runtime starts; 0 until a pool exists.</summary>
    public int MaxParallel { get; set; }

    /// <summary>When this process started: the anchor of the heartbeat grace, so a restart does not lose work it never saw.</summary>
    public DateTime? RuntimeStartedAt { get; set; }

    public DispatcherInfo Snapshot(DispatcherOptions current, int runningAttempts)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new DispatcherInfo(
            State,
            current.TickSeconds,
            MaxParallel > 0 ? MaxParallel : current.MaxParallel,
            runningAttempts,
            LastScanAt,
            Scans);
    }
}
