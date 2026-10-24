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

    /// <summary>
    /// An attempt ended waiting: keyed by the attempt, with the work items it waits on, whether all or any of them, the
    /// moment it named and the deadline that bounds it. Identifiers and moments only, and not a default manager trigger: the
    /// dispatcher, not a review, is what ends a wait.
    /// </summary>
    public const string WorkItemWaiting = "workitem_waiting";

    /// <summary>
    /// A waiting item went back to <c>created</c>, with why it was released. Identifiers only — when a person released
    /// it through <c>workitem.resume</c>, the line is theirs and carries the reason they gave — and not a default manager
    /// trigger.
    /// </summary>
    public const string WorkItemResumed = "workitem_resumed";
    public const string RoleAdded = "role_added";

    /// <summary>A role's own settings changed — which execution profile its work uses, and nothing else yet.</summary>
    public const string RoleUpdated = "role_updated";

    /// <summary>
    /// A role replaced its memory at one level. The line names the level — the campaign, as a line of that campaign;
    /// the project, as a line of the installation; or neither, for the client's — the role, how big the note is and
    /// what it hashes to; never what it says, because a note holds half-formed guesses about people and the
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

    /// <summary>Somebody reported an effect they performed outside Jason. The line carries the report's id, the effect,
    /// tool and operation as they wrote them, and what it is about; the report holds the rest of what they said.</summary>
    public const string ExternalEffectReported = "external_effect_reported";

    /// <summary>A role asked a question it could not answer, and ended its attempt carrying the reference.</summary>
    public const string DecisionRaised = "decision_raised";

    /// <summary>
    /// A person answered one. This is a trigger kind: the answer is what releases the review that continues
    /// the work, which is why nothing but a person's own verb may write it.
    /// </summary>
    public const string DecisionAnswered = "decision_answered";

    /// <summary>The campaign the question was about is archived, so there is nothing left to decide.</summary>
    public const string DecisionCancelled = "decision_cancelled";

    /// <summary>
    /// What happened to one notification: the counts it carried, the line it covers through, and each channel's
    /// outcome. It names no campaign, so it never wakes a manager.
    /// </summary>
    public const string NotificationSent = "notification_sent";

    /// <summary>
    /// A read of a campaign's conversations found a thread that moved since the read began: the thread, the moment it
    /// moved, the provider's own flags, and the person it belongs to where one is pinned. Never a subject, a preview or a
    /// name. This is a trigger kind: a reply is the manager's to read, and only the runtime's own read may say one came.
    /// </summary>
    public const string InboundReplyObserved = "inbound_reply_observed";

    /// <summary>
    /// A provider's event that is not a thread moving — a reply the provider detected, an invitation accepted, a message
    /// that bounced — stored and found to concern the campaign by an identifier its operations pinned: the event's kind,
    /// identifiers, moment and the provider's own flags, and the person where one contact is pinned. Never anything
    /// anybody wrote. A trigger kind, and only the runtime's own store may say one.
    /// </summary>
    public const string ProviderEventObserved = "provider_event_observed";

    /// <summary>
    /// A read of an account's events that stored something, failed, or read by date: the source, the plugin, how it read,
    /// how many pages and events, how it ended, and which package and snapshots it ran under. Never a cursor, a binding or
    /// anything an event holds. It names no campaign, so it never wakes a manager; a read of a cursor that found nothing
    /// writes none.
    /// </summary>
    public const string EventsRead = "events_read";

    /// <summary>
    /// An account whose events were not read for longer than the store reaches back: the source, the plugin, and the span
    /// from where reading stopped to where it began again. Said once, under a key of the source and where the span starts,
    /// and counted by the next notification that reaches anybody. It names no campaign.
    /// </summary>
    public const string EventsGapObserved = "events_gap_observed";

    /// <summary>
    /// A person asked for a span of an account's events to be read again: the request, the source, the plugin, the binding
    /// identity and the span, with who asked and their reason. It names no campaign; what the read finds says its own lines.
    /// </summary>
    public const string EventsBackfillRequested = "events_backfill_requested";

    /// <summary>
    /// A provider block holds work of the campaign: keyed by the block's public id, with the plugin, the scope, the
    /// sending account's identifier where the block is a sender's, the reason, the moment the provider said it clears by
    /// itself where it said one, and the work item that met it first in this campaign. Said once per campaign and block,
    /// which the database holds; never the block's link. A default manager trigger.
    /// </summary>
    public const string ProviderBlocked = "provider_blocked";

    /// <summary>
    /// A provider block that held work of the campaign has cleared: keyed by the block's public id, in every campaign it
    /// was said in. Not a trigger by default: the work it held runs again by itself.
    /// </summary>
    public const string ProviderBlockCleared = "provider_block_cleared";

    /// <summary>
    /// A project was made: keyed by its id, with the name and slug it was made with. It names no campaign. The default
    /// project, made by the migration that brought projects in, was made by nobody and has no line.
    /// </summary>
    public const string ProjectCreated = "project_created";

    /// <summary>A project's name changed, keyed by its id, with the name before and after. It names no campaign.</summary>
    public const string ProjectUpdated = "project_updated";

    /// <summary>
    /// A project's folder, or its campaigns', recorded again by <c>project.link</c>: keyed by the project's id, with the
    /// folder recorded before and after and, for each campaign whose path changed, the path before and after. It names no
    /// campaign, and holds paths and ids only: a folder's name is made from a name, and no memory file is ever read.
    /// </summary>
    public const string ProjectLinked = "project_linked";

    /// <summary>
    /// A person set a named preference: keyed by the preference's key, with the size and hash of the value before (null
    /// where it was not set) and after. It names no campaign, and never holds the value: a preference can be personal
    /// data, and the chronicle could never forget it.
    /// </summary>
    public const string PreferenceSet = "preference_set";

    /// <summary>A person unset a named preference and its value was deleted: keyed by the key, with the size and hash it had.</summary>
    public const string PreferenceUnset = "preference_unset";

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
        WorkItemWaiting,
        WorkItemResumed,
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
        DecisionRaised,
        DecisionAnswered,
        DecisionCancelled,
        NotificationSent,
        InboundReplyObserved,
        ProviderEventObserved,
        EventsRead,
        EventsGapObserved,
        EventsBackfillRequested,
        ProviderBlocked,
        ProviderBlockCleared,
        ProjectCreated,
        ProjectUpdated,
        ProjectLinked,
        PreferenceSet,
        PreferenceUnset,
    };

    public static bool IsWellFormed(string kind) => kind is not null && WellFormed().IsMatch(kind);

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,63}\z")]
    private static partial Regex WellFormed();
}
