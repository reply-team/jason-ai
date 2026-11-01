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
}
