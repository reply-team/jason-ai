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

    /// <summary>
    /// The resolved profile's current revision is refused by this build's rules for its host: a word a write would have
    /// been refused today (a flag the runtime composes, an argument, a tool or a deny the host does not take), kept from
    /// an older build whose rules were wider. Decided before a child exists and never retried: the repair is a
    /// <c>profile update</c>, not another scan.
    /// </summary>
    public const string ProfileRevisionRefused = "profile_revision_refused";

    /// <summary>
    /// The resolved profile's program is not on this machine, or is a script only a shell could start, so no host can be
    /// started; or the host refuses to start here by its own check of the machine, the profile names a host this build
    /// does not register, or the host's installation could not be read.
    /// </summary>
    public const string HostNotAvailable = "host_not_available";

    /// <summary>
    /// The role's skill is there but its name does not match the directory it lives in, which a host answers by
    /// ignoring the skill without saying so. Refused here instead, where it can be read.
    /// </summary>
    public const string RoleSkillInvalid = "role_skill_invalid";

    /// <summary>
    /// The role's skill could not be read consistently: a deployment was in flight and a second read of it
    /// lost the race too.
    /// </summary>
    /// <remarks>
    /// Refused rather than run untaught, for the reason <see cref="RoleSkillInvalid"/> already gives about a
    /// skill that was configured and did not arrive — the role would do the job untaught at the price of a
    /// real launch. It is not that code: nothing about the skill is invalid. It takes two deployments landing
    /// inside one launch's copy to see this.
    /// </remarks>
    public const string RoleSkillUnreadable = "role_skill_unreadable";

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

    /// <summary>
    /// The provider refused a call for a reason outside the work — money, a reached limit, a revoked credential, a broken
    /// channel — and did nothing for it. Not a refusal of the work: at the claim, work a provider block holds is parked on
    /// it, and an attempt that answers it ends <c>blocked</c> and parks its item the same way. The code every operation
    /// declares for the class, as its documents publish it.
    /// </summary>
    public const string ProviderBlocked = "provider_blocked";

    /// <summary>The operation acts on somebody and the work item names nobody.</summary>
    public const string ContactRequired = "contact_required";

    /// <summary>The person is not reachable on the channel the operation consumes.</summary>
    public const string NoChannelValue = "no_channel_value";

    /// <summary>That channel and value are on the do-not-contact register.</summary>
    public const string Suppressed = "suppressed";

    /// <summary>
    /// The operation works by the routed plugin's pin alone, and that plugin has pinned nothing for the person: nobody at
    /// the provider is known to be them, so there is nothing to act on and nobody is looked up to find out.
    /// </summary>
    public const string ContactNotLinked = "contact_not_linked";

    /// <summary>
    /// The work names a send under <c>args.send</c> that its campaign does not hold: no work item of the campaign under that
    /// id, or one that makes no send.
    /// </summary>
    public const string SendUnknown = "send_unknown";

    /// <summary>
    /// The send the work names has not succeeded, or came back with no reference: the provider kept nothing to find it by.
    /// </summary>
    public const string SendReferenceAbsent = "send_reference_absent";

    /// <summary>
    /// The work is routed to another plugin, or under another binding, than the send it names was made with: the reference
    /// is that provider account's, so it is acted on there or not at all.
    /// </summary>
    public const string SendRouteMismatch = "send_route_mismatch";

    /// <summary>The composed input does not satisfy the operation's own schema.</summary>
    public const string InputInvalid = "input_invalid";

    /// <summary>
    /// The input carries an argument its operation's document names per channel, on a channel the routed plugin's
    /// manifest does not list it for.
    /// </summary>
    public const string ArgumentUnsupported = "argument_unsupported";

    /// <summary>
    /// The input's message is a template, on a channel the routed plugin's manifest does not list for the operation under
    /// <c>templates</c>.
    /// </summary>
    public const string TemplateNotOffered = "template_not_offered";

    /// <summary>
    /// An earlier attempt of a send lost its answer, and either the routed plugin's manifest does not list the send under
    /// <c>settles_by_key</c>, or the routed package, by its id and digest, or its binding is not the one the unsettled
    /// attempt ran under: nothing says the repeat would be settled by the work item's key, so the claim does not make
    /// it, and the work stays as ambiguous as the attempt that may have acted.
    /// </summary>
    public const string ProviderAnswerLost = "provider_answer_lost";

    /// <summary>The plugin answered, and the answer is not the shape the operation publishes.</summary>
    public const string ResultInvalid = "result_invalid";

    /// <summary>
    /// A schema's pattern did not finish matching, twice, so a value was neither accepted nor refused. It is never a
    /// verdict on the value, and what it costs follows where it happened: at the claim nothing has happened and the
    /// attempt is transient; over a plugin's answer the plugin has run and the attempt is ambiguous. The API answers a
    /// request the same way, under the same code, as one to send again.
    /// </summary>
    public const string ValidationTimedOut = "validation_timed_out";

    /// <summary>
    /// A runtime restart found the attempt still scheduled, before anything was launched, so it was never
    /// counted — for provider work as much as for an agent's, because `processing` is committed before a
    /// child exists and an attempt still scheduled therefore provably ran nothing.
    /// </summary>
    public const string Interrupted = "interrupted";

    /// <summary>The work item was cancelled while the attempt was live.</summary>
    public const string Cancelled = "cancelled";
}
