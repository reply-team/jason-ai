using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Persistence;

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
public sealed class UnansweredEnd(Func<string, OperationContract?> contracts)
{
    /// <summary>
    /// What to record about an unanswered end, or null where the runtime's own code table should decide as it
    /// always has — every agent attempt, and a provider item naming an operation nothing publishes, which no
    /// claim would have handed to a plugin in the first place.
    /// </summary>
    public (FailureClass Class, bool Retriable)? Verdict(WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Kind != WorkItemKind.ProviderOp
            || item.Operation is not { } operation
            || contracts(operation) is not { } contract)
        {
            return null;
        }

        return (FailureClass.Ambiguous, OutcomeContract.Retriable(FailureClass.Ambiguous, contract));
    }
}
