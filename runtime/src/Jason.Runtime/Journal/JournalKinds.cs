using System.Text.RegularExpressions;

namespace Jason.Runtime.Journal;

/// <summary>
/// The kinds the runtime writes itself. Callers append their own vocabulary through <c>journal.append</c> —
/// a role's <c>plan_revision</c> or <c>observation</c> — but never one of these, so a chronicle line that claims
/// the runtime changed something really was the runtime.
/// </summary>
public static partial class JournalKinds
{
    public const string CampaignCreated = "campaign_created";
    public const string CampaignUpdated = "campaign_updated";
    public const string CampaignStarted = "campaign_started";
    public const string CampaignPaused = "campaign_paused";
    public const string CampaignArchived = "campaign_archived";
    public const string ContextUpdated = "context_updated";
    public const string ContactsAdded = "contacts_added";
    public const string ContactsRemoved = "contacts_removed";
    public const string ContactCreated = "contact_created";
    public const string ContactUpdated = "contact_updated";
    public const string ContactArchived = "contact_archived";
    public const string SuppressionAdded = "suppression_added";
    public const string SuppressionRemoved = "suppression_removed";
    public const string WorkItemCreated = "workitem_created";
    public const string WorkItemUpdated = "workitem_updated";

    /// <summary>Distinct from <see cref="ContextUpdated"/> so a campaign-context reader never filters item entries out.</summary>
    public const string WorkItemContextUpdated = "workitem_context_updated";
    public const string WorkItemScheduled = "workitem_scheduled";
    public const string WorkItemProcessing = "workitem_processing";
    public const string WorkItemSucceeded = "workitem_succeeded";
    public const string WorkItemFailed = "workitem_failed";
    public const string WorkItemCancelled = "workitem_cancelled";
    public const string WorkItemExpired = "workitem_expired";

    /// <summary>
    /// The item went back to <c>created</c>: its attempt was lost, a restart interrupted it, or a person
    /// approved what it was waiting for.
    /// </summary>
    public const string WorkItemReleased = "workitem_released";

    /// <summary>The item needs a person's approval before it can be run, and is waiting for one.</summary>
    public const string WorkItemAwaitingApproval = "workitem_awaiting_approval";

    /// <summary>A decision was asked of a person: which approval, about which work, over which subject.</summary>
    public const string ApprovalRequested = "approval_requested";

    public const string ApprovalApproved = "approval_approved";

    public const string ApprovalRejected = "approval_rejected";

    /// <summary>The decision was outgrown: the subject changed, or a newer parking replaced it.</summary>
    public const string ApprovalSuperseded = "approval_superseded";

    /// <summary>The work the decision was about is gone, so the decision is not one anybody needs to make.</summary>
    public const string ApprovalCancelled = "approval_cancelled";

    /// <summary>An expired item was given a future due date and is work again.</summary>
    public const string WorkItemReopened = "workitem_reopened";
    public const string RoleAdded = "role_added";

    /// <summary>A role's own settings changed — which execution profile its work uses, and nothing else yet.</summary>
    public const string RoleUpdated = "role_updated";

    /// <summary>
    /// A role replaced its memory of a campaign. The line names the campaign, the role, how big the note is and
    /// what it hashes to — never what it says, because a note holds half-formed guesses about people and the
    /// chronicle is the one table nobody can edit afterwards.
    /// </summary>
    public const string RoleNoteSet = "role_note_set";

    /// <summary>A new execution profile exists: which host it names, and what it was verified against.</summary>
    public const string ProfileCreated = "profile_created";

    /// <summary>A profile was edited, which appends a revision rather than changing one: the line names both numbers.</summary>
    public const string ProfileRevised = "profile_revised";

    /// <summary>The profile is out of service; work that resolves to it blocks until somebody says otherwise.</summary>
    public const string ProfileDisabled = "profile_disabled";

    public const string ProfileEnabled = "profile_enabled";

    /// <summary>A new plugin snapshot became the active one: which packages, at which digests, from when.</summary>
    public const string PluginsReloaded = "plugins_reloaded";

    /// <summary>Where a campaign sends its provider work changed: the route that was set, moved or removed.</summary>
    public const string RoutesUpdated = "routes_updated";

    /// <summary>A plugin's own identifier for one of our entities was learned and written down for good.</summary>
    public const string ExternalIdPinned = "external_id_pinned";

    /// <summary>A plugin answered with a different identifier than the one already pinned; both are kept.</summary>
    public const string ExternalIdDiverged = "external_id_diverged";

    /// <summary>Somebody reported an effect they performed outside Jason. The line names the report; the report
    /// holds what they said.</summary>
    public const string ExternalEffectReported = "external_effect_reported";

    public static IReadOnlySet<string> Reserved { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        CampaignCreated,
        CampaignUpdated,
        CampaignStarted,
        CampaignPaused,
        CampaignArchived,
        ContextUpdated,
        ContactsAdded,
        ContactsRemoved,
        ContactCreated,
        ContactUpdated,
        ContactArchived,
        SuppressionAdded,
        SuppressionRemoved,
        WorkItemCreated,
        WorkItemUpdated,
        WorkItemContextUpdated,
        WorkItemScheduled,
        WorkItemProcessing,
        WorkItemSucceeded,
        WorkItemFailed,
        WorkItemCancelled,
        WorkItemExpired,
        WorkItemReleased,
        WorkItemAwaitingApproval,
        ApprovalRequested,
        ApprovalApproved,
        ApprovalRejected,
        ApprovalSuperseded,
        ApprovalCancelled,
        WorkItemReopened,
        RoleAdded,
        RoleUpdated,
        RoleNoteSet,
        ProfileCreated,
        ProfileRevised,
        ProfileDisabled,
        ProfileEnabled,
        PluginsReloaded,
        RoutesUpdated,
        ExternalIdPinned,
        ExternalIdDiverged,
        ExternalEffectReported,
    };

    public static bool IsWellFormed(string kind) => kind is not null && WellFormed().IsMatch(kind);

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex WellFormed();
}
