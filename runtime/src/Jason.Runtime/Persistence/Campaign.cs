using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// A business campaign: the unit of isolation inside the runtime. It owns its context — the shared knowledge
/// every role working the campaign reads — and its members. Work items, which do everything to a campaign,
/// do not exist yet.
/// </summary>
public sealed class Campaign
{
    /// <summary>Internal key, used only for foreign keys inside the database. Never exposed.</summary>
    public int Id { get; set; }

    /// <summary>The only identifier that leaves the runtime: <c>cmp_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public required string Name { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    /// <summary>Mutable current-state JSON: the campaign's shared knowledge. Changes are journaled by the caller.</summary>
    public JsonObject Context { get; set; } = new();

    /// <summary>
    /// Which execution profile this campaign's agent work uses, unless an item names its own. A campaign is the
    /// unit of isolation here, so its policy outranks a role's.
    /// </summary>
    public string? ExecutionProfile { get; set; }

    /// <summary>
    /// How often this campaign is reviewed, in seconds, when the installation's cadence is not what it wants.
    /// Null means <c>Manager:ReviewSeconds</c>. It lives here rather than in the context because the dispatcher
    /// reads it, and the dispatcher must never read the context for meaning.
    /// </summary>
    public int? ManagerReviewSeconds { get; set; }

    /// <summary>
    /// The highest journal entry this campaign has been reviewed against. The summon reads the chronicle above
    /// it, so an entry that has been accounted for is never accounted for twice.
    /// </summary>
    public int ManagerEventWatermark { get; set; }

    /// <summary>
    /// What the cadence is measured from: the campaign going live, and every check-in since. Null while a
    /// campaign has never been live, which is also why such a campaign is never due.
    /// </summary>
    public DateTime? ManagerReviewAnchor { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public List<CampaignContact> Members { get; } = [];

    /// <summary>What plugins call this campaign. Loaded wherever the campaign is answered with, so an empty list means none.</summary>
    public List<ExternalId> ExternalIds { get; } = [];
}
