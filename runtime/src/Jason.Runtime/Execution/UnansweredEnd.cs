using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Execution;

/// <summary>
/// A1. How an attempt ends when nothing ever answered for it: its lease ran out, its executor stopped saying it
/// was alive, or a restart found it still out. For agent work the runtime's own code table decides, exactly as
/// it always has — an agent runs inside this machine and a lease that ran out there is the runtime's own fact.
/// A provider operation ends at somebody else's system, and a missing answer says nothing about whether that
/// system acted, so the only honest class is ambiguous and the operation's own contract decides what follows.
/// </summary>
/// <param name="contracts">
/// Where an operation's contract is read from. It is a dependency rather than a static call because the rule has
/// to be answerable for an operation this build does not publish: <c>never</c> is part of the contract vocabulary
/// and none of the published operations uses it.
/// </param>
/// <param name="settlesByKey">
/// Whether the package an attempt ran under, as its provenance names it, lists the operation under
/// <c>settles_by_key</c>. Where the contract settles a lost answer by the work item's key, only that package's word
/// makes the end worth repeating; with no answer to ask, the end is final.
/// </param>
public sealed class UnansweredEnd(
    Func<string, OperationContract?> contracts,
    Func<AttemptProvenanceDto?, string, bool>? settlesByKey = null)
{
    /// <summary>
    /// Whether the package an attempt's provenance names is still the one the registry holds, under the same digest,
    /// and lists the operation under <c>settles_by_key</c>. A package replaced since, or gone, cannot answer for the
    /// attempt that ran under it, so it answers no.
    /// </summary>
    public static Func<AttemptProvenanceDto?, string, bool> RanUnder(PluginRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return (provenance, operation) =>
            provenance is { PluginId: { } id, PluginDigest: { } digest }
            && registry.Snapshot.Find(id) is { } package
            && string.Equals(package.Digest, digest, StringComparison.Ordinal)
            && package.Manifest.SettlesByKey(operation);
    }

    /// <summary>
    /// Whether the package the attempt ran under, as its provenance names it, settles the operation by the work item's
    /// key: the word every end of a provider attempt asks for, answered or not, so a package replaced or removed since the
    /// claim answers no wherever the attempt ends.
    /// </summary>
    /// <param name="attempt">The attempt whose provenance names the package it ran under.</param>
    /// <param name="operation">The operation the attempt performed.</param>
    public bool SettlesByKey(Attempt attempt, string operation)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return settlesByKey is not null && settlesByKey(attempt.Provenance, operation);
    }

    /// <summary>
    /// What to record about an unanswered end, or null where the runtime's own code table should decide as it
    /// always has — every agent attempt, and a provider item naming an operation nothing publishes, which no
    /// claim would have handed to a plugin in the first place.
    /// </summary>
    /// <param name="item">The item the attempt belongs to.</param>
    /// <param name="attempt">The attempt nothing answered for, whose provenance names the package it ran under.</param>
    public (FailureClass Class, bool Retriable)? Verdict(WorkItem item, Attempt attempt)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(attempt);

        if (item.Kind != WorkItemKind.ProviderOp
            || item.Operation is not { } operation
            || contracts(operation) is not { } contract)
        {
            return null;
        }

        var keyed = OutcomeContract.SettlesByKey(contract) && SettlesByKey(attempt, operation);
        return (FailureClass.Ambiguous, OutcomeContract.Retriable(FailureClass.Ambiguous, contract, keyed));
    }
}
