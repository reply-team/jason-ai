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

    /// <summary>
    /// A run caused this work and nothing about that run's execution profile can be read, so there is no
    /// profile to inherit. It blocks here rather than quietly using the global default, which would change the
    /// executor behind somebody's back. Name a profile on the item, or give the campaign or the role a policy.
    /// </summary>
    public const string LineageResolutionUnsupported = "lineage_resolution_unsupported";

    /// <summary>The profile named — by the item, by a policy, or as the global default — does not exist.</summary>
    public const string ProfileNotFound = "profile_not_found";

    /// <summary>The resolved profile has been taken out of service.</summary>
    public const string ProfileDisabled = "profile_disabled";

    /// <summary>The resolved profile's program is not on this machine, so no host can be started.</summary>
    public const string HostNotAvailable = "host_not_available";

    /// <summary>
    /// The role's skill is there but its name does not match the directory it lives in, which a host answers by
    /// ignoring the skill without saying so. Refused here instead, where it can be read.
    /// </summary>
    public const string RoleSkillInvalid = "role_skill_invalid";

    /// <summary>This build publishes no contract for the operation the work item names.</summary>
    public const string OperationUnknown = "operation_unknown";

    /// <summary>No provider route exists for the operation yet.</summary>
    public const string NoRoute = "no_route";

    /// <summary>The routed plugin is not in the active snapshot: nothing of that name is installed.</summary>
    public const string PluginNotLoaded = "plugin_not_loaded";

    /// <summary>The routed plugin is installed but its environment holds it back.</summary>
    public const string PluginUnavailable = "plugin_unavailable";

    /// <summary>The routed plugin does not implement the operation, or is not a kind that performs any.</summary>
    public const string PluginOperationUnsupported = "plugin_operation_unsupported";

    /// <summary>The routed plugin speaks no version of the operation's contract that this one is.</summary>
    public const string ContractIncompatible = "contract_incompatible";

    /// <summary>The route's binding does not satisfy the schema the plugin's manifest declares for it.</summary>
    public const string BindingInvalid = "binding_invalid";

    /// <summary>The operation is one a person has to approve, and nothing here can stand in for that.</summary>
    public const string ApprovalRequired = "approval_required";

    /// <summary>The operation acts on somebody and the work item names nobody.</summary>
    public const string ContactRequired = "contact_required";

    /// <summary>The person is not reachable on the channel the operation consumes.</summary>
    public const string NoChannelValue = "no_channel_value";

    /// <summary>That channel and value are on the do-not-contact register.</summary>
    public const string Suppressed = "suppressed";

    /// <summary>The composed input does not satisfy the operation's own schema.</summary>
    public const string InputInvalid = "input_invalid";

    /// <summary>The plugin answered, and the answer is not the shape the operation publishes.</summary>
    public const string ResultInvalid = "result_invalid";

    /// <summary>
    /// A runtime restart found the attempt still scheduled, before anything was launched, so it was never
    /// counted — for provider work as much as for an agent's, because `processing` is committed before a
    /// child exists and an attempt still scheduled therefore provably ran nothing.
    /// </summary>
    public const string Interrupted = "interrupted";

    /// <summary>The work item was cancelled while the attempt was live.</summary>
    public const string Cancelled = "cancelled";
}
