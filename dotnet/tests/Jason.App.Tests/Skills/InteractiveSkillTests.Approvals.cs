using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// The approvals skill was read against 0.27.0 and has moved since — it names the act that approves a search and allows
    /// more people under its audience — so it is a draft again until a host reads it against a published version. The
    /// front matter and the body say draft, and the catalog records no reading of this text.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_approvals_skill_is_a_draft_until_a_host_reads_its_current_text()
    {
        const string Name = "approvals-and-questions";
        Assert.Equal("draft", SkillFrontMatter.Read(SkillPack.Find(Name).File).Metadata.GetValueOrDefault("status"));
        Assert.Contains("**Status: draft.**", Flattened(Name), StringComparison.Ordinal);
        Assert.DoesNotContain("**Status: verified**", Flattened(Name), StringComparison.Ordinal);

        var catalog = File.ReadAllText(SkillPack.Catalog());
        Assert.DoesNotContain($"| `{Name}` | Claude Code", catalog, StringComparison.Ordinal);
    }

    /// <summary>
    /// A question a role raised is answered by recording the answer: the campaign manager's next check-in creates the work
    /// the answer implies, and what recorded it creates nothing, as the skills for a person's own conversation say it. A
    /// host that read only this skill would carry the answer out itself and leave the campaign holding the work twice.
    /// And what <c>--answer</c> is for is the manager's reading, not the role's that asked, which ended when it asked.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_approvals_skill_leaves_the_work_an_answer_implies_to_the_managers_next_check_in()
    {
        var skill = Flattened("approvals-and-questions");
        Assert.Contains(
            "Its answer wakes the review that carries the work on: the campaign manager's next check-in will create the work the answer implies, and the session that recorded the answer creates nothing: if it did, the campaign would hold the same work twice.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains(
            "`--answer` is required and is what the campaign manager reads at its next check-in",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("what the role reads when the work comes back to it", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("when the work comes back to it", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>approval get --human</c> prints the example, a template rendered for one person, beside a line saying the person
    /// approves the template as written. The skill says the same where the preview carries one, in the same terms, so a
    /// person is not asked to approve the example as though it were the message every recipient gets.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_approvals_skill_presents_an_example_as_one_reading_of_the_template()
    {
        Assert.Contains(
            "Where the preview carries an `example`, present it as one reading of the template and say that what is approved is the template as written: the example is the runtime's rendering for one person, and the provider's rendering may differ.",
            Flattened("approvals-and-questions"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Where the preview carries a summary, it is what a person reads first: it names this approval's own person, campaign
    /// and numbers, and the intent beside it is only the operation's generic sentence.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_approvals_skill_quotes_the_summary_first()
    {
        Assert.Contains(
            "Where the preview carries a `summary`, quote it first and word for word — it names this approval's own person, campaign and numbers; the `intent` beside it is the operation's generic sentence.",
            Flattened("approvals-and-questions"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A host chooses a skill by its description, so a person brought by a notification, or asking whether
    /// notifications reach them, finds the skill only if its description says so.
    /// </summary>
    [Theory]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    [InlineData("approvals-and-questions", "or arrives because a notification said approvals or questions are waiting")]
    [InlineData("operating-the-installation", "whether notifications reach the person and turning them off")]
    public void The_skills_that_teach_notifications_say_so_in_their_description(string name, string words) =>
        Assert.Contains(words, SkillFrontMatter.Read(SkillPack.Find(name).File).Top["description"], StringComparison.Ordinal);

    /// <summary>
    /// Only a send written under the chat's mark, by a chat turn that can only read and propose, parks whatever the send
    /// mode says; one the chat writes in Ask first, Auto-edit or Full access follows the campaign's send mode, as one from a
    /// terminal does (docs/work-execution.md, "The chat's mark"). A skill that said every send written in the chat waits
    /// would tell an agent in the chat that a message will wait for the person when, under an automatic mode, it goes to a
    /// real person. So the sentence is pinned, and no skill of either pack may say that a send always waits whatever the
    /// send mode unless the same sentence names the chat's mark.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void Only_a_send_written_under_the_chats_mark_always_waits_whatever_the_send_mode()
    {
        Assert.Contains(
            "A send the chat of Jason Desktop writes follows the send mode, as one written in a terminal does; only one written under the chat's mark, by a chat turn that can only read and propose, always waits for the person, whatever the send mode or a standing approval says.",
            Flattened("approvals-and-questions"),
            StringComparison.Ordinal);

        foreach (var skill in SkillPack.All().Concat(SkillPack.Business()))
        {
            var text = Whitespace().Replace(File.ReadAllText(skill.File), " ");
            Assert.DoesNotContain("written in Jason's chat", text, StringComparison.Ordinal);
            Assert.DoesNotContain("written in the desktop's chat", text, StringComparison.Ordinal);
            Assert.DoesNotContain("written or edited in the desktop's chat", text, StringComparison.Ordinal);
            foreach (var sentence in SentenceEnd().Split(text))
            {
                if (sentence.Contains("always waits for the person, whatever the send mode", StringComparison.Ordinal))
                {
                    Assert.True(
                        sentence.Contains("the chat's mark", StringComparison.Ordinal),
                        $"{skill.Name} says a send always waits whatever the send mode without naming the chat's mark: {sentence}");
                }
            }
        }
    }


    /// <summary>
    /// "All of these", said about a list the agent showed, is the person's word for each item whose content was shown;
    /// where some were not shown, the agent says once how many would go out unread and asks whether to show them first or
    /// approve them as they are, and does what the answer says. It never refuses and never insists. Said the same in the
    /// session skill that presents the list and in the business pack's <c>confirm_each</c>, and the sentences beside them
    /// that made a list never enough, or a confirmation valid only after reading, say it too. Without it a chat approves
    /// ten letters on one "all of these" where a terminal refuses them, or a terminal holds a person to reading what they
    /// chose not to read.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void All_of_these_about_a_shown_list_is_the_persons_word_for_each_and_unread_items_are_asked_once()
    {
        const string Sentence =
            "All of these, said about a list you showed them, is their word for each item where each one's content was shown. "
            + "Where it was not, say once how many would go out unread and ask whether to show them first or approve them as "
            + "they are; their answer is their word.";
        var approvals = Flattened("approvals-and-questions");
        Assert.Contains(Sentence, approvals, StringComparison.Ordinal);
        Assert.DoesNotContain("never enough to decide anything", approvals, StringComparison.Ordinal);
        Assert.Contains("once they have read what it is or, told how many would go out unread, chosen to approve them as they are.", approvals, StringComparison.Ordinal);

        var boundaries = Whitespace().Replace(File.ReadAllText(Path.Combine(SkillPack.BusinessRoot(), "approval-boundaries", SkillPack.SkillFile)), " ");
        var from = boundaries.IndexOf("- **`confirm_each`** —", StringComparison.Ordinal);
        var to = from < 0 ? -1 : boundaries.IndexOf("### ", from, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, $"approval-boundaries has no `confirm_each` entry (found at {from}, next heading at {to}).");
        Assert.Contains(Sentence, boundaries[from..to], StringComparison.Ordinal);
        Assert.Contains("for a previewed call, after seeing the preview, or after being told how many would go out unread and choosing to approve them as they are.", boundaries, StringComparison.Ordinal);
    }

    /// <summary>
    /// An act's reason is the agent's own short line, never the person's pasted text, in a person's own session as in the
    /// chat, whose rule 13 says it in these words: a reason is journaled, and text from outside never goes into a command. A
    /// session taught to carry the person's sentence across would put a prospect's words, or a quote that breaks the line,
    /// into the chronicle under the person's name, where the chat writes its own line.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_reason_of_an_act_is_the_agents_own_short_line_and_never_the_persons_pasted_text()
    {
        var skill = Flattened("approvals-and-questions");
        Assert.Contains(
            "What you put in `--reason` is a short line in single quotes that says in your own words what the person told you to do, such as 'the person said to approve this one', made only of letters, digits, spaces and the marks . , : and -; never their pasted text and never a prospect's, since text from outside never goes into a command and a reason is journaled.",
            skill,
            StringComparison.Ordinal);
        Assert.DoesNotContain("their sentence carried across", skill, StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.S2, "the approvals skill never decides")]
    public void The_approvals_skill_never_decides_and_never_invents_the_person()
    {
        var skill = Flattened("approvals-and-questions");

        // A notification brought them here: it says how many wait, and may say protective actions failed, so both lists
        // are read, the failed action goes to the skill that reads failures, and reacting to the notification approves
        // nothing. A session that treated "you have 3 approvals" as the request would be deciding on a count; one that
        // took the failed action for something to decide would put it in front of the person as a choice it is not.
        Assert.Contains("A notification says how many approvals and questions wait, and may add how many replies came in, protective actions that failed in campaigns it names by id, accounts whose events were not read, provider blocks, items held at a daily limit and emails that bounced. The approvals and questions are yours here: list both — `jason approval list` and `jason decision list` — and present them as this skill says; a failed action is not a decision, and `troubleshooting-jason` is where it is read. A hold is lifted only on the person's word, as `managed-campaign-work` says; replies, blocks, accounts and bounces are read as `operating-the-installation`'s \"Notifications\" says.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("and may add that protective actions failed in campaigns it names by id.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("A notification says only how many approvals and questions wait.", skill, StringComparison.Ordinal);
        Assert.Contains("Nothing is approved by reacting to a notification: an approval is a person's decision, recorded as theirs.", skill, StringComparison.Ordinal);

        // The whole reason an approval exists is that a session is not entitled to answer it. A skill that
        // stopped saying so would be read by a session that finds an obvious-looking approval, approves it
        // itself, and leaves a record naming a person who never saw the thing they are recorded as allowing.
        Assert.Contains("never decide on a person's behalf", skill, StringComparison.Ordinal);

        // What goes into --answer is what the campaign manager reads when the work comes back. An agent that wrote its
        // own tidier paraphrase would put words in a person's mouth in the one row that exists to say what that person
        // actually decided. The reason is another thing, the agent's own short line, held below.
        Assert.Contains("What you put in `--answer` is **their word on that item**", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("What you put in `--reason`, and in `--answer`", skill, StringComparison.Ordinal);

        // Inside a launched run the CLI claims the attempt from the environment when no actor is given, so
        // omitting the option is not anonymity - it is a role trying to decide, refused as approval_not_human.
        // A session that had never been told this reads that refusal as the runtime being broken.
        Assert.Contains("--actor human:", skill, StringComparison.Ordinal);

        // The preview is stored at the claim precisely so nobody assembles a second version of it. Filling a
        // gap in it with a plausible cost, reach or reassurance means the person decides about an operation
        // that was never published, and the decision they gave is not the one the claim will compare against.
        Assert.Contains("never invent", skill, StringComparison.OrdinalIgnoreCase);

        // There is no bulk verb. Asked to "approve the first two", a session that did not know this either
        // invents one or picks whichever two were nearest to hand - and two approvals carry two subjects, two
        // reasons and two chronicle lines, so a guess here is a decision nobody made about work nobody named.
        Assert.Contains("one call each", skill, StringComparison.Ordinal);

        // The runtime's guarantee stops exactly here, and a skill that implied otherwise would be selling an
        // operator a check that is not made: an accountable decision depends on the person at the keyboard
        // being the person named, which is trust and not enforcement.
        Assert.Contains(
            "cannot tell a person from a process holding that person's own command line",
            skill,
            StringComparison.Ordinal);

        // An item that parks a second time after its input was edited looks like a malfunction and invites a
        // workaround. It is the subject hash refusing to let something run under a decision about something
        // else, and a session that did not recognise the reason would work around the one guarantee here.
        Assert.Contains("input_changed", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A standing approval is a person's decision made in advance, and the skill that records their decisions says how
    /// they set, list and unset one, in the words the runtime uses for it. It sits before "What an approval covers", it
    /// does not soften the rule that nobody decides for a person, and it names the three verbs and the options they take,
    /// and the act that approves a search and allows more people under its audience, so a session that guessed one would
    /// be corrected by the text and not by a refusal.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void The_approvals_skill_says_how_a_person_sets_lists_and_unsets_a_standing_approval()
    {
        var text = File.ReadAllText(SkillPack.Find("approvals-and-questions").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var skill = Flattened("approvals-and-questions");

        var section = text.IndexOf("## A standing approval is a person's decision made in advance", StringComparison.Ordinal);
        var covers = text.IndexOf("## What an approval covers, and what changes it", StringComparison.Ordinal);
        var recording = text.IndexOf("## Recording what they decide", StringComparison.Ordinal);
        Assert.True(
            recording >= 0 && section > recording && covers > section,
            $"The section on standing approvals is at {section}; it belongs after \"Recording what they decide\" ({recording}) and before \"What an approval covers\" ({covers}).");

        Assert.Contains("it is set only on their explicit word. **\"Handle these\" is not one**", skill, StringComparison.Ordinal);
        Assert.Contains("**`--actor human:<their id>` is required**", skill, StringComparison.Ordinal);
        Assert.Contains("`approval_not_human`", skill, StringComparison.Ordinal);
        Assert.Contains("**The limits are the person's to choose, and none is the default.**", skill, StringComparison.Ordinal);
        Assert.Contains("Setting one again replaces what was set and starts its count over", skill, StringComparison.Ordinal);
        Assert.Contains("`interested` covers neither `very_interested` nor `Interested`", skill, StringComparison.Ordinal);

        // The allowance is set by the act a person makes when they approve a search: approve, and allow more people under
        // its audience. A session that knew only the categories would ask the person to approve every top-up forever.
        Assert.Contains(
            "**A search is approved and allowed in one act**: `jason approval approve <apr_…> --allow <people> --actor human:<their id>`",
            skill,
            StringComparison.Ordinal);

        // What a search spends is the person's: its probe always waits for them, the allowance is offered only once the
        // audience has been judged, a new audience's search takes the old allowance back and the person hears so first,
        // and nobody else moves it.
        Assert.Contains("A search spends the provider's money, so the first search under an audience, its probe, always waits for the person.", skill, StringComparison.Ordinal);
        Assert.Contains("That is the moment to offer the allowance, never before the audience has been judged", skill, StringComparison.Ordinal);
        Assert.Contains("takes the old one back in the same act. Say so before they approve such a search", skill, StringComparison.Ordinal);
        Assert.Contains("No act but the person's moves the allowance: no role, no verdict and no probe that waits.", skill, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("approvals-and-questions").File);
        Assert.Contains(printed, line => line.StartsWith("jason approval set-standing cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --category meeting_requested ", StringComparison.Ordinal) && line.Contains(" --actor human:", StringComparison.Ordinal));
        Assert.Contains("jason approval list-standing cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD", printed);
        Assert.Contains(printed, line => line.StartsWith("jason approval unset-standing cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --category meeting_requested ", StringComparison.Ordinal) && line.Contains(" --actor human:", StringComparison.Ordinal));
    }

    /// <summary>
    /// What a standing approval releases is a draft nobody has been asked about. A reply parked before it was set stays the
    /// person's, and stays theirs after its question expires and the item is reopened; the skill says so, and says where to
    /// see what went out without them, by the two lines the runtime writes for it.
    /// </summary>
    [Fact]
    [Critical(Family.D4, "a skill a person's session reads teaches what was not decided")]
    public void A_reply_parked_before_the_standing_approval_stays_the_persons_and_what_went_out_is_visible()
    {
        var skill = Flattened("approvals-and-questions");

        Assert.Contains(
            "A reply parked before the standing approval was set still waits for the person, however long ago, and setting one releases none of it; it stays theirs even after its question expired and the item was reopened.",
            skill,
            StringComparison.Ordinal);
        Assert.Contains("**Where to see what went out without them.**", skill, StringComparison.Ordinal);
        Assert.Contains("is an `approved` approval whose reason is `standing_approval`", skill, StringComparison.Ordinal);
        Assert.Equal("standing_approval", Jason.Runtime.Approvals.ApprovalGate.StandingApproval);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("approvals-and-questions").File);
        Assert.Contains("jason approval list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --status approved", printed);
        Assert.Contains("jason journal list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --kind approval_standing_applied", printed);
        Assert.Equal("approval_standing_applied", Jason.Runtime.Journal.JournalKinds.ApprovalStandingApplied);
    }

    /// <summary>
    /// A session reopens a line on the person's word: on a yes to the manager's question, it records the answer and then
    /// reopens as the person naming that question, since the runtime takes only an answered one; on the person's direct
    /// word, it reopens as the person with no question. A session that reopened first would be refused, and one that
    /// answered without reopening would leave the person's yes undone until the manager's next review.
    /// </summary>
    [Fact]
    public void A_session_reopens_a_line_on_the_person_s_yes_and_on_their_direct_word()
    {
        var skill = Flattened("approvals-and-questions");

        Assert.Contains("## A line the person says to reopen", skill, StringComparison.Ordinal);
        Assert.Contains("When the person answers yes, record their answer as above, then reopen the line as theirs, naming that question with `--decision`.", skill, StringComparison.Ordinal);
        Assert.Contains("it never reads what the answer says, so you run the reopen on a yes alone. On any other answer, record it and reopen nothing.", skill, StringComparison.Ordinal);
        Assert.Contains("When the person tells you directly to reopen somebody's line, with no question behind it, run it as theirs without `--decision`", skill, StringComparison.Ordinal);

        var printed = SkillPack.PrintedCommands(SkillPack.Find("approvals-and-questions").File).ToList();
        var answer = printed.FindIndex(line => line.StartsWith("jason decision answer ", StringComparison.Ordinal) && line.Contains("write to them again", StringComparison.Ordinal));
        var carried = printed.FindIndex(line => line.StartsWith("jason campaign reopen-contacts ", StringComparison.Ordinal) && line.Contains(" --decision dec_", StringComparison.Ordinal));
        var direct = printed.FindIndex(line => line.StartsWith("jason campaign reopen-contacts ", StringComparison.Ordinal) && !line.Contains("--decision", StringComparison.Ordinal));
        Assert.True(answer >= 0 && carried > answer, "The answer is recorded before the reopen that names its question: the runtime takes only an answered question.");
        Assert.True(direct >= 0, "The skill prints no reopen on the person's direct word.");
        foreach (var reopen in new[] { printed[carried], printed[direct] })
        {
            Assert.Contains(" --actor human:", reopen, StringComparison.Ordinal);
            Assert.Contains(" --reason '", reopen, StringComparison.Ordinal);
        }
    }
}
