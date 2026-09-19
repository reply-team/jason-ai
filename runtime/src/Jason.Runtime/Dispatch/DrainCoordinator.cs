using Jason.Contracts.Api;
using Jason.Runtime.Execution;
using Microsoft.Extensions.Hosting;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The two operations that stop this dispatcher taking new work and start it again. They exist for an update: a
/// runtime about to be replaced should finish what it is holding and claim nothing new, and an update abandoned
/// halfway should leave a runtime that carries on as though nothing had happened.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here is written down. A drain belongs to one update on one machine, and a runtime that comes back
/// after a restart comes back running — the update it was drained for is over by then, one way or the other,
/// and a runtime that silently refused to claim anything because of a file nobody remembers writing is the
/// worst outcome available.
/// </para>
/// <para>
/// A shutdown is not a drain, and this is where the difference is kept: a runtime that is stopping passes
/// through the same <see cref="DispatcherState.Draining"/> on its way out, but its loop has been cancelled and
/// nothing comes after it. Resuming such a runtime would put a lie in <c>system.info</c> — a state that says
/// Running with no loop to run — so it is refused while the application is stopping.
/// </para>
/// </remarks>
public sealed class DrainCoordinator(DispatcherStatus status, RunningAttemptRegistry running, IHostApplicationLifetime lifetime)
{
    /// <summary>Stop claiming. Idempotent, and an answer rather than an error where there is nothing to stop.</summary>
    public DrainResponse Drain()
    {
        if (status.State == DispatcherState.Running)
        {
            status.State = DispatcherState.Draining;
        }

        return Answer();
    }

    /// <summary>Claim again. Idempotent; refused only for a runtime that is on its way out.</summary>
    public DrainResponse Resume()
    {
        if (status.State == DispatcherState.Draining && !lifetime.ApplicationStopping.IsCancellationRequested)
        {
            status.State = DispatcherState.Running;
        }

        return Answer();
    }

    /// <summary>
    /// The state and the number the caller is really asking about: how many attempts are still running. An
    /// applier polls this until it reaches zero or its own bound runs out.
    /// </summary>
    private DrainResponse Answer() => new(status.State, running.Count);
}
