namespace Jason.App.Tests.Skills;

/// <summary>
/// Packs the business knowledge used to sit beside, and that this repository does not ship. A reference to one
/// of them is not a matter of taste: it is a pointer to a file that is not here, in a text whose whole job is
/// to be followed literally by something that cannot check.
/// </summary>
/// <remarks>
/// Four skills of those packs are deliberately absent from this list: <c>reply-api</c>, <c>reply-auth</c>,
/// <c>reply-cli</c> and <c>reply-mcp</c>. Each is also the ordinary name of something real — the Reply CLI is a
/// repository this product documents and expects a person to install — so listing them would refuse true
/// sentences. None occurs in the pack today, and a reference to one would have to be caught by a reader rather
/// than here. That is what this guard does not catch, said plainly rather than left to be discovered.
/// </remarks>
/// <remarks>
/// <c>durable-work</c> is the sharpest of them. It taught that deciding where plans and work items physically
/// live is the job of a separate, optional pack — inside the repository of the runtime that owns durable work
/// outright. The model this runtime replaced left the vocabulary before the move and survived in the pointers.
/// </remarks>
internal static class AbsentPacks
{
    private static readonly string[] Names =
    [
        // The packs themselves, including the one this knowledge used to be part of: a sentence saying it
        // "ships inside ai-sdr-core" is a claim about a pack this repository does not have.
        "agentic-runtime",
        "reply-adapter",
        "ai-sdr-core",

        // And their skills by name, because a reference names a skill far more often than a pack — six
        // changelog lines sent a reader to reply-operations-mapping without naming the pack it belongs to.
        "durable-work",
        "execution-reporting",
        "orchestrator-integration",
        "reply-operations-mapping",
    ];

    public static IReadOnlyList<string> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return [.. Names.Where(name => text.Contains(name, StringComparison.OrdinalIgnoreCase))];
    }
}
