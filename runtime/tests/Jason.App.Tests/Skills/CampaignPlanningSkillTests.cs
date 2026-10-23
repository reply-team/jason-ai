using System.Text.RegularExpressions;

namespace Jason.App.Tests.Skills;

/// <summary>
/// What <c>campaign-planning</c> says that this runtime makes true: where the person's brief lands, and the path a plan
/// often takes for an audience nobody has tried. The pack's own guards hold its shape; these hold two sentences a session
/// plans by, which an edit could otherwise remove with every test green.
/// </summary>
public sealed partial class CampaignPlanningSkillTests
{
    /// <summary>
    /// With Jason installed a host is handed the same brief by two skills, and the one that works in the conversation and
    /// hands the work to Jason's roles has to win it. So the description says so, and still says what it says for a
    /// session with no Jason: a description that dropped either half would send the one session or the other to the wrong
    /// skill.
    /// </summary>
    [Fact]
    public void The_description_defers_to_the_planner_in_a_session_when_Jason_is_installed()
    {
        var description = SkillFrontMatter.Read(SkillPack.Business().Single(skill => skill.Name == "campaign-planning").File).Top.GetValueOrDefault("description") ?? string.Empty;

        Assert.Equal(
            "Turn a vague outbound goal into an executable plan: clarify the objective until success is observable, surface the constraints that shape it, compose the work from known operations and playbooks, and decide the checkpoints. Use when the user states a business goal — book meetings, launch a campaign, grow pipeline — rather than a single concrete action. With Jason installed, `planner-in-session` leads the planning and uses this skill as its method, so load that one first.",
            description);
    }

    /// <summary>
    /// The brief is taken as the person gives it and becomes the context of Jason's own campaign when it is created,
    /// before any contact exists and before anything is sent, so the plan and the roles that read the campaign start from
    /// the same brief — those roles, not every role: two of them work from their own work item's context alone. The verb
    /// is named apart from the provider operation of the same name. A plan that added the people first, or kept the brief
    /// in a conversation that ends, would start the work from less than the person gave.
    /// </summary>
    [Fact]
    public void The_brief_becomes_Jason_s_campaign_before_any_contact_exists()
    {
        var skill = Flat(Text());

        Assert.Contains("Take it as they give it: in their own words, typed, as a link, a file or a voice note.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "With Jason, that brief becomes the context of Jason's own campaign when the campaign is created (`jason campaign create`, not the provider operation `campaign.create`), under a project named from what is sold, before any contact is added and before anything is sent, so the plan and the roles that read the campaign start from the same brief.",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("every role that works it", skill, StringComparison.Ordinal);

        // The changelog says when the project came into the prerequisite, so a reader of an older copy knows what changed.
        Assert.Contains("- 2.4.0 (2026-10-23): with Jason, the campaign the brief becomes goes under a project named from what is sold, one project per product.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A pilot, then the rest, is one natural path and never the workflow: a quick look, a researcher's deeper check, the
    /// data pulled and stored, deep work on a pilot of about ten, outreach on the pilot, a hold, the first results read and
    /// the approach reworked, then the remainder — in that order. The planner decides each turn and the manager keeps the
    /// work alive. A text that made it the only path would turn planning craft into a script, and one that lost the
    /// order would send outreach before anything was learned.
    /// </summary>
    [Fact]
    public void Pilot_then_rest_is_one_natural_path_and_never_the_workflow()
    {
        const string Opening = "**One natural path: a pilot, then the rest.**";
        var text = Text();

        var validate = text.IndexOf("6. **Validate the plan before any of it runs.**", StringComparison.Ordinal);
        var start = text.IndexOf(Opening, StringComparison.Ordinal);
        var stops = text.IndexOf("**Where the contract stops,", StringComparison.Ordinal);
        Assert.True(
            validate >= 0 && start > validate && stops > start,
            $"The pilot-then-rest paragraph is at offset {start}; it belongs after step 6 (offset {validate}) and before \"Where the contract stops\" (offset {stops}).");

        var end = text.IndexOf("\n\n", start, StringComparison.Ordinal);
        var paragraph = Flat(text[start..(end > start ? end : text.Length)]);

        Assert.StartsWith(
            Opening + " It is one of the natural paths a plan can take, not a workflow, and not the only one: the conversation with the person may well find another.",
            paragraph,
            StringComparison.Ordinal);

        string[] turns =
        [
            "a quick look at whether there is anything there at all",
            "a researcher confirming the direction more deeply",
            "the data pulled and stored",
            "deep work per contact on a pilot of about ten",
            "outreach on the pilot",
            "; a hold;",
            "the first results read and the approach reworked",
            "then the remainder",
        ];
        var at = 0;
        foreach (var turn in turns)
        {
            var found = paragraph.IndexOf(turn, at, StringComparison.Ordinal);
            Assert.True(found >= 0, $"The pilot-then-rest paragraph does not name \"{turn}\" after offset {at}: {paragraph}");
            at = found + turn.Length;
        }

        Assert.Contains(
            "At each turn the planner decides — go on, change course, or take it back to the person — and the manager keeps the work alive, so that nothing hangs and no hypothesis is left abandoned.",
            paragraph,
            StringComparison.Ordinal);

        // The quick look happens in the conversation, before anything is committed; what follows it is the campaign's
        // work. A text that put the quick look under the campaign too would have a session create work for a hunch.
        Assert.Contains(
            "With Jason the turns after the quick look are work under the campaign, and the planner and the manager are its roles.",
            paragraph,
            StringComparison.Ordinal);
    }

    private static string Text() =>
        File.ReadAllText(SkillPack.Business().Single(skill => skill.Name == "campaign-planning").File)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string Flat(string text) => Whitespace().Replace(text, " ");

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
