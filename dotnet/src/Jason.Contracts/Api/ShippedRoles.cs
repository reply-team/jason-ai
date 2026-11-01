namespace Jason.Contracts.Api;

/// <summary>
/// The roles Jason ships: the builtin roster a fresh runtime is seeded with, in the order it is seeded. A role of any other
/// name is one a person added, and its name is that person's own word. The runtime's seed is held to this list by a test.
/// </summary>
public static class ShippedRoles
{
    /// <summary>The names, in the seeded order.</summary>
    public static IReadOnlyList<string> Names { get; } =
    [
        "manager",
        "planner",
        "researcher",
        "copywriter",
        "personalizer",
        "critic",
        "responder",
        "analyst",
        "deliverability-specialist",
    ];
}
