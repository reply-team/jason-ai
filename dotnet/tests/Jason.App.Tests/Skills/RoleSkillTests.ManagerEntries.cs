namespace Jason.App.Tests.Skills;

/// <summary>
/// The manager's head and its five entries, as texts: what the head says about the entry a run was launched for, and
/// what each entry teaches that the head no longer does.
/// </summary>
public partial class RoleSkillTests
{
    /// <summary>
    /// The envelope's entry names why the run was launched and carries the text it follows first; a null entry, or one
    /// with no text, is the general review. Every other cause keeps its section in the head.
    /// </summary>
    [Fact]
    public void The_manager_s_head_says_the_entry_is_followed_first_and_the_general_review_stands_without_one()
    {
        var head = Flattened("manager");

        Assert.Contains("**The entry you were launched for.** The envelope's `entry` names why you were launched and carries that entry's text as `entry.text`, which you follow first", head, StringComparison.Ordinal);
        Assert.Contains("Where `entry` is null, or names an entry with no text, you review as \"The review\" below says.", head, StringComparison.Ordinal);
        foreach (var entry in Jason.Contracts.Execution.RoleEntries.Of("manager"))
        {
            Assert.Contains($"- `{entry}` — ", head, StringComparison.Ordinal);
        }

        Assert.Contains("Each entry is also a file beside this one, `entries/<name>.md`", head, StringComparison.Ordinal);
        foreach (var section in new[] { "**Failed items are your inbox.**", "## The questions a person has answered", "## When a provider event woke you", "## When a provider block woke you", "## When the audience woke you", "Reports are what happened to the campaign where the runtime could not see it" })
        {
            Assert.Contains(section, head, StringComparison.Ordinal);
        }

        // The reply's section left the head for its entry, and the head keeps none of it.
        Assert.DoesNotContain("## When a reply woke you", head, StringComparison.Ordinal);
        Assert.Contains("## When a reply woke you", FlattenedEntry("manager", "inbound_reply"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A review a person asked for is <c>requested</c>, names no cause, and is read from the person's own line, which the
    /// runtime writes under the kind and the verb this build has; the two review entries say what it means for each depth,
    /// and a deep one asked for leaves the daily grid where it stands.
    /// </summary>
    [Fact]
    public void The_manager_reads_a_review_a_person_asked_for_from_their_line()
    {
        var head = Flattened("manager");

        Assert.Contains("`review_intent` is `requested` when a person asked for this review now, with `jason campaign review`: it names no trigger and no cause, and their `review_requested` line in the chronicle says who asked, the depth they asked for and why.", head, StringComparison.Ordinal);
        Assert.Contains("A review is asked for by a person only; a run never asks for one.", head, StringComparison.Ordinal);
        Assert.Equal("review_requested", Jason.Runtime.Journal.JournalKinds.ReviewRequested);
        Assert.Equal("campaign.review", Jason.Contracts.Api.Operations.CampaignReview);

        Assert.Contains("where `review_intent` is `requested`, a person asked for a quick look now", FlattenedEntry("manager", "quick_look"), StringComparison.Ordinal);
        Assert.Contains("know that the daily one still comes at its own moment, which this review does not move", FlattenedEntry("manager", "deep_review"), StringComparison.Ordinal);
        Assert.DoesNotContain(
            SkillPack.Entries("manager").SelectMany(entry => SkillPack.PrintedCommands(entry.File)).Concat(SkillPack.PrintedCommands(SkillPack.Find("manager").File)),
            line => line.StartsWith("jason campaign review", StringComparison.Ordinal));
    }

    /// <summary>One manager, from one review to the next: the head says the note is where a review starts and never the truth.</summary>
    [Fact]
    public void The_manager_s_head_says_one_manager_starts_from_its_note_and_believes_the_runtime_over_it()
    {
        var head = Flattened("manager");

        Assert.Contains("You are one manager, from one review to the next.", head, StringComparison.Ordinal);
        Assert.Contains("A review that finds a recent one in the note covers only what changed since then, and says so", head, StringComparison.Ordinal);
        Assert.Contains("**The note is never the truth over the campaign's state:**", head, StringComparison.Ordinal);
        Assert.Contains("**The note is never the truth over the campaign's state**: it says where to look and since when, and the runtime says what is there.", head, StringComparison.Ordinal);
        Assert.Contains("write back every key you found, so that a quick look never drops what the deep review left", head, StringComparison.Ordinal);
    }

    /// <summary>
    /// A person's line, said once in the head with what this build has: three touches as the fallback, the ceiling the
    /// settings hold, a row's touches and the two states a line can be in.
    /// </summary>
    [Fact]
    public void The_manager_s_head_says_what_a_person_s_line_is_in_this_build()
    {
        var head = Flattened("manager");

        Assert.Contains("Three touches, the first message and two follow-ups, is the line where nothing better was thought out for the person.", head, StringComparison.Ordinal);
        Assert.Contains("never more than `Lines:MaxTouches`, the ceiling the settings hold: at it the claim refuses the next touch, `touch_ceiling_reached`", head, StringComparison.Ordinal);
        Assert.Contains("`touches`, and when the last was, `last_touch_at`", head, StringComparison.Ordinal);
        Assert.Contains("A line is `paused` once the person replied", head, StringComparison.Ordinal);
        Assert.Contains("It is `finished` once it was ended", head, StringComparison.Ordinal);
        Assert.Contains("**You never plan past the ceiling, and you never touch a setting.**", head, StringComparison.Ordinal);
        Assert.Contains("A day's limit is lifted only on the person's word, `jason launches lift` typed by them, never by you", head, StringComparison.Ordinal);
        Assert.Contains("**end a person's line** in this campaign, with a reason", head, StringComparison.Ordinal);
    }

    /// <summary>A line is reopened only on the person's yes, by the question they answered, and never on a reading of the manager's.</summary>
    [Fact]
    public void The_manager_reopens_a_line_only_on_the_person_s_yes()
    {
        var head = Flattened("manager");

        Assert.Contains("**A line is reopened only on the person's yes.** You never reopen an ended line on your own reading", head, StringComparison.Ordinal);
        var reopen = Assert.Single(
            SkillPack.PrintedCommands(SkillPack.Find("manager").File),
            line => line.StartsWith("jason campaign reopen-contacts ", StringComparison.Ordinal));
        Assert.Matches(@"--decision dec_[0-9A-Z]{26} ", reopen);
    }

    /// <summary>
    /// A campaign linked to a provider's sequence gets no letters of Jason's own: the head's review and the deep review
    /// check that none was put in by accident, stop one that was and ask the person, and nothing steers toward a sequence.
    /// </summary>
    [Fact]
    public void The_manager_checks_that_a_campaign_linked_to_a_sequence_gets_no_letters_of_jason_s_own()
    {
        var head = Flattened("manager");
        var deep = FlattenedEntry("manager", "deep_review");

        Assert.Contains("**A campaign linked to a provider's sequence gets no letters of Jason's own.**", head, StringComparison.Ordinal);
        foreach (var text in new[] { head, deep })
        {
            Assert.Contains("check", text, StringComparison.Ordinal);
            Assert.Contains("none of Jason's own letters were put in by accident: a `message.send` or a `social_invitation.send` about its people", text, StringComparison.Ordinal);
            Assert.Contains("or a `campaign.enroll` no person asked for", text, StringComparison.Ordinal);
            Assert.Contains("raise one question to the person saying what was put in", text, StringComparison.Ordinal);
        }

        Assert.Contains("## A campaign linked to a provider's sequence", deep, StringComparison.Ordinal);
        Assert.DoesNotContain(SkillPack.PrintedCommands(SkillPack.Find("manager").File), line => line.Contains("--operation campaign.enroll", StringComparison.Ordinal));
        Assert.DoesNotContain(SkillPack.Entries("manager").SelectMany(entry => SkillPack.PrintedCommands(entry.File)), line => line.Contains("--operation campaign.enroll", StringComparison.Ordinal));
    }

    /// <summary>
    /// The reserve's threshold is the campaign's, one batch of twenty set by the runtime at the first start; and where no
    /// top-up can bring more, the manager asks at once rather than at the next deep review.
    /// </summary>
    [Fact]
    public void The_manager_asks_at_once_where_no_top_up_can_bring_more_people()
    {
        var head = Flattened("manager");

        Assert.Contains("fell under the threshold the campaign holds — one batch, twenty, which the runtime sets at the campaign's first start where its people come from a search (`Manager:AudienceThreshold`), unless the person changed it", head, StringComparison.Ordinal);
        Assert.DoesNotContain("the threshold the person set", head, StringComparison.Ordinal);
        Assert.Equal(20, new Jason.Runtime.Configuration.ManagerOptions().AudienceThreshold);
        Assert.Contains("**Where no top-up can bring more, ask at once.**", head, StringComparison.Ordinal);
        Assert.Contains("raise one question to the person at this review, and do not leave it to the next deep review", head, StringComparison.Ordinal);
        Assert.Contains("Where a top-up you created parks for the person, its preview is that question, so raise none beside it.", head, StringComparison.Ordinal);
    }

    /// <summary>
    /// A campaign that starts with fewer untouched people than a batch has its low reserve in the same pass as its start,
    /// and the start's entry answers it once, by asking at once where no allowance covers a top-up.
    /// </summary>
    [Fact]
    public void The_start_entry_answers_a_low_reserve_read_with_the_start_once()
    {
        var start = FlattenedEntry("manager", "start");

        Assert.Contains("## A low reserve at the start", start, StringComparison.Ordinal);
        Assert.Contains("its `audience_reserve_low` line is read in the same pass as the start, so this review is the one that answers it", start, StringComparison.Ordinal);
        Assert.Contains("one question to the person at once, as \"Where no top-up can bring more\" says — never left to the next deep review", start, StringComparison.Ordinal);
        Assert.Contains("jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind audience_reserve_low --since 2026-09-18T09:00:00Z", SkillPack.PrintedCommands(EntryFile("manager", "start")));
    }

    /// <summary>
    /// A first start is moved by one planner item for the first batch, twenty or the person's number, and nothing is
    /// created twice; widening is the deep review's.
    /// </summary>
    [Fact]
    public void The_start_entry_hands_the_first_batch_to_the_planner_by_one_item()
    {
        var start = FlattenedEntry("manager", "start");

        Assert.Contains("**A first start means the campaign is set up and live, and moving it is yours.**", start, StringComparison.Ordinal);
        Assert.Contains("**The first batch is twenty people, or the number the person gave.**", start, StringComparison.Ordinal);
        Assert.Contains("it is the context's `first_batch`, a whole number", start, StringComparison.Ordinal);
        Assert.Contains("Create nothing twice", start, StringComparison.Ordinal);
        Assert.Contains("Widening to the next batch is the deep review's too", start, StringComparison.Ordinal);
        var planner = Assert.Single(
            SkillPack.PrintedCommands(EntryFile("manager", "start")),
            line => line.StartsWith("jason workitem create ", StringComparison.Ordinal));
        Assert.Contains("--role planner ", planner, StringComparison.Ordinal);
        Assert.Contains("\"batch\":20", planner, StringComparison.Ordinal);
    }

    /// <summary>A start after a pause is not a new campaign: the lines go on, and a first batch only where nobody was touched.</summary>
    [Fact]
    public void The_resume_entry_says_a_start_after_a_pause_is_not_a_new_campaign()
    {
        var resume = FlattenedEntry("manager", "resume");

        Assert.Contains("## A start after a pause is not a new campaign", resume, StringComparison.Ordinal);
        Assert.Contains("Work the pause held runs again by itself, and the people in flight keep their lines", resume, StringComparison.Ordinal);
        Assert.Contains("Plan a first batch only where nobody in the campaign was ever touched", resume, StringComparison.Ordinal);
        Assert.Contains("the daily grid starts again from it", resume, StringComparison.Ordinal);
        Assert.Contains("--kind campaign_paused", resume, StringComparison.Ordinal);
    }

    /// <summary>The quick look reads the counts, acts only on what is off, and accounts for nobody one by one.</summary>
    [Fact]
    public void The_quick_look_reads_the_counts_and_acts_only_on_what_is_off()
    {
        var quick = FlattenedEntry("manager", "quick_look");

        Assert.Contains("The quick look is short. It reads the counts, acts only on what is off, and accounts for no person one by one", quick, StringComparison.Ordinal);
        Assert.Contains("do not create a person's next touch, end a line whose touches are spent, or widen, at a quick look", quick, StringComparison.Ordinal);
        Assert.Contains("**Nothing off is a good quick look.**", quick, StringComparison.Ordinal);
        Assert.DoesNotContain(SkillPack.PrintedCommands(EntryFile("manager", "quick_look")), line => line.Contains("list-contacts", StringComparison.Ordinal));
    }

    /// <summary>
    /// The deep review accounts for every person, the held and the ended among them; works in an order to a ceiling of
    /// twenty people; creates each next touch one at a time when due; and ends a line by the verb that ends it.
    /// </summary>
    [Fact]
    public void The_deep_review_accounts_for_every_person_and_keeps_each_line()
    {
        var deep = FlattenedEntry("manager", "deep_review");

        Assert.Contains("Every person who is not `working`, `waiting` or `out` is yours to account for at this review", deep, StringComparison.Ordinal);
        Assert.Contains("- **`paused`.** They replied and the runtime held their line.", deep, StringComparison.Ordinal);
        Assert.Contains("- **`finished`.** Their line in this campaign ended", deep, StringComparison.Ordinal);
        Assert.Contains("**A review has fifteen minutes and one attempt, so it works in an order and to a ceiling.**", deep, StringComparison.Ordinal);
        Assert.Contains("Handle at most twenty people at one review across steps 2 to 4.", deep, StringComparison.Ordinal);
        Assert.Contains("**The next touch is yours, one at a time, at a review.**", deep, StringComparison.Ordinal);
        Assert.Contains("**A follow-up is a new letter.**", deep, StringComparison.Ordinal);
        Assert.Contains("No line goes past `Lines:MaxTouches`, the ceiling the settings hold, and you never plan past it", deep, StringComparison.Ordinal);
        Assert.Contains("the roles may plan it: a fourth touch after something made the person newly relevant, or two where two are enough", deep, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(EntryFile("manager", "deep_review"));
        Assert.Contains(printed, line => line.StartsWith("jason campaign finish-contacts ", StringComparison.Ordinal) && line.Contains("--reason ", StringComparison.Ordinal));
        Assert.Contains(printed, line => line.StartsWith("jason campaign list-contacts ", StringComparison.Ordinal) && line.EndsWith("--summary paused", StringComparison.Ordinal));
        Assert.Contains(printed, line => line.StartsWith("jason campaign list-contacts ", StringComparison.Ordinal) && line.EndsWith("--summary finished", StringComparison.Ordinal));
        var touch = Assert.Single(printed, line => line.Contains("--operation message.send", StringComparison.Ordinal));
        Assert.Contains("--idempotency-key wi_01JB6K8TQ2W9V4MZ0C3Y7H5NRD-touch-2-cnt_", touch, StringComparison.Ordinal);
    }

    /// <summary>
    /// After a reply the runtime has held the person's line, and the manager ends it or continues it by the verdict: a
    /// person's own reply ends it, an out-of-office notice continues it, and no verdict yet leaves it held.
    /// </summary>
    [Fact]
    public void The_reply_entry_ends_or_continues_the_line_and_an_out_of_office_continues_it()
    {
        var reply = FlattenedEntry("manager", "inbound_reply");

        Assert.Contains("**The runtime has already held the line.**", reply, StringComparison.Ordinal);
        Assert.Contains("What becomes of the line is yours, and you end it or continue it: the reply's verdict says which.", reply, StringComparison.Ordinal);
        Assert.Contains("- **A person wrote for themselves, or asked to stop** — nature `person` or `opt_out`: end their line in this campaign.", reply, StringComparison.Ordinal);
        Assert.Contains("- **A notice a machine sent** — `out_of_office` or `auto_reply`, nature `automatic`: the line goes on. Continue it", reply, StringComparison.Ordinal);
        Assert.Contains("**Where no verdict stands yet**, the responder has not run: leave the line held", reply, StringComparison.Ordinal);
        Assert.Contains("Create no take-back of your own beside it.", reply, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(EntryFile("manager", "inbound_reply"));
        Assert.Contains(printed, line => line.StartsWith("jason campaign finish-contacts ", StringComparison.Ordinal));
        Assert.Contains(printed, line => line.StartsWith("jason campaign continue-contacts ", StringComparison.Ordinal));
    }

    /// <summary>
    /// On a provider without events, a thread can move for a follow-up the provider sent later than recorded, and the
    /// runtime holds the person for it: the reply entry is the one place that continues them, plans again what the hold
    /// took back, and says so in the note.
    /// </summary>
    [Fact]
    public void The_reply_entry_continues_a_line_held_for_a_move_that_was_jason_s_own()
    {
        var reply = FlattenedEntry("manager", "inbound_reply");

        Assert.Contains("**Where the thread's last message is Jason's own, the hold was for nothing.** On a provider without events", reply, StringComparison.Ordinal);
        Assert.Contains("a follow-up the provider sent later than Jason recorded", reply, StringComparison.Ordinal);
        Assert.Contains("Continue the line, plan again what the hold took back", reply, StringComparison.Ordinal);
        Assert.Contains("and say so in your note, naming the thread and the letter", reply, StringComparison.Ordinal);
        Assert.Contains(
            SkillPack.PrintedCommands(EntryFile("manager", "inbound_reply")),
            line => line.StartsWith("jason campaign continue-contacts ", StringComparison.Ordinal) && line.Contains("our own follow-up", StringComparison.Ordinal));
    }

    /// <summary>
    /// A reply no responder judged is told to nobody, so the manager records its verdict, nature included, as the
    /// responder would; a person's reply is then notified at the end of the run that judged it.
    /// </summary>
    [Fact]
    public void The_reply_entry_records_the_verdict_of_a_reply_no_responder_judged()
    {
        var reply = FlattenedEntry("manager", "inbound_reply");

        Assert.Contains("## A reply no responder judged", reply, StringComparison.Ordinal);
        Assert.Contains("the runtime raised none for its thread (`thread_not_campaign_own`) and you raise none, or the responder for it never ran — record the verdict yourself, as the responder would", reply, StringComparison.Ordinal);
        Assert.Contains("A reply no responder judged is the one exception, and the `inbound_reply` entry says when.", Flattened("manager"), StringComparison.Ordinal);
        var record = Assert.Single(
            SkillPack.PrintedCommands(EntryFile("manager", "inbound_reply")),
            line => line.StartsWith("jason reply record ", StringComparison.Ordinal));
        Assert.Contains("--nature person ", record, StringComparison.Ordinal);
        Assert.Contains("--contact cnt_", record, StringComparison.Ordinal);
        Assert.DoesNotContain("--decision", record, StringComparison.Ordinal);
    }

    /// <summary>To change how the manager does one kind of review, a person edits that entry's file, which an update keeps.</summary>
    [Fact]
    public void Customising_jason_says_one_kind_of_review_is_changed_in_its_entry_file()
    {
        var customising = Whitespace().Replace(File.ReadAllText(SkillPack.Find("customising-jason").File), " ");

        Assert.Contains("To change how the manager does one kind of review, edit that entry's file", customising, StringComparison.Ordinal);
        Assert.Contains("an update keeps a file the person edited", customising, StringComparison.Ordinal);
    }
}
