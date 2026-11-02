using System.Collections.Frozen;

namespace Jason.Contracts.Configuration;

/// <summary>
/// The kinds a <c>Manager:Triggers</c> entry may name: the chronicle kinds the runtime writes itself.
/// </summary>
/// <remarks>
/// The list lives here rather than beside the runtime's own because the command-line client needs it, to tell a
/// setting that names one of these from a word somebody typed, and cannot reference the runtime. A copy is only as
/// good as the test that holds it to the original, and one does, the way the settings vocabulary is held: a kind
/// the runtime starts writing and this list lacks fails a test rather than becoming a refused word wherever a
/// settings file that names it is described.
/// </remarks>
public static class ManagerTriggerKinds
{
    public static IReadOnlySet<string> All { get; } = new[]
    {
        "campaign_created", "campaign_updated", "campaign_started", "campaign_paused", "campaign_archived",
        "context_updated",
        "contacts_added", "contacts_removed", "contact_created", "contact_updated", "contact_archived",
        "suppression_added", "suppression_removed",
        "workitem_created", "workitem_updated", "workitem_context_updated", "workitem_scheduled", "workitem_processing",
        "workitem_succeeded", "workitem_failed", "workitem_cancelled", "workitem_expired", "workitem_released",
        "workitem_awaiting_approval", "workitem_reopened", "workitem_waiting", "workitem_resumed",
        "approval_requested", "approval_approved", "approval_rejected", "approval_superseded", "approval_cancelled",
        "standing_approval_set", "standing_approval_unset", "approval_standing_applied",
        "role_added", "role_updated", "role_note_set",
        "profile_created", "profile_revised", "profile_disabled", "profile_enabled",
        "plugins_reloaded", "routes_updated",
        "external_id_pinned", "external_id_diverged", "external_id_origin", "external_effect_reported",
        "decision_raised", "decision_answered", "decision_cancelled",
        "notification_sent",
        "inbound_reply_observed", "provider_event_observed", "message_sent_observed", "reply_classified",
        "events_read", "events_gap_observed", "events_backfill_requested",
        "provider_blocked", "provider_block_cleared",
        "project_created", "project_updated", "project_linked",
        "preference_set", "preference_unset",
    }.ToFrozenSet(StringComparer.Ordinal);
}
