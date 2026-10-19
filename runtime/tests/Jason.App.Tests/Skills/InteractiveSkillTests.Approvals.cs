namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    /// <summary>
    /// A skill that teaches notifications and is verified is verified by the host that read it as it ships: the front
    /// matter, the body and the catalog's bullet say so, the catalog's row names that host, and the pack's own rule holds
    /// the row's digest to the text. The operating skill teaches them too, and was read as it ships, the ready
    /// installation's one question between the wiring check and the real work included. The managed path and the
    /// planner in a person's session teach none, and are held to the same rule in theories of their own beside this one,
    /// through the same helper.
    /// </summary>
    [Theory]
    [InlineData("approvals-and-questions", "Claude Code 2.1.293 (claude-opus-5) | 0.5.0")]
    [InlineData("operating-the-installation", "Claude Code 2.1.302 (Opus 5.5) | 0.12.0")]
    public void The_skills_that_teach_notifications_are_verified_by_the_host_that_read_them(string name, string reading) =>
        IsVerifiedByTheHostThatReadIt(name, reading);

    /// <summary>
    /// The managed path teaches no notifications, so it is held on its own to the same rule: verified by the host that
    /// read it as it ships. It was read again after the person's brief became the campaign's context and its
    /// description named a person's first question, so its row names that later reading.
    /// </summary>
    [Theory]
    [InlineData("managed-campaign-work", "Claude Code 2.1.302 (Opus 5.5) | 0.12.0")]
    public void The_managed_path_is_verified_by_the_host_that_read_it(string name, string reading) =>
        IsVerifiedByTheHostThatReadIt(name, reading);

    /// <summary>
    /// The planner in a person's session teaches no notifications either, and is held to the same rule: verified by the
    /// host that read it as it ships. Its craft block is the launched planner's too, so the block is in the digest this
    /// row holds; the launched planner's file is read, and verified, on its own. It was read after its description named
    /// the moment it is for and the campaign-planning skill deferred to it, so its row names that later reading.
    /// </summary>
    [Theory]
    [InlineData("planner-in-session", "Claude Code 2.1.303 (Opus 5.5) | 0.12.0")]
    public void The_planner_in_a_persons_session_is_verified_by_the_host_that_read_it(string name, string reading) =>
        IsVerifiedByTheHostThatReadIt(name, reading);

    private static void IsVerifiedByTheHostThatReadIt(string name, string reading)
    {
        var host = reading[..reading.IndexOf(" (", StringComparison.Ordinal)];
        Assert.Equal("verified", SkillFrontMatter.Read(SkillPack.Find(name).File).Metadata.GetValueOrDefault("status"));
        Assert.Contains($"**Status: verified** against {host}.", Flattened(name), StringComparison.Ordinal);

        var catalog = File.ReadAllText(SkillPack.Catalog());
        Assert.Contains($"| `{name}` | {reading} | `{SkillPack.ReadingDigest(SkillPack.Find(name).File)}` |", catalog, StringComparison.Ordinal);
        Assert.Contains($"[`{name}`]({name}/SKILL.md) — **verified.**", catalog, StringComparison.Ordinal);
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
        // the role reads when the work comes back. A session that wrote its own tidier paraphrase would put
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
}
