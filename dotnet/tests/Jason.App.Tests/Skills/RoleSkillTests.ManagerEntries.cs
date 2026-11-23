using Jason.Runtime.Tests;

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
    /// A first start is held by a missing sender alone: with no tone on record the first batch is handed over all the same
    /// and the line says once that a tone can be named. A start that raised a question for a tone and stopped would leave a
    /// campaign the chat set up and started with nothing moving.
    /// </summary>
    [Fact]
    public void A_first_start_stops_for_a_missing_sender_and_goes_on_without_a_tone()
    {
        var start = FlattenedEntry("manager", "start");
        Assert.Contains("a route for the send the context's channel needs; who the messages are written as, from the preferences and the memory files. Where one is missing, nothing that would write to anybody is planned: raise one question naming what is missing, and stop there.", start, StringComparison.Ordinal);
        Assert.Contains(InteractiveSkillTests.MissingTone + " Say it in your line, and raise no question for it.", start, StringComparison.Ordinal);
        Assert.DoesNotContain("who the messages are written as and how the company sounds, from the preferences", start, StringComparison.Ordinal);
    }

    /// <summary>
    /// A deep review that falls due as a reply asks for a review runs in the reply review's place, and the reply's section
    /// is no longer in the head: the head and the deep review send it to the <c>inbound_reply</c> entry's file, and a
    /// review after a resume reads the replies of the pause there too.
    /// </summary>
    [Fact]
    public void A_deep_review_carrying_a_reply_reads_the_reply_entry_s_file()
    {
        const string Pointer = "Where `trigger` is `inbound_reply_observed`, its section is the `inbound_reply` entry: read the file `entries/inbound_reply.md` beside your skill, its \"When a reply woke you\" and \"A reply no responder judged\", before step 1.";
        Assert.Contains(Pointer, Flattened("manager"), StringComparison.Ordinal);
        Assert.Contains(Pointer, FlattenedEntry("manager", "deep_review"), StringComparison.Ordinal);
        Assert.Contains("replies, and the lines they paused, read as the `inbound_reply` entry says (the file `entries/inbound_reply.md` beside your skill)", FlattenedEntry("manager", "resume"), StringComparison.Ordinal);

        var reply = FlattenedEntry("manager", "inbound_reply");
        Assert.Contains("## When a reply woke you", reply, StringComparison.Ordinal);
        Assert.Contains("## A reply no responder judged", reply, StringComparison.Ordinal);
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
        Assert.Contains("that succeeded, or that ended not knowing whether it went (an `ambiguous` attempt) — and nothing else", head, StringComparison.Ordinal);
        Assert.Contains("nor is a letter the provider held that a `message.cancel` which succeeded took back", head, StringComparison.Ordinal);
        Assert.Contains("It is `finished` once it was ended", head, StringComparison.Ordinal);
        Assert.Contains("**You never plan past the ceiling, and you never touch a setting.**", head, StringComparison.Ordinal);
        Assert.Contains("A day's limit is lifted only on the person's word, by them or by their own session, never by you", head, StringComparison.Ordinal);
        Assert.Contains("Where you meet a held check-in or a role's work held at a day's limit, the lift is the person's word, and a question you raise about it names the way: they tell their agent to ignore the limit, or type the command the held item's notification prints, and it holds until the installation's next midnight", head, StringComparison.Ordinal);
        Assert.Contains("**end a person's line** in this campaign, with a reason", head, StringComparison.Ordinal);
        Assert.Contains("Where you need the limits themselves, read them: `jason launches list --campaign <cmp>` says each role's limit a day as it stands", head, StringComparison.Ordinal);
    }

    /// <summary>A line is reopened only on the person's yes, by the question they answered, and never on a reading of the manager's.</summary>
    [Fact]
    public void The_manager_reopens_a_line_only_on_the_person_s_yes()
    {
        var head = Flattened("manager");

        Assert.Contains("**A line is reopened only on the person's yes.** You never reopen an ended line on your own reading", head, StringComparison.Ordinal);
        Assert.Contains("a yes reopens the end it was asked about, and a line ended again later needs a question of its own", head, StringComparison.Ordinal);
        Assert.Contains("So do not ask to reopen a line whose `touches` already reach `Lines:MaxTouches`", head, StringComparison.Ordinal);
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
    /// The reserve's threshold is the campaign's, twenty, the runtime's own number, set by the runtime at the first start; and where no
    /// top-up can bring more, the manager asks at once rather than at the next deep review.
    /// </summary>
    [Fact]
    public void The_manager_asks_at_once_where_no_top_up_can_bring_more_people()
    {
        var head = Flattened("manager");

        Assert.Contains("fell under the threshold the campaign holds — twenty, the runtime's own number, which it sets at the campaign's first start where its people come from a search (`Manager:AudienceThreshold`), unless the person changed it", head, StringComparison.Ordinal);
        Assert.DoesNotContain("the threshold the person set", head, StringComparison.Ordinal);
        Assert.Equal(20, new Jason.Runtime.Configuration.ManagerOptions().AudienceThreshold);
        Assert.Contains("**Where no top-up can bring more, ask at once.**", head, StringComparison.Ordinal);
        Assert.Contains("raise one question to the person at this review, and do not leave it to the next deep review", head, StringComparison.Ordinal);
        Assert.Contains("Where a top-up you created parks for the person, its preview is that question, so raise none beside it.", head, StringComparison.Ordinal);
    }

    /// <summary>
    /// A low reserve is a question only where the reserve is really spent. While a search of the campaign is still followed
    /// or waits for the person, the probe included, it asks nothing and creates nothing, and the review the search's end
    /// summons plans the batch; after that, it waits for a verdict only while the analyst's item that gives one is open, so
    /// with no such item open the question is asked. The page says the same of the check-in the low line wakes.
    /// </summary>
    [Fact]
    public void A_low_reserve_asks_nothing_while_its_search_or_its_judgement_is_under_way()
    {
        const string Search = "While a search of the campaign is still followed (its `ended_as` is null) or waits for the person's approval, the probe included, a low reserve asks nothing and creates nothing: say in your line that the reserve is low because the probe has not landed, and the review the search's end summons plans the batch.";
        const string Judgement = "After that, a low reserve asks nothing while no verdict is in yet and the analyst's item that will give one is still open: say in your line when the judgement is due.";
        const string Ask = "Where no analyst's item is open, because none was made or it ended or failed with no verdict, ask.";

        var head = Flattened("manager");
        var section = head[head.IndexOf("**Where no top-up can bring more, ask at once.**", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("## What you may decide yourself", StringComparison.Ordinal)];
        foreach (var said in new[] { Search, Judgement, Ask })
        {
            Assert.Contains(said, section, StringComparison.Ordinal);
        }

        // The top-up's paragraph points to that rule rather than asking: before any verdict it asks nothing while the
        // analyst's item is open.
        Assert.Contains("before any verdict, create no top-up: say in your line that the reserve is low and when the judgement is due, and follow \"Where no top-up can bring more\" below.", head, StringComparison.Ordinal);
        Assert.DoesNotContain("ask the person as \"Where no top-up can bring more\" below says", head, StringComparison.Ordinal);

        var start = FlattenedEntry("manager", "start");
        var low = start[start.IndexOf("## A low reserve at the start", StringComparison.Ordinal)..];
        foreach (var said in new[] { Search, Judgement, Ask })
        {
            Assert.Contains(said, low, StringComparison.Ordinal);
        }

        Assert.Contains("starts with fewer than its threshold (twenty unless changed) people nobody has started on", start, StringComparison.Ordinal);

        var page = Whitespace().Replace(File.ReadAllText(Path.Combine(SkillPack.RepositoryRoot(), "docs", "campaign-manager.md")), " ");
        Assert.Contains(
            "the recovery wakes nobody, and the check-in the low line wakes asks nothing while the probe is unfinished: the manager asks at once only where the reserve is really spent, with no search of the campaign under way.",
            page,
            StringComparison.Ordinal);
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

    /// <summary>
    /// The medium chain, as the manager meets it: the start hands the batch to the planner, which raises the copywriter's
    /// line, research only where it is asked for or justified, one personalizer per person and the first touches; the deep
    /// review sends each follow-up from the copywriter's template, has a personalizer write for one person only where it
    /// decides so, and has the analyst read the first batch once before the campaign widens. A manager told the old pilot
    /// would wait for a planner's question that never comes, and widen on nothing read.
    /// </summary>
    [Fact]
    public void The_manager_s_entries_carry_the_medium_chain_in_its_order()
    {
        var start = FlattenedEntry("manager", "start");
        Assert.Contains("The planner runs the first batch as its pilot, the medium chain its skill orders, and raises each role's work itself — one copywriter's item for the campaign's line, research for one person only where the person asked or it can say why, one personalizer's item for each person's first letter, and the first touch for each person of the batch", start, StringComparison.Ordinal);
        Assert.Contains("What follows the first touches is yours, at the deep reviews: the follow-ups, the end of each line, the analyst's reading of the first batch and the widening", start, StringComparison.Ordinal);
        Assert.DoesNotContain("the hold for their replies and the analyst's reading", start, StringComparison.Ordinal);

        var deep = FlattenedEntry("manager", "deep_review");
        Assert.Contains("**A follow-up is the copywriter's general template.**", deep, StringComparison.Ordinal);
        Assert.Contains("create the send from that template, as written, with no role run", deep, StringComparison.Ordinal);
        Assert.Contains("create a personalizer's item for that person alone, and the send at the review after it has answered; never one for a batch", deep, StringComparison.Ordinal);
        Assert.Contains("**The first batch is read before the campaign widens.**", deep, StringComparison.Ordinal);
        Assert.Contains("creates one analyst's item to read that batch", deep, StringComparison.Ordinal);
        Assert.Contains("once for the campaign; the widening after the first batch waits for its answer", deep, StringComparison.Ordinal);
        Assert.Contains("Where the first batch is an audience's probe whose judgement you already asked for, that judgement is the reading: ask for the delivery and the messages in the same item, and create no second one.", deep, StringComparison.Ordinal);
        Assert.DoesNotContain("planner's pilot asked the person whether to go on", deep, StringComparison.Ordinal);
        Assert.DoesNotContain("at the end of the pilot the planner runs", deep, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(EntryFile("manager", "deep_review"));
        Assert.Contains(printed, line => line.StartsWith("jason workitem list ", StringComparison.Ordinal) && line.Contains(" --role copywriter ", StringComparison.Ordinal));
        Assert.Contains(printed, line => line.Contains(" --role analyst ", StringComparison.Ordinal) && line.Contains("-read-first-batch", StringComparison.Ordinal));

        var head = Flattened("manager");
        Assert.Contains("An answer to go on, to the question you raised on the analyst's reading of the first batch, is the word to widen", head, StringComparison.Ordinal);
        Assert.DoesNotContain("to the question a planner's pilot raised at its end", head, StringComparison.Ordinal);
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
        Assert.Contains("A touch refused `touch_ceiling_reached` is the exception: it is a failure, and its line is ended where you meet it, with the ceiling as the reason", quick, StringComparison.Ordinal);
        Assert.Contains("**Nothing off is a good quick look.**", quick, StringComparison.Ordinal);
        Assert.DoesNotContain(SkillPack.PrintedCommands(EntryFile("manager", "quick_look")), line => line.Contains("list-contacts", StringComparison.Ordinal));
    }

    /// <summary>
    /// The deep review accounts for every person, the paused and the ended among them; works in an order to a ceiling of
    /// twenty people; creates each next touch one at a time when due; and ends a line by the verb that ends it.
    /// </summary>
    [Fact]
    public void The_deep_review_accounts_for_every_person_and_keeps_each_line()
    {
        var deep = FlattenedEntry("manager", "deep_review");

        Assert.Contains("Every person who is not `working`, `waiting` or `out` is yours to account for at this review", deep, StringComparison.Ordinal);
        Assert.Contains("- **`paused`.** They replied and the runtime paused their line.", deep, StringComparison.Ordinal);
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
    /// After a reply the runtime has usually paused the person's line, and the manager ends it or continues it by the
    /// verdict: a person's own reply ends it, an out-of-office notice continues it, and no verdict yet leaves it paused.
    /// Where the person is still enrolled after the reply, the runtime took a letter of the campaign for its own move and
    /// paused nothing, so the manager decides at that review: no verb pauses a member, and the touches queued would go.
    /// </summary>
    [Fact]
    public void The_reply_entry_ends_or_continues_the_line_and_an_out_of_office_continues_it()
    {
        var reply = FlattenedEntry("manager", "inbound_reply");

        Assert.Contains("**The runtime has usually paused the line already.**", reply, StringComparison.Ordinal);
        Assert.Contains("What becomes of the line is yours, and you end it or continue it: the reply's verdict says which.", reply, StringComparison.Ordinal);
        Assert.Contains("- **A person wrote for themselves, or asked to stop** — nature `person`, other than `not_now`, or `opt_out`: end their line in this campaign.", reply, StringComparison.Ordinal);
        Assert.Contains("- **A notice a machine sent** — `out_of_office` or `auto_reply`, nature `automatic`: the line goes on. Continue it", reply, StringComparison.Ordinal);
        Assert.Contains("**Where no verdict stands yet**, the responder has not run: leave the line paused", reply, StringComparison.Ordinal);
        Assert.Contains("**Where the person is still `enrolled` after the reply, the runtime did not pause the line**, because a letter of this campaign went to them at or after the reply's moment", reply, StringComparison.Ordinal);
        Assert.Contains("no verb pauses a member, so \"leave the line paused\" is not open to you, and every touch queued for them goes when it is due. Decide at this review.", reply, StringComparison.Ordinal);
        Assert.Contains("give every touch still queued for them a later `not_before`, or cancel it, each with a reason", reply, StringComparison.Ordinal);
        Assert.Contains("Where the person is still `enrolled` after a reply, the runtime did not pause the line", Flattened("manager"), StringComparison.Ordinal);
        Assert.Contains("Create no take-back of your own beside it.", reply, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(EntryFile("manager", "inbound_reply"));
        Assert.Contains(printed, line => line.StartsWith("jason campaign finish-contacts ", StringComparison.Ordinal));
        Assert.Contains(printed, line => line.StartsWith("jason campaign continue-contacts ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A `not_now` verdict has nature `person` too, so the bullet that ends a person's own line names it as the one it
    /// leaves out: an agent that applies the first bullet it matches never ends the line of a person who asked to hear
    /// again later, whose line waits paused for the day its `back_at` gives, as the deep review's `paused` row says.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a person who asked to hear later has their line ended by the first bullet they match")]
    public void A_not_now_reply_is_never_ended_by_the_bullet_that_ends_a_person_s_own_line()
    {
        var reply = FlattenedEntry("manager", "inbound_reply");

        Assert.Contains("nature `person`, other than `not_now`, or `opt_out`: end their line in this campaign.", reply, StringComparison.Ordinal);
        Assert.DoesNotContain("nature `person` or `opt_out`", reply, StringComparison.Ordinal);
        var ends = reply.IndexOf("- **A person wrote for themselves, or asked to stop**", StringComparison.Ordinal);
        var later = reply.IndexOf("- **`not_now`** — a person, who asked for later: leave the line paused until the day its `back_at` gives, and say so in your note;", StringComparison.Ordinal);
        Assert.True(ends >= 0 && later > ends);
        Assert.Contains("where a `not_now` named a later day, the line waits for it, and that is the reason;", FlattenedEntry("manager", "deep_review"), StringComparison.Ordinal);
    }

    /// <summary>
    /// A `not_now` that names no day leaves the line paused with no day to continue it on, so the deep review accounts for
    /// those people by one question for all of them together, write again or end, asked once and recorded in the note so
    /// that no later review asks it again; and the line is ended on the person's answer only, never on the manager's own
    /// judgement.
    /// </summary>
    [Fact]
    public void The_deep_review_asks_once_about_the_people_whose_not_now_named_no_day()
    {
        var review = FlattenedEntry("manager", "deep_review");

        Assert.Contains("Where a `not_now` named no day, the line stays paused, and that is the reason too: name those people in your line, and raise one question to the person for all of them together — they said not now and named no day: write to them again, or end their lines?", review, StringComparison.Ordinal);
        Assert.Contains("Record in your note that you asked, and for whom, so that no later review asks again for the same people.", review, StringComparison.Ordinal);
        Assert.Contains("Such a line is ended on the person's answer only, never on your own judgement.", review, StringComparison.Ordinal);
    }

    /// <summary>
    /// A campaign linked to a provider's sequence gets no letters of Jason's own, and a set-up may link one before its
    /// first start. So the start entry says, before it hands the first batch to the planner, and the resume entry says,
    /// where it plans a first batch, that such a campaign has none planned: the planner is handed nothing, and the manager
    /// makes the check its head's review gives a linked campaign.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a role's skill teaches the role what was not decided")]
    public void The_start_and_the_resume_plan_no_batch_for_a_campaign_linked_to_a_provider_s_sequence()
    {
        const string Fence = "Where the campaign's context says it is linked to a provider's sequence, plan no batch: hand nothing to the planner, say so in your line, and make the check your skill's review gives such a campaign.";
        var start = FlattenedEntry("manager", "start");
        var fence = start.IndexOf(Fence, StringComparison.Ordinal);
        var hand = start.IndexOf("3. **Hand the first batch to the planner, by one item.**", StringComparison.Ordinal);
        Assert.True(fence >= 0 && hand > fence, $"The start entry does not say, before its step 3, '{Fence}' (at {fence}, step 3 at {hand}).");

        var resume = FlattenedEntry("manager", "resume");
        var then = resume.IndexOf("**What it then needs.**", StringComparison.Ordinal);
        var plan = resume.IndexOf("Plan a first batch only where nobody in the campaign was ever touched", StringComparison.Ordinal);
        var linked = resume.IndexOf(Fence, StringComparison.Ordinal);
        Assert.True(then >= 0 && plan > then && linked > then, $"The resume entry's \"What it then needs\" does not say '{Fence}' (at {linked}).");

        // The check it points at is in the head's review and in the deep review's section of its own.
        Assert.Contains("## A campaign linked to a provider's sequence", FlattenedEntry("manager", "deep_review"), StringComparison.Ordinal);
    }

    /// <summary>
    /// On a provider without events, a thread can move for a follow-up the provider sent later than recorded, and the
    /// runtime pauses the person for it: the reply entry is the one place that continues them, plans again what the hold
    /// took back, and says so in the note.
    /// </summary>
    [Fact]
    public void The_reply_entry_continues_a_line_paused_for_a_move_that_was_jason_s_own()
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

    /// <summary>
    /// The default line is three touches, each written when it is due and never the whole line ahead; the start leaves the
    /// follow-ups to the deep review, and the deep review creates them one at a time. A manager that queued the line at
    /// once would have letters waiting when a reply comes.
    /// </summary>
    [Fact]
    public void The_default_line_is_three_touches_written_one_at_a_time_when_due()
    {
        var head = Flattened("manager");
        Assert.Contains("Three touches, the first message and two follow-ups, is the line where nothing better was thought out for the person. Each is written when it is due, one at a time, and the line is never planned ahead as a whole.", head, StringComparison.Ordinal);

        var start = FlattenedEntry("manager", "start");
        Assert.Contains("only the first touch is the pilot's: the follow-ups are created one at a time, when due, at the deep review", start, StringComparison.Ordinal);

        var deep = FlattenedEntry("manager", "deep_review");
        Assert.Contains("**Three touches, the first message and two follow-ups, is the line where nothing better was thought out.**", deep, StringComparison.Ordinal);
        Assert.Contains("create the next touch at this review: never earlier, and never the whole line at once", deep, StringComparison.Ordinal);
    }

    /// <summary>
    /// At the ceiling the claim refuses the touch, the failure wakes the manager, and the manager ends the line with a
    /// reason; it never asks for the ceiling to be raised to go on. A manager that asked would be routing around the
    /// person's own setting, and one that left the line open would leave the person standing without a reason.
    /// </summary>
    [Fact]
    public void The_manager_ends_a_line_at_the_ceiling_and_never_asks_to_raise_it()
    {
        var head = Flattened("manager");
        Assert.Contains("and the touch's item fails, which wakes you as any failure does. **At the ceiling, the line ends.** End it, `finish-contacts` with the ceiling as the reason, and never ask a person to raise the ceiling so that the line can go on", head, StringComparison.Ordinal);
        Assert.Contains("A line is always ended with a reason, which its `contacts_finished` line keeps.", head, StringComparison.Ordinal);
        Assert.Contains("`touch_ceiling_reached` and `line_finished` are a line's end, not a fault", head, StringComparison.Ordinal);

        var deep = FlattenedEntry("manager", "deep_review");
        Assert.Contains("and that failure is the line's end: end the line at this review, `finish-contacts` with the ceiling as the reason, and never ask for the ceiling to be raised as a way around it", deep, StringComparison.Ordinal);
        Assert.Contains("a touch refused `touch_ceiling_reached` always ends it", deep, StringComparison.Ordinal);
        Assert.Contains(
            SkillPack.PrintedCommands(EntryFile("manager", "deep_review")),
            line => line.StartsWith("jason campaign finish-contacts ", StringComparison.Ordinal) && line.Contains("touch_ceiling_reached", StringComparison.Ordinal));
        Assert.Equal(5, new Jason.Runtime.Configuration.LinesOptions().MaxTouches);
    }

    /// <summary>
    /// After a reply an out-of-office notice continues the line and moves the next letter to the day the reply names,
    /// by a later moment or by cancelling it; a person who asked to stop is ended, and the register and the provider's
    /// opt-out stay the responder's or the person's. A manager that suppressed on its own would make a second record of
    /// a stop the responder already made.
    /// </summary>
    [Fact]
    public void The_reply_entry_moves_an_away_letter_and_leaves_the_stop_s_register_to_the_responder()
    {
        var reply = FlattenedEntry("manager", "inbound_reply");

        Assert.Contains("move a touch still queued for them to the day the reply names, which the responder's `returns` list gives under `back_at`: give it that later `not_before`, or cancel it, with a reason, and the deep review creates the next touch once the day has come", reply, StringComparison.Ordinal);
        Assert.Contains("Where they asked to stop, ending the line is the whole of your part: the register's entry and the provider's opt-out are the responder's, recorded with its verdict, or the person's own, and you put nothing on the register yourself.", reply, StringComparison.Ordinal);
        Assert.DoesNotContain(SkillPack.PrintedCommands(EntryFile("manager", "inbound_reply")), line => line.StartsWith("jason suppression add", StringComparison.Ordinal));
    }

    /// <summary>
    /// A reopen is asked as a question that references the line that ended it, and done only on a yes: the runtime checks
    /// the question and never reads its answer, so the text has to say that the manager acts on a yes alone.
    /// </summary>
    [Fact]
    public void The_manager_asks_to_reopen_by_the_finished_line_and_acts_on_a_yes_alone()
    {
        var head = Flattened("manager");

        Assert.Contains("The runtime does not read the answer's words: it checks that the question is this campaign's, answered by a named person, and names the `contacts_finished` line among its references, and it reopens only the people that line ended whose line it was the latest to end: a yes reopens the end it was asked about, and a line ended again later needs a question of its own. Whether the answer is yes is yours to read, so you reopen on a yes alone", head, StringComparison.Ordinal);
        Assert.Contains("Where the person's session already reopened the line on their yes, the reopen answers `not_finished` for them, and nothing is left to do.", head, StringComparison.Ordinal);
        var raise = Assert.Single(
            SkillPack.PrintedCommands(SkillPack.Find("manager").File),
            line => line.StartsWith("jason decision raise ", StringComparison.Ordinal) && line.Contains("--reference journal_entry:jrn_", StringComparison.Ordinal));
        Assert.DoesNotContain("jrn_", raise[..raise.IndexOf("--reference", StringComparison.Ordinal)], StringComparison.Ordinal);
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
