namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    [Fact]
    public void The_approvals_skill_never_decides_and_never_invents_the_person()
    {
        var skill = Flattened("approvals-and-questions");

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
