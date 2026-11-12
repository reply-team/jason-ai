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

    /// <summary>The project the campaign works toward. Every campaign has exactly one; a campaign made without naming one is in the default.</summary>
    public required int ProjectId { get; set; }

    public Project? Project { get; set; }

    /// <summary>Where the campaign's folder is, once there are folders. Nothing writes it yet, and no answer carries it.</summary>
    public string? FolderPath { get; set; }

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
    /// How few untouched members the campaign keeps before its manager is told the reserve is low; null means nobody
    /// counts them. A row and not the context for the reason the review pace is one: the dispatcher reads it.
    /// </summary>
    public int? AudienceThreshold { get; set; }

    /// <summary>
    /// The highest journal entry the reserve step has checked this campaign's count against. It reads only the lines
    /// above it, so a tick with nothing new to look at costs one read and counts nobody.
    /// </summary>
    public int AudienceWatermark { get; set; }

    /// <summary>
    /// The highest journal entry this campaign has been reviewed against. The summon reads the chronicle above
    /// it, so an entry that has been accounted for is never accounted for twice.
    /// </summary>
    public int ManagerEventWatermark { get; set; }

    /// <summary>
    /// The highest journal entry the respond step has read for this campaign. It reads the replies said above it, so a
    /// reply is answered once whichever reader said it. Its own number, not the manager's: a narrowed list of what wakes a
    /// manager must not stop a reply from waking the responder, nor the other way round.
    /// </summary>
    public int ResponderWatermark { get; set; }

    /// <summary>
    /// What the cadence is measured from: the campaign going live, and every check-in since. Null while a
    /// campaign has never been live, which is also why such a campaign is never due.
    /// </summary>
    public DateTime? ManagerReviewAnchor { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ArchivedAt { get; set; }

    /// <summary>
    /// The key the caller created this campaign with, trimmed, or null when it gave none. A property of the campaign
    /// and not an entry in a cache: it lasts as long as the campaign does, and one key names one campaign.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>The <c>sha256:</c> of the request that created the campaign under its key, which a repeat must match.</summary>
    public string? IdempotencyDigest { get; set; }

    /// <summary>
    /// Where the next read of this campaign's conversations starts: the runtime's clock, never a provider's stamp. Set
    /// when the campaign first goes live, and after each complete read to that read's start less the look-back, never
    /// moving back. Null while a campaign has never been live.
    /// </summary>
    public DateTime? InboundWatermark { get; set; }

    /// <summary>When the last read was started, or the last skip accounted; what the cadence is measured from.</summary>
    public DateTime? InboundSyncedAt { get; set; }

    /// <summary>The read still outstanding: the work item that lists the conversations, or null when none is.</summary>
    public int? InboundSyncItemId { get; set; }

    /// <summary>How far into the read's pages the outstanding read is; zero on its first page.</summary>
    public int InboundOffset { get; set; }

    /// <summary>How many reads in a row have ended without a result; the next one waits longer for each.</summary>
    public int InboundFailures { get; set; }

    /// <summary>
    /// From when a provider's events are said in this campaign's chronicle: the runtime's clock when the campaign first
    /// went live, and never moved after. An event before it is history — stored, and found to concern the campaign, but
    /// said by no line. Null while a campaign has never been live.
    /// </summary>
    public DateTime? EventsFrom { get; set; }

    public List<CampaignContact> Members { get; } = [];

    /// <summary>What plugins call this campaign. Loaded wherever the campaign is answered with, so an empty list means none.</summary>
    public List<ExternalId> ExternalIds { get; } = [];
}
