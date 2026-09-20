using Jason.Contracts.Api;
using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;
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
/// Running with no loop to run, and a scan that claims work nothing is left to run — so it is refused once the
/// process is leaving.
/// </para>
/// <para>
/// "Leaving" is asked of two things, because one of them answers too late. <c>system.shutdown</c> writes its
/// acknowledgement before it asks the host to stop, so for a fifth of a second afterwards
/// <see cref="IHostApplicationLifetime.ApplicationStopping"/> has not been cancelled and a resume inside that
/// window would be allowed by the token alone; the coordinator that answered knows immediately, and is asked
/// first. The token still has its own case: a console interrupt, or a host stopping for a reason nobody asked
/// for over the API.
/// </para>
/// </remarks>
public sealed class DrainCoordinator(
    DispatcherStatus status,
    RunningAttemptRegistry running,
    IHostApplicationLifetime lifetime,
    ShutdownCoordinator shutdown)
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
        if (status.State == DispatcherState.Draining && !Leaving)
        {
            status.State = DispatcherState.Running;
        }

        return Answer();
    }

    /// <summary>Whether this process is on its way out, by either of the two things that can say so.</summary>
    private bool Leaving => shutdown.Requested || lifetime.ApplicationStopping.IsCancellationRequested;

    /// <summary>
    /// The state and the number the caller is really asking about: how many attempts are still running. An
    /// applier polls this until it reaches zero or its own bound runs out.
    /// </summary>
    private DrainResponse Answer() => new(status.State, running.Count);
}
