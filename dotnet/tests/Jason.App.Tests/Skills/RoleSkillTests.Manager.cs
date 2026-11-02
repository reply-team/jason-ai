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
            skill.IndexOf("jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind provider_op --operation conversation.get", StringComparison.Ordinal) < skill.IndexOf("jason workitem create cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role responder", StringComparison.Ordinal),
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

    /// <summary>
    /// A block summons the manager because what reaches people stopped: it says what stopped and why, and decides with the
    /// person whether to pause or wait for the trial. Clearing the block is the person's word that the cause is gone, never
    /// the manager's; and the line that says it cleared wakes nobody, because the work runs again by itself.
    /// </summary>
    [Fact]
    public void The_manager_woken_by_a_block_says_what_stopped_and_never_clears_it()
    {
        var skill = Flattened("manager");

        var events = skill.IndexOf("## When a provider event woke you", StringComparison.Ordinal);
        var block = skill.IndexOf("## When a provider block woke you", StringComparison.Ordinal);
        var boundary = skill.IndexOf("## What you may decide yourself", StringComparison.Ordinal);
        Assert.True(
            events >= 0 && block > events && boundary > block,
            $"Provider events open at {events}, the block at {block} and the boundary at {boundary}: the block comes between them.");
        Assert.Contains("`trigger` is `provider_blocked` when the provider refused the account, or one sending account under it", skill, StringComparison.Ordinal);
        Assert.Contains("it never carries the link", skill, StringComparison.Ordinal);
        Assert.Contains("In your review, say what stopped and why", skill, StringComparison.Ordinal);
        Assert.Contains("decide with the person, by one question, whether to pause the campaign or wait for the block's trial", skill, StringComparison.Ordinal);
        Assert.Contains("**You never clear a block yourself**: a clear is a person's word that the cause is gone, and it is theirs to give.", skill, StringComparison.Ordinal);
        Assert.Contains("the campaign is told `provider_block_cleared`, and that line wakes nobody", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A capability the provider does not offer yet is the manager's to work around, never to repeat: the skill names the
    /// code, and for each capability a provider may lack for a while it says what to do instead — the harness's own moment
    /// for a send the provider cannot defer, the runtime's own walk of the pages for a feed of inbound events, and what the
    /// provider said when it took a message for a status it cannot be asked.
    /// </summary>
    [Fact]
    public void The_manager_takes_a_capability_the_provider_does_not_offer_yet_as_final()
    {
        var skill = Flattened("manager");

        Assert.Contains("`capability_not_offered` says the provider does not offer an operation, or one argument of it, yet", skill, StringComparison.Ordinal);
        Assert.Contains("The code is final: it is nobody's configuration and no argument a corrected item could fix, and no repeat of the same item gets past it.", skill, StringComparison.Ordinal);
        Assert.Contains("Choose another way to do the work, or tell the person it cannot be done yet, and say which in your line.", skill, StringComparison.Ordinal);
        Assert.Contains("is stated in the list of capabilities the contracts README (`docs/contracts/README.md`) keeps", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("create the message again as a new item without the moment", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("the runtime's own walk of the conversation pages", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("as the fact to keep: never send it again to learn more", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A send ends when the provider has taken it, and an older plugin says so as `sent` where a newer one says
    /// `accepted`. The manager reads either as the item done and never sends again to learn whether it went.
    /// </summary>
    [Fact]
    public void The_manager_reads_a_send_sent_or_accepted_as_done()
    {
        var skill = Flattened("manager");

        Assert.Contains(
            "**A send is done when the provider has taken it.** A `message.send` or a `social_invitation.send` that succeeds `sent` or `accepted` is done: `accepted` says the provider took it, for now or for a later moment, and never that it went.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "Whether it went arrives later, where the provider tells it, and never from that item; do not send it again to make sure.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// One send is read or taken back by naming the work item that made it, never its reference, and a refusal of the
    /// send named is final. The line that says a send taken for later went wakes the manager only where the installation
    /// lists it, names the send and the read and never the text, and calls for nothing by itself.
    /// </summary>
    [Fact]
    public void The_manager_names_a_send_by_its_work_item_and_is_told_one_went_only_where_it_asked()
    {
        var skill = Flattened("manager");

        var events = skill.IndexOf("## When a provider event woke you", StringComparison.Ordinal);
        var went = skill.IndexOf("## When a send that went woke you", StringComparison.Ordinal);
        var block = skill.IndexOf("## When a provider block woke you", StringComparison.Ordinal);
        Assert.True(
            events >= 0 && went > events && block > went,
            $"Provider events open at {events}, a send that went at {went} and the block at {block}: it comes between them.");
        Assert.Contains("A `message.status` item names the send's work item under `args.send`", skill, StringComparison.Ordinal);
        Assert.Contains("you never write the reference, the runtime takes it from the send's own attempt", skill, StringComparison.Ordinal);
        Assert.Contains("ends `send_not_pending`, final, its details saying which", skill, StringComparison.Ordinal);
        Assert.Contains("Either ends `send_unknown`, `send_reference_absent` or `send_route_mismatch`, final", skill, StringComparison.Ordinal);
        Assert.Contains("`trigger` is `message_sent_observed` only where this installation lists it under `Manager:Triggers`: it is not a default one.", skill, StringComparison.Ordinal);
        Assert.Contains("never the text that went", skill, StringComparison.Ordinal);
        Assert.Contains("this line is the only place the runtime says it went", skill, StringComparison.Ordinal);
        Assert.Contains("by itself it calls for nothing", skill, StringComparison.Ordinal);
        Assert.Contains(
            "jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind message_sent_observed --since 2026-09-18T09:00:00Z",
            SkillPack.PrintedCommands(File.ReadAllLines(SkillPack.Find("manager").File)));
    }

    /// <summary>
    /// A read of one send answers one of five words, and the manager is told the two final ones that are not a send that
    /// went apart: a failed send did not go, an unknown one may have. Whether it went is learned from the person's thread,
    /// found and read before any new send to them, never by sending again, and the note says what the thread showed.
    /// </summary>
    [Fact]
    public void The_manager_reads_the_thread_before_sending_again_after_a_send_that_may_have_gone()
    {
        var skill = Flattened("manager");

        Assert.Contains("answers one of five words: `queued`, `sent`, `cancelled`, `failed` or `unknown`", skill, StringComparison.Ordinal);
        Assert.Contains("A `failed` send did not go; an `unknown` one may have", skill, StringComparison.Ordinal);
        Assert.Contains("Never send again on `unknown` alone", skill, StringComparison.Ordinal);
        Assert.Contains("before any new send to that person, find their thread with a `conversation.list` item for them, then read it with a `conversation.get` item", skill, StringComparison.Ordinal);
        Assert.Contains("say in the note what the thread showed", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A send for a later moment that the provider cannot hold has a way around it: the same send without the moment,
    /// created to be claimed only once the moment has passed. The manager is told that the person still approves it then,
    /// and that a machine asleep at the moment claims nothing, so it never promises a send at an exact time. Where the
    /// refusal is ambiguous, an earlier attempt of the item may have reached the person, so the new item waits until the
    /// provider has said it did not.
    /// </summary>
    [Fact]
    public void The_manager_is_told_how_to_send_at_a_moment_the_provider_cannot_hold()
    {
        var skill = Flattened("manager");

        Assert.Contains(
            "**A send for a later moment.** Where a `message.send` or a `social_invitation.send` that names a moment in `send_after` ends `capability_not_offered` or `argument_unsupported`, you may create a new item without it, with `not_before` set to the moment, unless the failure is `ambiguous`: then an earlier attempt of that item may have reached the person, and a new item is created only once the provider has said it did not.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "It is claimed once the moment has passed, and both sends are `confirm_each`, so it then waits for a person's approval; nothing goes out before they give it. The machine has to be awake for the item to be claimed at all.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A message composed as a template on a channel the routed plugin does not write a template on is refused at the claim,
    /// and the refusal has two ways round it, beside the one for a moment the provider cannot hold: the same message as final
    /// text, held back as that one is while an earlier attempt may have reached the person, or a person's route to a plugin
    /// that writes a template there, which the manager asks for rather than makes.
    /// </summary>
    [Fact]
    public void The_manager_is_told_what_to_do_with_a_template_the_plugin_does_not_write()
    {
        var skill = Flattened("manager");

        Assert.Contains(
            "**A template the plugin does not write.** Where a `message.send` composed as a template ends `template_not_offered`, the plugin it is routed to writes no template on that channel. You may create a new item with the message written as final text, unless the failure is `ambiguous`: then an earlier attempt of that item may have reached the person, and a new item is created only once the provider has said it did not. Or raise one question asking a person to route the operation to a plugin that writes a template there: a route is somebody's configuration.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The runtime raises the responder for a reply by itself, so a manager that created one as well would leave the
    /// campaign holding two answers to one reply. It looks for the runtime's item first, defers to an open one that
    /// names the line, reads the result of one that succeeded, and keeps its own path only for a line nobody answers.
    /// </summary>
    [Fact]
    public void The_manager_defers_to_the_responder_the_runtime_raised_for_the_line()
    {
        var skill = Flattened("manager");

        Assert.Contains("**The runtime has usually raised the responder already.**", skill, StringComparison.Ordinal);
        Assert.Contains("`inbound_reply.moves` names each line it answers by the line's `jrn_` id", skill, StringComparison.Ordinal);
        Assert.Contains("jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind ai_role --role responder --order finished", skill, StringComparison.Ordinal);
        Assert.Contains(
            "**An open responder whose `inbound_reply.moves` names the line:** you defer to it. Create no read and no responder for that line",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "If it succeeded, read its result (`outcome`, `verdict`, `created_work_items`) and never draft or act again on that line",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("If it failed, was cancelled or expired, the failed inbox above and the text below apply.", skill, StringComparison.Ordinal);
        Assert.Contains("**No responder names the line.** The rule left it to you", skill, StringComparison.Ordinal);
        Assert.Contains("Where no responder names the line, queue the read", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The result fields the manager is told to read are the ones the runtime asks the responder for, so the skill cannot
    /// name a field the responder is never asked to give.
    /// </summary>
    [Fact]
    public void The_result_fields_the_manager_reads_are_the_ones_the_responder_is_asked_for()
    {
        var format = Jason.Runtime.Dispatch.Responder.ResultFormat["properties"]!.AsObject();

        foreach (var field in new[] { "outcome", "verdict", "created_work_items" })
        {
            Assert.True(format.ContainsKey(field), $"The responder's result format has no '{field}'.");
        }
    }

    /// <summary>
    /// The check-in reads the verdicts by category through the journal listing by kind, counts the latest line of a
    /// reply, and sends the reader to the responder's data file for the categories rather than copying them.
    /// </summary>
    [Fact]
    public void The_manager_reads_the_verdict_lines_by_category_since_its_last_review()
    {
        var skill = Flattened("manager");

        Assert.Contains("**What the responder concluded about replies.**", skill, StringComparison.Ordinal);
        Assert.Contains("jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind reply_classified --since", skill, StringComparison.Ordinal);
        Assert.Contains("count them by category; where one reply has several lines, the latest stands.", skill, StringComparison.Ordinal);
        Assert.Contains("The base categories are the responder's `categories.json`, which is not copied here", skill, StringComparison.Ordinal);
        Assert.Contains("A reaction of `draft` or `escalate` is something waiting for a person", skill, StringComparison.Ordinal);
        Assert.Contains("You record no verdict of your own reading", skill, StringComparison.Ordinal);
    }

    /// <summary>The line the skill reads is the one the runtime writes: its kind and the fields the skill names are the journal's.</summary>
    [Fact]
    public void The_verdict_line_the_manager_reads_is_the_kind_the_runtime_writes()
    {
        Assert.Equal("reply_classified", Jason.Runtime.Journal.JournalKinds.ReplyClassified);
    }

    /// <summary>
    /// A bounce is read at the review as a count and never filed as a refusal, the two notes the responder leaves for a
    /// later moment are read at the review, and the guardrails say when the count is a problem. The ids are the ones the
    /// runtime's categories use.
    /// </summary>
    [Fact]
    public void The_manager_reads_bounces_as_a_count_and_the_notes_of_the_replies_that_wait_for_a_moment()
    {
        var skill = Flattened("manager");

        Assert.Contains("A `bounce` is a deliverability fact and no refusal: the responder files no register entry for it and raises no question per bounce", skill, StringComparison.Ordinal);
        Assert.Contains("under `bounces` in its role note for the campaign. The count of them is yours at this review, from the `reply_classified` lines and that note, and the business pack's `sending-guardrails` skill says when it is a problem.", skill, StringComparison.Ordinal);
        Assert.Contains("A `not_now` and an `out_of_office` leave a note with the moment the person named under `returns` in the same role note, and no draft, so read both at this review", skill, StringComparison.Ordinal);
        Assert.Contains("jason rolenote get --role responder --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", SkillPack.PrintedCommands(SkillPack.Find("manager").File));
        Assert.True(File.Exists(Path.Combine(SkillPack.BusinessRoot(), "sending-guardrails", SkillPack.SkillFile)));
    }

    /// <summary>
    /// Below its threshold the responder records a guess and asks; the person's answer is recorded by the manager as a
    /// second verdict for the same reply, whose identifiers come from the first verdict line and not from the question.
    /// The printed command names every field the verb requires, so the second one meets the first.
    /// </summary>
    [Fact]
    public void The_manager_records_a_persons_answer_to_a_question_of_the_responder_as_a_second_verdict()
    {
        var skill = Flattened("manager");

        Assert.Contains("The one you do record is a person's answer.", skill, StringComparison.Ordinal);
        Assert.Contains("raises a question whose options are the category ids, and drafts nothing; acting on the answer is yours, at the answered question's line.", skill, StringComparison.Ordinal);
        Assert.Contains("from the first verdict line, never from the question", skill, StringComparison.Ordinal);
        Assert.Contains("as a second verdict for the same reply, which supersedes the first", skill, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("manager").File).Single(line => line.StartsWith("jason reply record ", StringComparison.Ordinal));
        foreach (var option in new[] { "--plugin", "--thread", "--message", "--message-at", "--category", "--confidence", "--reaction" })
        {
            Assert.Contains(option + " ", printed, StringComparison.Ordinal);
        }
    }
}
