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
    ProviderOpPlan? Plan = null);
