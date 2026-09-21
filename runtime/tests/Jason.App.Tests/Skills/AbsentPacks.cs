namespace Jason.App.Tests.Skills;

/// <summary>
/// Packs the business knowledge used to sit beside, and that this repository does not ship. A reference to one
/// of them is not a matter of taste: it is a pointer to a file that is not here, in a text whose whole job is
/// to be followed literally by something that cannot check.
/// </summary>
/// <remarks>
/// <c>durable-work</c> is the sharpest of them. It taught that deciding where plans and work items physically
/// live is the job of a separate, optional pack — inside the repository of the runtime that owns durable work
/// outright. The model this runtime replaced left the vocabulary before the move and survived in the pointers.
/// </remarks>
internal static class AbsentPacks
{
    private static readonly string[] Names =
    [
        "agentic-runtime",
        "reply-adapter",
        "durable-work",
        "execution-reporting",
        "orchestrator-integration",
    ];

    public static IReadOnlyList<string> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return [.. Names.Where(name => text.Contains(name, StringComparison.OrdinalIgnoreCase))];
    }
}
