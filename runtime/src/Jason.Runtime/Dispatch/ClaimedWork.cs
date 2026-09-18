using Jason.Runtime.Routing;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// One claim handed from the scan to the handler pool. Both the internal keys and the public ids travel: the
/// handler reloads by key in its own scope, and everything it says about the work uses the public ids.
/// </summary>
/// <param name="Plan">
/// For a provider operation, everything the claim resolved — the package, the route and the composed input —
/// carried in memory rather than looked up again, so a reload between the claim and the run cannot change what
/// was already decided. Null for agent work, which resolves its command at claim the same way.
/// </param>
public sealed record ClaimedWork(
    int WorkItemId,
    int AttemptId,
    string WorkItemPublicId,
    string AttemptPublicId,
    ProviderOpPlan? Plan = null,

    /// <summary>
    /// What an agent attempt needs beyond its command line: the deny rules and the callback word the launcher
    /// writes into the per-attempt work directory. Null for provider work and for a role run by its own entry
    /// command, neither of which has a profile to say it.
    /// </summary>
    AgentLaunch? Agent = null);

/// <summary>The per-item half of an agent launch, as the claim resolved it.</summary>
public sealed record AgentLaunch(IReadOnlyList<string> Deny, string CliCommand);
