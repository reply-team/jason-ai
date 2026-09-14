namespace Jason.Contracts.Plugins;

/// <summary>What a plugin is for. Only a provider is invocable; a notification plugin reserves its kind.</summary>
public enum PluginKind
{
    Provider,
    Notification,
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
/// repeated, permanent and validation are final, and ambiguous means the provider may already have acted.
/// </summary>
public enum FailureClass
{
    Transient,
    Permanent,
    Validation,
    Ambiguous,
}

/// <summary>The two ways one invocation can end on the protocol level.</summary>
public enum OutcomeStatus
{
    Succeeded,
    Failed,
}
