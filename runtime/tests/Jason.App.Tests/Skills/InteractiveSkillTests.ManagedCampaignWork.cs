namespace Jason.App.Tests.Skills;

public partial class InteractiveSkillTests
{
    [Fact]
    public void The_managed_path_says_what_the_runtime_decides_and_what_it_promises_nothing_about()
    {
        var skill = Flattened("managed-campaign-work");

        // No provider ships configured, and a route is somebody's deliberate act rather than a default. A
        // skill that implied otherwise would have the session promise work that fails with no_route, which
        // reads to a person as the product being broken. The sentence is pinned rather than the word: "route"
        // also matches "routed", so a text saying the opposite passed the assertion that was here.
        Assert.Contains("writing a route is an operator's explicit act", skill, StringComparison.Ordinal);
        Assert.Contains("no_route", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("out of the box", skill, StringComparison.OrdinalIgnoreCase);

        // The wiring check proves a provider is reached, and it is not campaign work: the real work begins with a plan,
        // and this skill is how the plan becomes work. A session that took the check for the job would stop there.
        Assert.Contains(
            "The wiring check, `docs/golden-path.md`, connects a provider and proves it with the person's own address; it is not campaign work. The real work begins with a campaign planned with the business pack's `campaign-planning` skill, and this skill is how that plan becomes work the runtime and Jason's roles perform.",
            skill,
            StringComparison.Ordinal);

        // And it says so where a session checks whether a provider is connected: at the end of "Before you start", after
        // the sentence that sends setting one up to the operating skill, and before the work itself.
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        var setUp = text.IndexOf("Setting one up is `operating-the-installation`", StringComparison.Ordinal);
        var wiring = text.IndexOf("The wiring check, `docs/golden-path.md`, connects a provider", StringComparison.Ordinal);
        var work = text.IndexOf("\n## The work itself\n", StringComparison.Ordinal);
        Assert.True(
            setUp >= 0 && wiring > setUp && work > wiring,
            $"managed-campaign-work says where the wiring check stands at offset {wiring}; it belongs after the operating skill's sentence (offset {setUp}) and before \"## The work itself\" (offset {work}).");

        // Arguments are judged when the item is created, against the operation's own published document — so
        // a refusal there is about what was asked for, and not about the provider being unreachable.
        Assert.Contains("validated when the item is created", skill, StringComparison.Ordinal);

        // Work created inside a launched run inherits that run's profile, and naming an actor breaks the
        // chain. A session that named one would scatter a campaign's work across hosts nobody chose.
        Assert.Contains("inherits the profile of the run that created it", skill, StringComparison.Ordinal);

        // Scheduling and resumption belong to the runtime. This is the sentence that replaces the model this
        // pack supersedes, and it has to be in the skill rather than only in a document nobody hands over.
        Assert.Contains("nothing else wakes the work", skill, StringComparison.Ordinal);

        // A create whose answer was lost is sent again with the same key, never without one: the key is what makes
        // the second send the first one's answer rather than a second campaign or a second enrolment. And nothing
        // makes a key up, so a session that does not give one has none to repeat.
        Assert.Contains("send the same command with the same key", skill, StringComparison.Ordinal);
        Assert.Contains("Nothing makes a key up for you", skill, StringComparison.Ordinal);

        // And the three moments that are skills of their own, named so that a session knows they exist. A
        // session that met a parked approval, a stopped item or an effect produced outside Jason and had
        // never heard of them would improvise — which is the one thing this pack exists to prevent.
        Assert.Contains("approvals-and-questions", skill, StringComparison.Ordinal);
        Assert.Contains("troubleshooting-jason", skill, StringComparison.Ordinal);
        Assert.Contains("reporting-outside-effects", skill, StringComparison.Ordinal);
    }
}
