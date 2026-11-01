namespace Jason.App.Tests.Skills;

/// <summary>
/// A role's judgement, shared by the two files that teach that role: the launched role's file under
/// <c>roles/&lt;role&gt;/</c>, which the runtime hands to the role it launches on a work item, and the session file
/// <c>&lt;role&gt;-in-session/</c>, which a person's own session reads. Between two markers, like the launch contract,
/// so that a guard can hold the two copies to the same bytes and a correction that reaches one of them and not the
/// other is a red test rather than two roles that quietly disagree.
/// </summary>
internal static class CraftBlock
{
    public const string Begin = "<!-- craft:begin -->";

    public const string End = "<!-- craft:end -->";

    /// <summary>What a session file's name adds to the name of the role it pairs with.</summary>
    public const string SessionSuffix = "-in-session";

    /// <summary>The block, line endings normalised, or null when this file carries none.</summary>
    public static string? Extract(string file) => MarkedBlock.Extract(file, Begin, End);

    /// <summary>
    /// Every session file in the runtime pack and the role its name pairs it with. By the name rather than by a
    /// list typed here: a list would agree with itself for ever, and a session file nobody added to it would be
    /// guarded by nothing.
    /// </summary>
    public static IReadOnlyList<SessionFile> SessionFiles() =>
    [
        .. SkillPack.All()
            .Where(skill => !skill.IsRole && skill.Name.EndsWith(SessionSuffix, StringComparison.Ordinal))
            .Select(skill => new SessionFile(skill, skill.Name[..^SessionSuffix.Length])),
    ];
}

/// <summary>One session file, and the name of the role it says it pairs with.</summary>
internal sealed record SessionFile(PackSkill Skill, string Role);
