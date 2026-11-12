namespace Jason.Runtime.Persistence;

/// <summary>
/// One search a campaign's audience is filled by, from the moment the provider took it: where the next read of its people
/// starts, what the provider last said of it, how many it brought in, and how it ended. Made in the change set that ends
/// the <c>audience.search</c> work item <c>succeeded</c>, one row per such item; the reads of its people move it, and it
/// ends once. A search's live state is this row: the work item's result is frozen when the item finishes, and nothing
/// is kept in a step's memory. Named from outside by its search's work item, as a pin is named by its entity, so it
/// carries no public id of its own.
/// </summary>
public sealed class AudienceSearch
{
    public int Id { get; set; }

    public int CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>The audience the search ran under, as its work item carried it under <c>context.audience</c>.</summary>
    public int AudienceId { get; set; }

    public Audience? Audience { get; set; }

    public int SearchWorkItemId { get; set; }

    public WorkItem? SearchWorkItem { get; set; }

    /// <summary>The plugin the search ran at, as its attempt's provenance kept it: the one whose identifiers its people get.</summary>
    public required string PluginId { get; set; }

    /// <summary>The provider's reference for a search it keeps running; null for one it finished inside its call.</summary>
    public string? Reference { get; set; }

    /// <summary>Where the next read starts: the plugin's own words, stored as answered and never parsed. Null until a read answers one.</summary>
    public string? Cursor { get; set; }

    /// <summary>The provider's latest word for the search, as a read answered it; null until a read has answered.</summary>
    public string? State { get; set; }

    /// <summary>Whether the latest read said more people are readable now; the step that makes the next read consumes it.</summary>
    public bool More { get; set; }

    /// <summary>How many people the search was asked for.</summary>
    public int Asked { get; set; }

    /// <summary>How many the provider said it found, as the latest answer said; null until one did.</summary>
    public int? Found { get; set; }

    /// <summary>How many memberships of the campaign this search made.</summary>
    public int Landed { get; set; }

    /// <summary>
    /// When the reads began to land nobody: the search's start, then the moment of each landing that made somebody a
    /// member. How long a search has stood still is measured from here.
    /// </summary>
    public DateTime QuietSince { get; set; }

    /// <summary>When the dispatcher made its latest read of the search; null before its first.</summary>
    public DateTime? ReadAt { get; set; }

    /// <summary>The dispatcher's latest read of the search, whose end decides how long the next one waits.</summary>
    public int? ReadWorkItemId { get; set; }

    public WorkItem? ReadWorkItem { get; set; }

    /// <summary>How many of the dispatcher's reads in a row ended without an answer: each doubles the wait for the next.</summary>
    public int Failures { get; set; }

    /// <summary>When the search succeeded.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>How the search ended — <c>finished</c>, <c>exhausted</c>, <c>stalled</c> or <c>cancelled</c> — and null while it is followed.</summary>
    public string? EndedAs { get; set; }

    public DateTime? EndedAt { get; set; }
}

/// <summary>
/// The fact that a search made a person a member of its campaign: one row per membership a search made, written by the
/// landing once and never changed — a removal or an add-back of the person leaves it as it is. The membership row holds
/// where the person stands; this holds where they came from.
/// </summary>
public sealed class AudienceLanding
{
    public int Id { get; set; }

    public int CampaignContactId { get; set; }

    public CampaignContact? Membership { get; set; }

    public int AudienceId { get; set; }

    public Audience? Audience { get; set; }

    public int SearchWorkItemId { get; set; }

    public WorkItem? SearchWorkItem { get; set; }

    /// <summary>The work item whose answer carried the page: a read of the search, or the search itself where it finished inside its call.</summary>
    public int ReadWorkItemId { get; set; }

    public WorkItem? ReadWorkItem { get; set; }

    public DateTime LandedAt { get; set; }
}
