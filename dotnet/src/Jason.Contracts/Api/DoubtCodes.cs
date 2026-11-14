using Jason.Contracts.Journal;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>
/// Why a send is doubted: one closed list, the same for a doubting review (<c>workitem.review</c>) and for the reserved
/// context key <c>ask_person</c> a send may be created with. The list is data, read from the one place it is published,
/// the <c>workitem_reviewed</c> kind's document (<c>docs/contracts/journal/workitem_reviewed.json</c>, its
/// <c>$defs/code</c>), which a role reads with <c>jason journal kinds</c> and a plugin author reads in the tree: no second
/// copy is kept here.
/// </summary>
public static class DoubtCodes
{
    /// <summary>The kind whose document publishes the list.</summary>
    public const string Kind = "workitem_reviewed";

    /// <summary>
    /// The longest sentence a doubt may carry beside its code, a line short enough to read on an approval's card: the note of
    /// <c>ask_person</c> (<c>ask_person_note</c>) and of a doubting review alike.
    /// </summary>
    public const int MaxNoteLength = 280;

    /// <summary>Every code, in the order the document lists them.</summary>
    public static IReadOnlyList<string> All { get; } = Read();

    /// <summary>Whether <paramref name="code"/> is one of the list, compared exactly as written.</summary>
    public static bool Knows(string? code) => code is not null && All.Contains(code, StringComparer.Ordinal);

    private static string[] Read()
    {
        var contract = JournalKindCatalog.Find(Kind)
            ?? throw new InvalidOperationException($"The `{Kind}` kind's document is not embedded, and it publishes the doubt codes.");
        if (contract.Body["$defs"]?["code"]?["anyOf"] is not JsonArray branches || branches.Count == 0)
        {
            throw new InvalidOperationException($"{Kind}.json: `$defs/code` is a non-empty `anyOf` of the codes, each a `const`.");
        }

        return [.. branches.Select(branch => (string?)branch?["const"]
            ?? throw new InvalidOperationException($"{Kind}.json: every branch of `$defs/code` names its code as a `const`."))];
    }
}
