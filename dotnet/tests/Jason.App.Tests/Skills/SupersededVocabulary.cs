namespace Jason.App.Tests.Skills;

/// <summary>
/// The words of the model this pack replaces. Each entry is a phrase rather than a word, because the words
/// themselves are often innocent — an account may be a workspace, and a role's note is its own memory. What
/// is refused is the idea: operational state kept in files, and something other than the runtime deciding
/// when work runs.
/// </summary>
/// <remarks>
/// The first entry is refused even inside a sentence that denies one. Naming the idea in order to deny it
/// teaches the idea, and a reader who has never heard of it does not need to be introduced: the skills say
/// "nothing else wakes the work — the runtime does".
/// </remarks>
internal static class SupersededVocabulary
{
    private static readonly string[] Phrases =
    [
        // Something other than the runtime deciding when work runs.
        "orchestrator",

        // The retired pack's own model: a directory of markdown as the queue and the lifecycle record.
        "markdown workspace",
        "work item file",
        "work-item file",
        "plan file",
        "state file",
        "todo.md",
        "backlog.md",

        // Where standing preferences used to go. Campaign context is authoritative and journalled; a role
        // note is one role's private memory; cross-campaign learned practice does not exist in this version.
        "user memory",
        "user-memory",
    ];

    public static IReadOnlyList<string> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return [.. Phrases.Where(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase))];
    }
}
