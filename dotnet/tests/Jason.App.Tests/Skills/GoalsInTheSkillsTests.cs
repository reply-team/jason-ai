using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Context;

namespace Jason.App.Tests.Skills;

/// <summary>
/// The goals a skill prints and the kinds it names are the runtime's own: a skill that printed a goal the runtime
/// refuses, or named a kind it does not know, would teach a person's agent a campaign that cannot be made.
/// </summary>
public sealed partial class GoalsInTheSkillsTests
{
    [GeneratedRegex(@"'(\{[^']*\})'")]
    private static partial Regex QuotedObject();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>
    /// Every <c>goals</c> value a skill prints in a context it writes, read out of the printed line and held to the
    /// document the runtime enforces. A skill that prints none fails here too: the two are the ones that print how a
    /// campaign is given its goals; the session file points to the second for it.
    /// </summary>
    [Theory]
    [InlineData("planner")]
    [InlineData("managed-campaign-work")]
    public void The_goals_a_skill_prints_are_ones_the_runtime_accepts(string skill)
    {
        var contract = ContextKeyCatalog.Find("goals");
        Assert.NotNull(contract);

        var found = 0;
        foreach (var line in SkillPack.PrintedCommands(SkillPack.Find(skill).File))
        {
            foreach (Match quoted in QuotedObject().Matches(line))
            {
                if (JsonNode.Parse(quoted.Groups[1].Value) is JsonObject context && context.TryGetPropertyValue("goals", out var goals))
                {
                    found++;
                    var problems = contract.CheckValue(goals);
                    Assert.True(problems.Count == 0, $"{skill} prints goals the runtime refuses: {line} -> {string.Join("; ", problems)}");
                }
            }
        }

        Assert.True(found > 0, $"{skill} prints no goals in a context it writes.");
    }

    /// <summary>The kinds a skill lists are the document's, all of them and no other.</summary>
    [Fact]
    public void The_kinds_the_managed_work_skill_names_are_the_kinds_the_runtime_knows()
    {
        var contract = ContextKeyCatalog.Find("goals");
        Assert.NotNull(contract);
        var kinds = contract.ValueSchema["items"]!["properties"]!["kind"]!["enum"]!.AsArray().Select(kind => (string)kind!).ToList();

        var text = Whitespace().Replace(File.ReadAllText(SkillPack.Find("managed-campaign-work").File), " ");
        var named = Regex.Match(text, @"a `kind` \(([^)]*)\)");
        Assert.True(named.Success, "managed-campaign-work names no `kind` of a goal.");
        var listed = Regex.Matches(named.Groups[1].Value, "`([a-z_]+)`").Select(match => match.Groups[1].Value).ToList();

        Assert.Equal(kinds.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The default a skill states for a campaign with no goals is the one the contract states, and the key is the
    /// reserved one, so a skill that said a campaign with no goals is refused, or that the roles take a trial, fails.
    /// </summary>
    [Theory]
    [InlineData("planner")]
    [InlineData("planner-in-session")]
    [InlineData("managed-campaign-work")]
    public void A_skill_that_settles_goals_states_the_default_the_contract_states(string skill)
    {
        var contract = Whitespace().Replace(
            File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "docs", "contracts", "context", "goals.md")),
            " ");
        Assert.Contains("the roles take the default goal, a demo", contract, StringComparison.Ordinal);

        var text = Whitespace().Replace(File.ReadAllText(SkillPack.Find(skill).File), " ");
        Assert.Matches(@"default goal, a demo|default[^.]*\ba demo\b|a demo[^.]*default", text);
        Assert.Contains("`goals`", text, StringComparison.Ordinal);
    }
}
