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

    /// <summary>The item went back to <c>created</c>: its attempt was lost, or a restart interrupted it.</summary>
    public const string WorkItemReleased = "workitem_released";

    /// <summary>An expired item was given a future due date and is work again.</summary>
    public const string WorkItemReopened = "workitem_reopened";
    public const string RoleAdded = "role_added";

    /// <summary>A new plugin snapshot became the active one: which packages, at which digests, from when.</summary>
    public const string PluginsReloaded = "plugins_reloaded";

    /// <summary>Where a campaign sends its provider work changed: the route that was set, moved or removed.</summary>
    public const string RoutesUpdated = "routes_updated";

    /// <summary>A plugin's own identifier for one of our entities was learned and written down for good.</summary>
    public const string ExternalIdPinned = "external_id_pinned";

    /// <summary>A plugin answered with a different identifier than the one already pinned; both are kept.</summary>
    public const string ExternalIdDiverged = "external_id_diverged";

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
        WorkItemReopened,
        RoleAdded,
        PluginsReloaded,
        RoutesUpdated,
        ExternalIdPinned,
        ExternalIdDiverged,
    };

    public static bool IsWellFormed(string kind) => kind is not null && WellFormed().IsMatch(kind);

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex WellFormed();
}
