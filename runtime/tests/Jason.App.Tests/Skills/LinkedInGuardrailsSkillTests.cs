using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

/// <summary>
/// What <c>linkedin-guardrails</c> says about the invitation itself: how long its note may be, and how invitations are
/// spread. A host left to its own knowledge gave a connection note "about 300 characters" and paced by a figure of its
/// own; these hold the two sentences that answer it, and hold that the pacing advice stays a rule in words while every
/// number the skill owes is still an expert's question.
/// </summary>
public sealed partial class LinkedInGuardrailsSkillTests
{
    /// <summary>
    /// The bound is the one the runtime enforces, read from the contract document rather than typed here, so a contract
    /// that moved its bound turns this red until the skill says the new one.
    /// </summary>
    [Fact]
    public void The_connection_note_bound_is_the_bound_the_contract_enforces()
    {
        var bound = NoteBound();

        Assert.Contains(
            $"The note is at most {bound} characters, counted as characters and not bytes, and a longer one is refused when the work is created, before anything is sent (`docs/contracts/operations/social_invitation.send.json`): write it to fit, because a note shortened after approval is not the note the person approved.",
            Flat(Text()),
            StringComparison.Ordinal);
    }

    /// <summary>Said once, where the skill speaks about the invitation, so that a second copy cannot drift from the first.</summary>
    [Fact]
    public void The_note_bound_is_said_once()
    {
        var said = Regex.Matches(Flat(Text()), $@"\b{NoteBound()} characters\b").Count;

        Assert.True(said == 1, $"linkedin-guardrails states the note's bound {said} times; it is said once.");
    }

    /// <summary>
    /// How many invitations and how spread: the same small number each working day, across its hours and across the
    /// week, never an allowance spent at once, and how many is read from the account rather than remembered.
    /// </summary>
    [Fact]
    public void Invitations_are_paced_across_the_day_and_the_week_by_the_account_s_own_allowance()
    {
        var pacing = Pacing();

        Assert.Contains(
            "For invitations that means the same small number each working day, spread across its hours and across the week, never a day's allowance spent in one sitting or a week's in one day.",
            pacing,
            StringComparison.Ordinal);
        Assert.Contains(
            "How many is the account's to say — read it from `sender.health` and `channel.describe`, keep well under it — and no figure in this skill replaces that read.",
            pacing,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// No figure is sourced for pacing anywhere, and the skill's own rule is that the channel supplies the value. So the
    /// advice carries no digit, and every question an expert still owes an answer to keeps its marker.
    /// </summary>
    [Fact]
    public void The_pacing_advice_carries_no_figure_and_the_expert_markers_survive()
    {
        var pacing = Pacing();
        var digit = Regex.Match(pacing, @"\d");
        Assert.False(digit.Success, $"The pacing advice carries the figure '{digit.Value}': {pacing}");

        var markers = Regex.Matches(Text(), Regex.Escape("TODO(expert)")).Count;
        Assert.True(markers == 7, $"linkedin-guardrails carries {markers} TODO(expert) markers, and seven questions are still open.");
    }

    private static int NoteBound()
    {
        var document = JsonNode.Parse(File.ReadAllText(Path.Combine(
            SkillPack.RepositoryRoot(), "docs", "contracts", "operations", "social_invitation.send.json")))!;
        return document["input_schema"]!["properties"]!["args"]!["properties"]!["note"]!["maxLength"]!.GetValue<int>();
    }

    /// <summary>The step that paces the account, from its bold title to the step after it.</summary>
    private static string Pacing()
    {
        var skill = Flat(Text());
        var start = skill.IndexOf("**Pace like a person.**", StringComparison.Ordinal);
        var end = skill.IndexOf("9. **Watch acceptance.**", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"linkedin-guardrails has no pacing step before its acceptance step ({start}, {end}).");
        return skill[start..end];
    }

    private static string Text() =>
        File.ReadAllText(SkillPack.Business().Single(skill => skill.Name == "linkedin-guardrails").File)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Flat(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
