using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

/// <summary>
/// What each role would go wrong without: the one thing this build cannot give it, said in its own file. The
/// roster describes professions — "handles replies", "warm-up and deliverability diagnosis" — and a launched
/// role cannot ask anybody whether the verb it wants exists. Told nothing, it spends a paid attempt looking for
/// one and then invents it; told plainly, it reads what is there and reports.
/// </summary>
/// <remarks>
/// One file per role — <c>RoleSkillTests.&lt;Role&gt;.cs</c> — the way the interactive skills have one each,
/// because these were written in parallel and several workers editing one file is several conflicts.
/// </remarks>
public partial class RoleSkillTests
{
    /// <summary>Flattened, so a pinned sentence may span a line break: what is guarded is what the skill says.</summary>
    internal static string Flattened(string role) =>
        Whitespace().Replace(File.ReadAllText(SkillPack.Find(role).File), " ");

    /// <summary>
    /// And that this file carries the launch contract at all. The pack-wide guard holds the blocks identical to
    /// each other; this holds that a role has one, so a file that dropped it fails in its own name rather than
    /// by making some other role's comparison the failure.
    /// </summary>
    internal static void CarriesTheLaunchContract(string role) =>
        Assert.True(
            LaunchContract.Extract(SkillPack.Find(role).File) is not null,
            $"'{role}' carries no launch contract, so it was told nothing about how it was launched.");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
