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

    /// <summary>
    /// A person set a campaign's standing approval for one category of reply: keyed by the category, with the record it
    /// replaced (null where none stood) and the record set — the category, the operations it covers, its expiry and its
    /// ceiling. Written by <c>approval.set_standing</c> only, and not a default manager trigger.
    /// </summary>
    public const string StandingApprovalSet = "standing_approval_set";

    /// <summary>A person took a campaign's standing approval for one category back: keyed by the category, with the record it had.</summary>
    public const string StandingApprovalUnset = "standing_approval_unset";

    /// <summary>
    /// The dispatcher released work under a standing approval instead of parking it: keyed by the line that set the
    /// standing approval, with the approval it wrote, the operation, the subject's hash, the verdict that named the
    /// category and the category. Never a draft, a subject or a name: the preview stays on the approval. Not a default
    /// manager trigger: the manager reads what went out at its check-in.
    /// </summary>
    public const string ApprovalStandingApplied = "approval_standing_applied";

    /// <summary>
    /// The dispatcher released a send under the automatic mode in force for its contact instead of parking it: keyed by the
    /// temperature, cold or warm, with the approval it wrote, the operation, the subject's hash, where the mode came from and
    /// the review that released it. Identifiers only, never a text or a name; no program may subscribe to it.
    /// </summary>
    public const string ApprovalAutomaticApplied = "approval_automatic_applied";

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

    /// <summary>
    /// A send was reviewed before it went, by <c>workitem.review</c>: keyed by the review hash, with the result, the doubt's
    /// code where it doubts, the operation and the reviewing run's role. The line names the send as its item and the
    /// reviewing attempt as its attempt. Never the doubt's sentence and never a reason. Written by that operation only, not
    /// a default manager trigger, and not subscribable: what it says is the claim's to read.
    /// </summary>
    public const string WorkItemReviewed = "workitem_reviewed";
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

    /// <summary>
    /// Where every campaign without a route of its own sends its provider work changed: a global route was set or taken
    /// away through the settings writer, and the reload that froze it activated.
    /// </summary>
    public const string GlobalRoutesUpdated = "global_routes_updated";

    /// <summary>
    /// What a plugin may use of one capability changed: a grant was given or taken back through the settings writer, and
    /// the reload that froze it activated.
    /// </summary>
    public const string PluginGrantsUpdated = "plugin_grants_updated";

    /// <summary>A plugin's own identifier for one of our entities was learned and written down for good.</summary>
    public const string ExternalIdPinned = "external_id_pinned";

    /// <summary>A plugin answered with a different identifier than the one already pinned; both are kept.</summary>
    public const string ExternalIdDiverged = "external_id_diverged";

    /// <summary>
    /// Whether the provider found the entity a pin or a divergence just named, or created it: <c>found</c> or
    /// <c>created</c>, written right after that line under its key, work item and attempt, and only where the plugin
    /// said which. A plugin that says nothing leaves no line, so the absence of one means unknown.
    /// </summary>
    public const string ExternalIdOrigin = "external_id_origin";

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
    /// A role's verdict on one reply, recorded by <c>reply.record</c>: the plugin, thread and message that name the reply,
    /// its moment, the person where one is named, the category as a word the skills own, the confidence, the reaction
    /// decided and the goal it steers to by id and kind. Never a word of the reply, a subject or a name. Written by that
    /// operation only, and not a default manager trigger: the verdict is itself what a reader of a reply produces.
    /// </summary>
    public const string ReplyClassified = "reply_classified";

    /// <summary>
    /// Any send a read finds gone, once, whether it went inside the send's own call or later: keyed by the work item that
    /// made it, with the plugin, the provider's reference, the channel, the moment it went and the message's identifier in
    /// the thread, and the read that saw it. Never a subject, a body or a name. Said once per send, which the database
    /// holds. Not a default manager trigger: the thread is where a send's consequences arrive.
    /// </summary>
    public const string MessageSentObserved = "message_sent_observed";

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
    /// was said in, by the dispatcher with the attempt that got past it, or by the person who cleared it with the reason
    /// they gave and no attempt. Not a trigger by default: the work it held runs again by itself.
    /// </summary>
    public const string ProviderBlockCleared = "provider_block_cleared";

    /// <summary>
    /// A campaign's untouched members — the summary's <c>new</c> — fell under the <c>audience_threshold</c> it set: the
    /// count and the threshold, no key. Said by the dispatcher once per crossing, which the journal decides: the latest of
    /// this kind and <see cref="AudienceReserveRecovered"/> is where the reserve stands. A default manager trigger.
    /// </summary>
    public const string AudienceReserveLow = "audience_reserve_low";

    /// <summary>
    /// A campaign's untouched members are back at or above its <c>audience_threshold</c>, after a line said they had
    /// fallen under it: the count and the threshold, no key. Not a trigger by default: the manager was told when the
    /// reserve fell, and a reserve that recovered asks nothing of it.
    /// </summary>
    public const string AudienceReserveRecovered = "audience_reserve_recovered";

    /// <summary>
    /// One page of a search's people landed in its campaign: keyed by the work item whose answer carried the page — a read
    /// of the search, or the search itself where it finished inside its call — and about the search's work item, with the
    /// audience, the provider's state, the counts and the people it made members of, never a word of theirs. Said by the
    /// dispatcher once per page that carried anybody. Not a trigger by default: the reserve and the summary count them.
    /// </summary>
    public const string AudiencePeopleLanded = "audience_people_landed";

    /// <summary>
    /// A search ended — finished, exhausted, stalled, stopped, or found gone by a read or a stop — keyed by and about its
    /// work item, with what it was asked for, found and landed. Said by the dispatcher once per search, which the database holds. A default manager trigger:
    /// whether the audience needs more, or another search, is the manager's.
    /// </summary>
    public const string AudienceSearchEnded = "audience_search_ended";

    /// <summary>
    /// A search the provider holds — paused by a person, out of credits, at the plan's limit — keyed by the read that first
    /// said so and about the search's work item, with its audience, the state and the counts. Said by the dispatcher when a
    /// read first reports the hold, and again only after the search moves and holds again. A default manager trigger: the
    /// person is told through the manager's question what holds it and what to do at the provider, while the runtime goes
    /// on reading it on a slower cadence.
    /// </summary>
    public const string AudienceSearchHeld = "audience_search_held";

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

    /// <summary>
    /// A subscriber's handler ended in a final class for one line, so the chain of that kind ended there for that line and
    /// the kind moved on: keyed by the kind, with the line, the plugin, the class, the code and the attempts. Never the
    /// failure's message or the envelope. Written by the subscription runner only, as the system; it names no campaign,
    /// and no program may subscribe to it.
    /// </summary>
    public const string SubscriptionFailed = "subscription_failed";

    /// <summary>
    /// A person moved a subscribed kind's cursor back, by a line of the kind or by a moment: keyed by the kind, with the
    /// line the cursor stood after before and after the move, null at the journal's start. Every rewind is a line of its
    /// own. Written by <c>subscription.rewind</c> only, as the person who asked; it names no campaign, and no program may
    /// subscribe to it.
    /// </summary>
    public const string SubscriptionCursorRewound = "subscription_cursor_rewound";

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
        StandingApprovalSet,
        StandingApprovalUnset,
        ApprovalStandingApplied,
        ApprovalAutomaticApplied,
        WorkItemReopened,
        WorkItemWaiting,
        WorkItemResumed,
        WorkItemReviewed,
        RoleAdded,
        RoleUpdated,
        RoleNoteSet,
        ProfileCreated,
        ProfileRevised,
        ProfileDisabled,
        ProfileEnabled,
        PluginsReloaded,
        RoutesUpdated,
        GlobalRoutesUpdated,
        PluginGrantsUpdated,
        ExternalIdPinned,
        ExternalIdDiverged,
        ExternalIdOrigin,
        ExternalEffectReported,
        DecisionRaised,
        DecisionAnswered,
        DecisionCancelled,
        NotificationSent,
        InboundReplyObserved,
        ProviderEventObserved,
        ReplyClassified,
        MessageSentObserved,
        EventsRead,
        EventsGapObserved,
        EventsBackfillRequested,
        ProviderBlocked,
        ProviderBlockCleared,
        AudienceReserveLow,
        AudienceReserveRecovered,
        AudiencePeopleLanded,
        AudienceSearchEnded,
        AudienceSearchHeld,
        ProjectCreated,
        ProjectUpdated,
        ProjectLinked,
        PreferenceSet,
        PreferenceUnset,
        SubscriptionFailed,
        SubscriptionCursorRewound,
    };

    public static bool IsWellFormed(string kind) => kind is not null && WellFormed().IsMatch(kind);

    [GeneratedRegex(@"^[a-z][a-z0-9_]{0,63}\z")]
    private static partial Regex WellFormed();
}
