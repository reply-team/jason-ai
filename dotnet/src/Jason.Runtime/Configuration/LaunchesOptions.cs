namespace Jason.Runtime.Configuration;

/// <summary>
/// How many runs of a role one campaign may start in the installation's day, from one midnight to the next in the zone
/// <c>Runtime:TimeZone</c> names, or the system's own. Read through its seam once a scan, at the claim's start.
/// </summary>
public sealed class LaunchesOptions
{
    public const string Section = "Launches";

    /// <summary>The fewest and most launches of a role a campaign may be allowed in one of the installation's days.</summary>
    public const int MinimumPerDay = 1;

    public const int MaximumPerDay = 1_000;

    /// <summary>
    /// The limit of each role, by the role's name, counted from the installation's midnight by the attempts that started. A
    /// role this map does not name, a custom role among them, has no limit. The map ships a net against a runaway role
    /// for each of the nine roles Jason ships, to be corrected after the first live campaigns. The binder adds what the
    /// file writes to what stands here, so a shipped role's limit can be changed and never removed. A run the claim finds at the limit is not failed — a failure
    /// would wake the manager — but held: it waits, with no attempt, until the installation's next midnight, until a person
    /// resumes it, or until a person lifts the day's limits, which lets every role in every campaign past them until that
    /// midnight. A raised limit applies from the next scan to work not yet held, and an item already held keeps its moment.
    /// </summary>
    public Dictionary<string, int> MaxPerDay { get; set; } = new(StringComparer.Ordinal)
    {
        ["critic"] = 240,
        ["personalizer"] = 120,
        ["researcher"] = 40,
        ["manager"] = 25,
        ["responder"] = 25,
        ["planner"] = 10,
        ["copywriter"] = 10,
        ["analyst"] = 10,
        ["deliverability-specialist"] = 10,
    };
}
