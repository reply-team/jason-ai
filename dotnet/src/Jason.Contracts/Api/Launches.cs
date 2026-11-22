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
