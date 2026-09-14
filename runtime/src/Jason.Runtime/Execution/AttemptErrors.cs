namespace Jason.Runtime.Execution;

/// <summary>
/// Every attempt-error code the runtime itself writes, spelled once. Codes an executor reports are its own
/// vocabulary; the runtime only decides whether they are worth another attempt.
/// </summary>
public static class AttemptErrors
{
    /// <summary>The attempt's whole time budget ran out.</summary>
    public const string LeaseExpired = "lease_expired";

    /// <summary>The executor stopped saying it was alive.</summary>
    public const string HeartbeatMissed = "heartbeat_missed";

    /// <summary>The child process ended without completing the attempt through the API.</summary>
    public const string ExecutorExited = "executor_exited";

    /// <summary>The command could not be started at all.</summary>
    public const string ExecutorLaunchFailed = "executor_launch_failed";

    /// <summary>The role has no entry command and none is configured for every role.</summary>
    public const string RoleNotLaunchable = "role_not_launchable";

    /// <summary>No provider route exists for the operation yet.</summary>
    public const string NoRoute = "no_route";

    /// <summary>A runtime restart found the attempt still scheduled; it was never counted.</summary>
    public const string Interrupted = "interrupted";

    /// <summary>The work item was cancelled while the attempt was live.</summary>
    public const string Cancelled = "cancelled";
}
