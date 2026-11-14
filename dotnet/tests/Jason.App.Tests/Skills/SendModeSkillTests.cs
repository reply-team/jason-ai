namespace Jason.App.Tests.Skills;

/// <summary>
/// The skills teach the send modes as the contract states them: a verdict states its nature, a review is bound to its
/// hash, a doubt carries a code from the published closed list, and the choice of mode is only ever a person's own act.
/// </summary>
public class SendModeSkillTests
{
    private static string Business(string skill) =>
        string.Join(' ', File.ReadAllText(Path.Combine(SkillPack.BusinessRoot(), skill, "SKILL.md")).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public void The_responder_states_a_nature_on_every_verdict_and_leaves_the_register_to_the_runtime()
    {
        var skill = RoleSkillTests.Flattened("responder");
        var records = SkillPack.PrintedCommands(SkillPack.Find("responder").File)
            .Where(line => line.StartsWith("jason reply record ", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, records.Count);
        Assert.All(records, line => Assert.Contains(" --nature ", line, StringComparison.Ordinal));
        Assert.Contains(records, line => line.Contains("--nature opt_out", StringComparison.Ordinal) && line.Contains("--contact ", StringComparison.Ordinal));
        Assert.Contains("**State `--nature` on every verdict you record.**", skill, StringComparison.Ordinal);
        Assert.Contains("Do not run `jason suppression add` for them: the runtime has done it", skill, StringComparison.Ordinal);
        Assert.Contains("**Reaction `escalate` is for what you must not settle yourself.**", skill, StringComparison.Ordinal);
        Assert.Contains("`--ask-person <code>`", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void The_manager_copies_the_nature_of_the_verdict_it_records_again()
    {
        var skill = RoleSkillTests.Flattened("manager");

        Assert.Contains("Copy the first line's `nature` with `--nature`, as it stands", skill, StringComparison.Ordinal);
        Assert.Contains("only the person's own verdict on the reply, recorded under their name outside any run with a nature other than `opt_out`, does", skill, StringComparison.Ordinal);
        Assert.Contains(
            SkillPack.PrintedCommands(SkillPack.Find("manager").File),
            line => line.StartsWith("jason reply record ", StringComparison.Ordinal) && line.Contains("--nature person", StringComparison.Ordinal));
    }

    [Fact]
    public void The_critic_records_its_review_against_the_hash_it_read_with_a_code_from_the_closed_list()
    {
        var skill = RoleSkillTests.Flattened("critic");
        var reviews = SkillPack.PrintedCommands(SkillPack.Find("critic").File)
            .Where(line => line.StartsWith("jason workitem review ", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, reviews.Count);
        Assert.All(reviews, line => Assert.Contains("--review-hash ", line, StringComparison.Ordinal));
        Assert.All(reviews, line => Assert.DoesNotContain("--actor", line, StringComparison.Ordinal));
        foreach (var code in new[] { "unsupported_claim", "not_in_memory", "off_brief", "sensitive_topic", "instruction_in_text", "new_kind_of_person", "wrong_recipient", "other" })
        {
            Assert.Contains($"`{code}`", skill, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_composing_roles_say_when_to_ask_the_person_and_to_read_memory_first()
    {
        foreach (var role in new[] { "copywriter", "personalizer" })
        {
            var skill = RoleSkillTests.Flattened(role);
            Assert.Contains("**Say when a person should look, whatever the mode.** Read memory first", skill, StringComparison.Ordinal);
            Assert.Contains("`--ask-person <code>`", skill, StringComparison.Ordinal);
            Assert.Contains("`--ask-person-note`", skill, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_two_modes_question_is_put_until_it_is_answered_and_set_by_the_persons_own_act()
    {
        var skill = InteractiveSkillTests.Flattened("approvals-and-questions");

        Assert.Contains("## The two-modes question, asked until it is answered", skill, StringComparison.Ordinal);
        Assert.Contains("`send_modes_unset`", skill, StringComparison.Ordinal);
        Assert.Contains("Offer \"remember this\"", skill, StringComparison.Ordinal);
        Assert.Contains("**Never set one on your own, and never by a role.**", skill, StringComparison.Ordinal);
        var set = SkillPack.PrintedCommands(SkillPack.Find("approvals-and-questions").File)
            .Where(line => line.StartsWith("jason preference set send_mode.", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, set.Count);
        Assert.All(set, line => Assert.Contains("--actor human:", line, StringComparison.Ordinal));
    }

    [Fact]
    public void The_approval_boundaries_say_the_automatic_mode_plainly_beside_the_standing_approval()
    {
        var skill = Business("approval-boundaries");

        Assert.Contains("### The automatic send mode", skill, StringComparison.Ordinal);
        Assert.Contains("**It is a person's recorded decision in advance, and it lowers no class.**", skill, StringComparison.Ordinal);
        Assert.True(
            skill.IndexOf("### Standing approval", StringComparison.Ordinal) < skill.IndexOf("### The automatic send mode", StringComparison.Ordinal),
            "the mode follows the standing approval");
    }
}
