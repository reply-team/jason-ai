using System.Text.Json.Serialization;

namespace Jason.Contracts.Api;

/// <summary>
/// A person's word that today's daily limits of launches do not hold: every role in every campaign runs past
/// <c>Launches:MaxPerDay</c> until the installation's next midnight. The actor is a person with an id, and the line says
/// whose word it is; the reason is journaled with it.
/// </summary>
public sealed record LaunchesLiftRequest(ActorRef? Actor, string? Reason);

/// <summary>
/// What a lift stands at: the UTC instant of the installation's next midnight, when the limits hold again, the zone that
/// midnight is the installation's in, how many held items this lift let go, and whether the day had been lifted already —
/// a second lift the same day writes nothing and still lets go what was held since.
/// </summary>
public sealed record LaunchesLiftDto(DateTimeOffset Until, string TimeZone, int Released, bool AlreadyLifted);

/// <summary>
/// A read of the daily limits of launches as they stand now. With a campaign, each role also says how many of its runs
/// started there today, and <c>held</c> counts that campaign's items alone.
/// </summary>
public sealed record LaunchesListRequest(string? CampaignId);

/// <summary>
/// The daily limits of launches as they stand: the installation's day they count in, the lift that stands today or null,
/// each role's limit, and how many work items wait on a daily limit now — in every campaign, or in the one asked about.
/// Facts only; setting a limit is the settings file's.
/// </summary>
public sealed record LaunchesListDto(LaunchesDayDto Day, LaunchesStandingLiftDto? Lift, IReadOnlyList<LaunchLimitDto> Roles, int Held);

/// <summary>
/// The installation's day the limits count in: from its midnight to the next, as UTC instants, in the zone it is taken in
/// (<c>Runtime:TimeZone</c>, or the system's own).
/// </summary>
public sealed record LaunchesDayDto(DateTimeOffset Start, DateTimeOffset End, string TimeZone);

/// <summary>A person's lift of today's limits: until when it holds, whose word it was, and when they said it.</summary>
public sealed record LaunchesStandingLiftDto(DateTimeOffset Until, ActorRef Actor, DateTimeOffset LiftedAt);

/// <summary>
/// One role's daily limit as the claim reads it now: <c>max_per_day</c> is null where the role has none, and
/// <c>source</c> says whether the number is the one Jason ships or one the settings set. <c>started_today</c> is there only
/// when a campaign was asked about: the runs of the role that started there since the installation's midnight, counted as
/// the claim counts them.
/// </summary>
public sealed record LaunchLimitDto(
    string Role,
    int? MaxPerDay,
    LaunchLimitSource Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? StartedToday = null);

/// <summary>Where a role's daily limit comes from: the number Jason ships, or the settings.</summary>
public enum LaunchLimitSource
{
    Default,
    Settings,
}
