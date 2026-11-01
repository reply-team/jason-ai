namespace Jason.Runtime.Execution;

/// <summary>
/// One rule set decides what is worth another attempt — the runtime's. An executor reports what went wrong and
/// does not get a vote, because the same failure means the same thing whoever hit it.
/// </summary>
public static class FailureClassifier
{
    /// <summary>Losing the machine, losing the process, and the four transient conditions an executor can report.</summary>
    public static IReadOnlySet<string> RetriableCodes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AttemptErrors.LeaseExpired,
        AttemptErrors.HeartbeatMissed,
        AttemptErrors.ExecutorExited,
        "rate_limited",
        "provider_unavailable",
        "timeout",
        "transient",
    };

    /// <summary>Everything the set does not name is final: a wrong instruction does not get better by being repeated.</summary>
    public static bool IsRetriable(string code) => RetriableCodes.Contains(code);
}
