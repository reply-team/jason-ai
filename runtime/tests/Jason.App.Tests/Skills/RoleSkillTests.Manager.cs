namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    /// <summary>The catalog says what the manager now reads in a noticed reply or provider-event line, so a reader choosing a role learns it before opening the file.</summary>
    [Fact]
    public void The_catalog_says_that_the_manager_reads_what_a_noticed_reply_asks_and_whose_thread_it_is()
    {
        var catalog = Whitespace().Replace(File.ReadAllText(SkillPack.Catalog()), " ");
        var start = catalog.IndexOf("- [`roles/manager`]", StringComparison.Ordinal);
        Assert.True(start >= 0, "The catalog has no line for 'manager'.");
        var end = catalog.IndexOf(" - [`", start + 1, StringComparison.Ordinal);
        var line = end > start ? catalog[start..end] : catalog[start..];

        Assert.Contains("what a noticed reply or provider-event line asks of it and whose thread it is", line, StringComparison.Ordinal);
    }

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

    /// <summary>
    /// A line found by the person may name a thread this campaign did not start: the manager reads it before it raises
    /// the responder, and never answers a thread the outreach of another campaign that is not archived owns.
    /// </summary>
    [Fact]
    public void The_manager_reads_a_thread_found_by_the_person_before_it_answers()
    {
        var skill = Flattened("manager");

        Assert.Contains("under `attributed_by`", skill, StringComparison.Ordinal);
        Assert.Contains("`contact_pin` is the person alone", skill, StringComparison.Ordinal);
        Assert.Contains("Raise the responder at your next review, once the read shows whose thread it is;", skill, StringComparison.Ordinal);
        Assert.Contains("a thread the outreach of another campaign that is not archived owns is that campaign's to answer, never yours.", skill, StringComparison.Ordinal);
        Assert.Contains("or the read alone where the line says `attributed_by` `contact_pin`", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The read of a thread found by the person follows this campaign's route, which may lead elsewhere than the plugin the
    /// line came from: it is queued only where the two agree, and otherwise the manager asks.
    /// </summary>
    [Fact]
    public void The_manager_reads_a_thread_found_by_the_person_only_at_the_line_s_plugin()
    {
        var skill = Flattened("manager");

        Assert.Contains("Then queue the read alone, and only where this campaign reads conversations at the plugin the line names under `plugin`", skill, StringComparison.Ordinal);
        Assert.Contains("jason route resolve --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --operation conversation.get", skill, StringComparison.Ordinal);
        Assert.Contains("Where it names another plugin, or none, queue nothing, say so in your note and raise one question instead.", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A thread no campaign's outreach owns reaches every campaign that is not archived the person is in, and one answer is all the person
    /// should get: before raising the responder the manager reads the person's chronicle across campaigns, and leaves the
    /// thread to a campaign that already answers it.
    /// </summary>
    [Fact]
    public void The_manager_leaves_a_thread_no_campaign_owns_to_a_campaign_already_answering_it()
    {
        var skill = Flattened("manager");

        Assert.Contains("before you raise the responder for it, read the person's chronicle across campaigns", skill, StringComparison.Ordinal);
        Assert.Contains("jason journal list --contact cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", skill, StringComparison.Ordinal);
        Assert.Contains(
            "Where another campaign that is not archived has already raised the responder or replied in that thread, leave the thread to it and say so in your note; otherwise raise the responder.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A line whose <c>contact_id</c> is null names nobody, so the responder's item names nobody either: the responder
    /// finds the person through the read, and where it cannot, or the read stops short of the latest message, the question
    /// is the manager's to raise.
    /// </summary>
    [Fact]
    public void The_manager_raises_the_responder_without_a_person_when_the_line_names_none()
    {
        var skill = Flattened("manager");

        Assert.Contains(
            "Where the line's `contact_id` is null, nobody is pinned to the thread: create the responder's item without `--contact`, and the responder finds the person through the read.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "Where it finds none, or more than one, it drafts no reply and says who wrote, and you raise one question at your next review.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "The same holds where the read does not show the thread's latest message: the responder drafts no reply and says so, and the question is yours.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A waiting item is work the runtime will start again by itself, so a review that took it for stale work and raised
    /// it once more would run the step twice; and the wait is ended early on a person's word, never on the manager's.
    /// </summary>
    [Fact]
    public void The_manager_does_not_take_a_waiting_item_for_stale_work()
    {
        var skill = Flattened("manager");

        var stale = skill.IndexOf("**Stale and blocked work.**", StringComparison.Ordinal);
        var waiting = skill.IndexOf("**A `waiting` item is not stale.**", StringComparison.Ordinal);
        var approvals = skill.IndexOf("**Pending approvals and pending decisions.**", StringComparison.Ordinal);
        Assert.True(
            stale >= 0 && waiting > stale && approvals > waiting,
            $"Stale work opens at {stale}, the waiting item at {waiting} and approvals at {approvals}: the waiting item belongs inside stale work.");

        Assert.Contains("Do not create it, or the work it waits for, a second time", skill, StringComparison.Ordinal);
        Assert.Contains("A wait is ended early only by a person, who resumes it under their own name with `jason workitem resume`, never by you", skill, StringComparison.Ordinal);
        Assert.Contains(
            "jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --status waiting",
            SkillPack.PrintedCommands(File.ReadAllLines(SkillPack.Find("manager").File)));
    }
}
