namespace Jason.Runtime.Persistence;

/// <summary>
/// A business campaign: the unit of isolation inside the runtime. Skeleton shape only — context, journal and
/// the remaining lifecycle columns arrive with later migrations. Everything done to a campaign happens through
/// work items, which do not exist yet.
/// </summary>
public sealed class Campaign
{
    /// <summary>Internal key, used only for foreign keys inside the database. Never exposed.</summary>
    public int Id { get; set; }

    /// <summary>The only identifier that leaves the runtime: <c>cmp_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public required string Name { get; set; }

    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ArchivedAt { get; set; }
}

/// <summary>draft → active → paused → archived. Stored as snake_case text.</summary>
public enum CampaignStatus
{
    Draft,
    Active,
    Paused,
    Archived,
}
