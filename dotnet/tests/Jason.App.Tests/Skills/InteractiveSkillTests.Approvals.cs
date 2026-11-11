using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// The approvals skill was read against 0.27.0 and has moved since — it names the act that approves a search and allows
    /// more people under its audience — so it is a draft again until a host reads it against a published version. The
    /// front matter, the body and the catalog's bullet say draft, and the catalog records no reading of this text.
    /// </summary>
    [Fact]
    public void The_approvals_skill_is_a_draft_until_a_host_reads_its_current_text()
    {
        const string Name = "approvals-and-questions";
        Assert.Equal("draft", SkillFrontMatter.Read(SkillPack.Find(Name).File).Metadata.GetValueOrDefault("status"));
        Assert.Contains("**Status: draft.**", Flattened(Name), StringComparison.Ordinal);
        Assert.DoesNotContain("**Status: verified**", Flattened(Name), StringComparison.Ordinal);

        var catalog = File.ReadAllText(SkillPack.Catalog());
        Assert.Contains($"[`{Name}`]({Name}/SKILL.md) — **draft.**", catalog, StringComparison.Ordinal);
        Assert.DoesNotContain($"| `{Name}` | Claude Code", catalog, StringComparison.Ordinal);
    }

    /// <summary>
    /// A question a role raised is answered by recording the answer: the campaign manager's next check-in creates the work
    /// the answer implies, and what recorded it creates nothing, as the skills for a person's own conversation say it. A
    /// host that read only this skill would carry the answer out itself and leave the campaign holding the work twice.
    /// And what <c>--answer</c> is for is the manager's reading, not the role's that asked, which ended when it asked.
    /// </summary>
    [Fact]
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
    [InlineData("approvals-and-questions", "or arrives because a notification said approvals or questions are waiting")]
    [InlineData("operating-the-installation", "whether notifications reach the person and turning them off")]
    public void The_skills_that_teach_notifications_say_so_in_their_description(string name, string words) =>
        Assert.Contains(words, SkillFrontMatter.Read(SkillPack.Find(name).File).Top["description"], StringComparison.Ordinal);

    [Fact]
    [Critical(Family.S2, "the approvals skill never decides")]
    public void The_approvals_skill_never_decides_and_never_invents_the_person()
    {
        var skill = Flattened("approvals-and-questions");

        // A notification brought them here: it says how many wait, and may say protective actions failed, so both lists
        // are read, the failed action goes to the skill that reads failures, and reacting to the notification approves
        // nothing. A session that treated "you have 3 approvals" as the request would be deciding on a count; one that
        // took the failed action for something to decide would put it in front of the person as a choice it is not.
        Assert.Contains("A notification says how many approvals and questions wait, and may add that protective actions failed in campaigns it names by id. The approvals and questions are yours here: list both — `jason approval list` and `jason decision list` — and present them as this skill says; a failed action is not a decision, and `troubleshooting-jason` is where it is read.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("A notification says only how many approvals and questions wait.", skill, StringComparison.Ordinal);
        Assert.Contains("Nothing is approved by reacting to a notification: an approval is a person's decision, recorded as theirs.", skill, StringComparison.Ordinal);

        // The whole reason an approval exists is that a session is not entitled to answer it. A skill that
        // stopped saying so would be read by a session that finds an obvious-looking approval, approves it
        // itself, and leaves a record naming a person who never saw the thing they are recorded as allowing.
        Assert.Contains("never decide on a person's behalf", skill, StringComparison.Ordinal);

        // What goes into --reason and --answer is quoted back to whoever reads it later, and --answer is what
        // the campaign manager reads when the work comes back. An agent that wrote its own tidier paraphrase would put
        // words in a person's mouth in the one row that exists to say what that person actually decided.
        Assert.Contains("their word on that item", skill, StringComparison.Ordinal);

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
}
