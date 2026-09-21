using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

/// <summary>
/// Every <c>SKILL.md</c> a catalog <em>links</em>. Markdown links only, and never any token that happens to
/// end in the file's name: a catalog also explains where a role's skill ends up — at
/// <c>.claude/skills/&lt;role&gt;/SKILL.md</c> inside the attempt's work directory — and a reader that took
/// prose for a link would report a missing skill for a sentence that is telling the truth.
/// </summary>
/// <remarks>
/// Shared, because both packs have a catalog and what a person needs from either is something to click.
/// </remarks>
internal static partial class PackCatalog
{
    public static IEnumerable<string> Links(string catalog)
    {
        foreach (var match in LinkTarget().Matches(catalog).Cast<Match>())
        {
            yield return match.Groups[1].Value;
        }
    }

    [GeneratedRegex(@"\]\(([^()\s]+/SKILL\.md)\)")]
    private static partial Regex LinkTarget();
}
