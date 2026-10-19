namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_manager_is_told_which_items_run_without_anybody_and_proposes_the_rest()
    {
        var skill = Flattened("manager");

        // Two items run without waiting for anybody, because their not running is the harm. A manager told there was
        // one would treat the record of a stop as something to hold for a decision.
        Assert.Contains("Two items run even while Jason's campaign is paused, and nobody approves them, because their not running is the harm.", skill, StringComparison.Ordinal);
        Assert.Contains("`optout.record`", skill, StringComparison.Ordinal);

        // An incident pause is created, Jason's campaign paused, a person asked, and the run ended: a campaign runs one
        // item at a time, so the provider's pause is claimed only after the run that created it.
        Assert.Contains("create the provider's pause with a high `--priority`", skill, StringComparison.Ordinal);
        Assert.Contains("and then end the run", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("Do both, the provider's first", skill, StringComparison.Ordinal);

        // A population that moved is a new decision, and never an edit of the old one.
        Assert.Contains("Read it again with `enrollment.list` and create a new `campaign.activate` for the new number.", skill, StringComparison.Ordinal);

        // Taking a person out of a campaign is a decision for a person, which the manager prepares and never makes.
        Assert.Contains("`enrollment.stop`", skill, StringComparison.Ordinal);
        Assert.Contains("You do not approve it. You created it; a person decides it.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The campaign context is what a role reads when its skill has it read the campaign, and not every role does: the
    /// launch envelope carries the work item's own context and nothing else. A manager told that every role reads it would
    /// leave a standing instruction there for a role that never sees it, rather than in that role's own work item.
    /// </summary>
    [Fact]
    public void The_manager_is_told_that_not_every_role_reads_the_campaign_context()
    {
        var skill = Flattened("manager");

        Assert.Contains(
            "**The campaign context is the shared knowledge.** A role whose skill reads the campaign reads it whole; one that does not works from its own work item's context alone, so work you create for it carries what it needs. It is also where a person writes when they want the campaign to behave differently:",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("It is what every role reads", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A reply wakes the manager with a line that names a thread and says nothing of what was written. The manager is
    /// told to read every such line by its kind, to read the thread before deciding, and to leave the runtime's own read
    /// to the runtime.
    /// </summary>
    [Fact]
    public void The_manager_woken_by_a_reply_reads_the_lines_and_the_thread_and_leaves_the_read_to_the_runtime()
    {
        var skill = Flattened("manager");

        Assert.Contains("`trigger` is `inbound_reply_observed`", skill, StringComparison.Ordinal);
        Assert.Contains("--kind inbound_reply_observed --since", skill, StringComparison.Ordinal);
        Assert.Contains("--operation conversation.get", skill, StringComparison.Ordinal);
        Assert.Contains("--role responder", skill, StringComparison.Ordinal);

        // Not in its own run: an item it creates is claimed only after the run ends, the read before the responder, and
        // what they found is read at the next review.
        Assert.Contains("and **not in this run**", skill, StringComparison.Ordinal);
        Assert.Contains("--context '{\"thread\":\"501\",\"read\":", skill, StringComparison.Ordinal);
        Assert.Contains("decide at your next review", skill, StringComparison.Ordinal);
        Assert.True(
            skill.IndexOf("--operation conversation.get", StringComparison.Ordinal) < skill.IndexOf("--role responder", StringComparison.Ordinal),
            "the read is queued before the responder");
        Assert.Contains("**The read itself is not yours to redo.**", skill, StringComparison.Ordinal);
        Assert.Contains("made by `system:dispatcher`", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a reply Jason sends moves its thread is the provider's matter. Where it does, inbound sync says that move as
    /// it says an answer, and the manager is told how to recognise its own reply — the reply item's result, and the moment
    /// the line names — and that nothing happened then, so it queues neither a read nor a responder for a message it sent
    /// itself. Where it does not, no line comes for that reply at all.
    /// </summary>
    [Fact]
    public void The_manager_woken_by_its_own_reply_is_told_nothing_happened()
    {
        var skill = Flattened("manager");

        Assert.Contains(
            "Whether the provider's list moves a thread for a reply Jason sends is the provider's matter, and its plugin's README says which. Where it does, the line says it like any other:",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("Where it does not, no line comes for Jason's own reply, and nothing wakes you for it.", skill, StringComparison.Ordinal);
        Assert.Contains(
            "when the thread's latest message is Jason's own — a `conversation.reply` item of this campaign succeeded for that thread, and the line's `last_activity_at` is not after the `sent_at` its result names — nothing happened, and there is nothing to read or answer. Without a `sent_at` in that result, the read tells.",
            skill,
            StringComparison.Ordinal);
    }
}
