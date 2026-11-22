using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

/// <summary>
/// What each interactive skill would go wrong without. The role skills have had guards like these since the
/// manager was written; the interactive ones had parse and execution guards and nothing holding the facts
/// themselves — so a sentence could be edited away and every test would stay green.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here names, in its comment, the failure it prevents. One that cannot name a failure does
/// not get written: that is how this stays a set of guards rather than a second copy of the text.
/// </para>
/// <para>
/// One file per skill — <c>InteractiveSkillTests.&lt;Skill&gt;.cs</c> — because these five were written in
/// parallel, and five workers editing one file is five conflicts.
/// </para>
/// </remarks>
public partial class InteractiveSkillTests
{
    /// <summary>Flattened, so a pinned sentence may span a line break: what is guarded is what the skill says.</summary>
    internal static string Flattened(string name) =>
        Whitespace().Replace(File.ReadAllText(SkillPack.Find(name).File), " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// The prose of a skill: its body without the front matter, the status paragraph, any fenced block, the headings and
    /// the craft block. Shared by the guards that refuse a rule copied rather than pointed to.
    /// </summary>
    private static string Prose(string file) => ProseOf(SkillPack.Body(file).Split('\n'));

    /// <summary>The prose of some lines of a skill, one section say, read as <see cref="Prose(string)"/> reads a body.</summary>
    private static string ProseOf(IEnumerable<string> lines)
    {
        var kept = new List<string>();
        var status = false;
        foreach (var (line, fenced) in SkillPack.Fenced(lines))
        {
            var start = line.TrimStart();
            if (fenced || start.StartsWith('#'))
            {
                continue;
            }

            if (start.StartsWith("**Status:", StringComparison.Ordinal))
            {
                status = true;
            }

            if (status)
            {
                status = start.Length > 0;
                continue;
            }

            kept.Add(line);
        }

        // The form of the person's word in the chat is rule 13 of the chat's instructions, given where each skill sets or
        // runs an act on that word; quoting the chat's own rule is not copying a sibling's.
        var prose = Regex.Replace(CraftBlockText().Replace(string.Join('\n', kept), " "), @"\s+", " ");
        return ChatsForm().Replace(prose, " ");
    }

    /// <summary>The form of the person's word that rule 13 of the chat's instructions gives, as a skill quotes it.</summary>
    [GeneratedRegex(@"with `--actor human:<account>` as Jason's first paragraph names them and `--reason '\.\.\.'` in your own words(?: \([^)]*\))?, at the end of the command")]
    private static partial Regex ChatsForm();

    /// <summary>Every run of eight consecutive words, lower-cased, punctuation and emphasis dropped.</summary>
    private static HashSet<string> Runs(string prose)
    {
        var words = ProseWord().Matches(prose.ToLowerInvariant()).Select(match => match.Value).ToList();
        var runs = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index + 8 <= words.Count; index++)
        {
            runs.Add(string.Join(' ', words.Skip(index).Take(8)));
        }

        return runs;
    }

    [GeneratedRegex(@"<!-- craft:begin -->.*?<!-- craft:end -->", RegexOptions.Singleline)]
    private static partial Regex CraftBlockText();

    [GeneratedRegex(@"[a-z0-9']+")]
    private static partial Regex ProseWord();
}
