namespace Jason.Contracts.Plugins;

/// <summary>
/// What a plugin is for. A provider performs canonical operations and is the only kind a route reaches; a notification
/// plugin delivers notifications; a subscriber implements no operation, is no channel and reads no account, and only
/// handles the journal kinds it subscribes to. Any kind may subscribe; a subscriber must.
/// </summary>
public enum PluginKind
{
    Provider,
    Notification,
    Subscriber,
}

/// <summary>
/// A plugin in the active snapshot is either usable or held back by its environment — a declared executable that
/// is missing or unusable on this machine. A package problem never gets this far: it keeps the whole set out.
/// </summary>
public enum PluginStatus
{
    Valid,
    Unavailable,
}

/// <summary>What a load made of one candidate directory.</summary>
public enum CandidateStatus
{
    Valid,
    Unavailable,
    Invalid,

    /// <summary>A directory without a <c>plugin.yaml</c>: not a plugin, and never a reason to reject the set.</summary>
    Skipped,
}

/// <summary>Whether a snapshot came from the load at startup or from an explicit reload.</summary>
public enum SnapshotSource
{
    Startup,
    Reload,
}

/// <summary>
/// What the caller may conclude from a failure. The dispatcher reads the class, never the code: transient may be
/// repeated, permanent and validation are final, ambiguous means the provider may already have acted, and blocked
/// means the provider refused the call for a reason outside the work item — money, a reached limit, a revoked
/// credential, a broken channel — and did nothing for it. A blocked failure carries the block
/// (<see cref="ProviderBlockContract"/>); until the runtime parks work on it, it is final.
/// </summary>
public enum FailureClass
{
    Transient,
    Permanent,
    Validation,
    Ambiguous,
    Blocked,
}

/// <summary>The two ways one invocation can end on the protocol level.</summary>
public enum OutcomeStatus
{
    Succeeded,
    Failed,
}
