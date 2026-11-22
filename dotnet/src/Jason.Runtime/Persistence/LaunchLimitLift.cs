using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// A person's word that the daily limits of launches do not hold for the rest of one of the installation's days: every
/// role in every campaign runs past <c>Launches:MaxPerDay</c> until that day's next midnight. One row per day and zone,
/// and nothing deletes one: at the next midnight it no longer covers the day the claim counts, and it stays as the
/// record of who lifted which day and why.
/// </summary>
public sealed class LaunchLimitLift
{
    public int Id { get; set; }

    /// <summary>The installation's local date the lift covers.</summary>
    public DateOnly LocalDate { get; set; }

    /// <summary>The zone that date was taken in, as an IANA id: a date of another zone is another day.</summary>
    public required string TimeZone { get; set; }

    /// <summary>Who lifted it: a person, by the id they gave.</summary>
    public ActorType ActorType { get; set; }

    public string? ActorId { get; set; }

    /// <summary>The reason they gave, or null.</summary>
    public string? Reason { get; set; }

    /// <summary>When it was lifted.</summary>
    public DateTime CreatedAt { get; set; }
}
