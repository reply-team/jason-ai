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

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ArchivedAt { get; set; }

    public List<CampaignContact> Members { get; } = [];
}
