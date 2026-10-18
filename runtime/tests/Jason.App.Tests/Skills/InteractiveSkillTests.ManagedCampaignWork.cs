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

        // The wiring check proves a provider is reached, and it is not campaign work: it is a walk the person takes or
        // skips, asked once by the operating skill, and the real work begins with the person's brief and a plan, which
        // this skill turns into work. A session that took the check for the job would stop there.
        Assert.Contains(
            "The wiring check, `docs/golden-path.md`, connects a provider and proves it with the person's own address; it is not campaign work. It is a synthetic walk the person takes or skips when the installation becomes ready, and asking them once is `operating-the-installation`'s. The real work begins with the person's brief — what they tell you about their first campaign, in their own words — and a campaign planned with the business pack's `campaign-planning` skill, and this skill is how the brief and that plan become work the runtime and Jason's roles perform.",
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

    /// <summary>
    /// What the person told about their campaign becomes its context when Jason's campaign is created, before any contact
    /// exists and before anything is sent, so every role works from the brief rather than from whatever reached it later.
    /// The context is shared knowledge a role that reads the campaign reads whole, so no key is reserved and none is anybody's
    /// to rely on; and not every role reads it, so a session is not told that a standing instruction there reaches one
    /// that works from its own work item's context alone.
    /// And the verb is Jason's own: the provider operation of the same name makes a campaign somewhere else. A session that
    /// added the people first, or kept the brief in the conversation, would start the work from less than it was given.
    /// The paragraph is held whole: a sentence added to it that named a key every role is told to rely on would turn "no
    /// key is reserved" into its opposite while every sentence pinned here still stood.
    /// </summary>
    [Fact]
    public void The_brief_becomes_Jason_s_campaign_before_any_contact_exists()
    {
        const string Opening = "**The brief becomes the campaign.**";
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);

        var start = text.IndexOf(Opening, StringComparison.Ordinal);
        Assert.True(start >= 0, "managed-campaign-work has no paragraph on the brief.");
        var end = text.IndexOf("\n\n", start, StringComparison.Ordinal);
        var paragraph = Whitespace().Replace(text[start..(end > start ? end : text.Length)], " ").Trim();

        Assert.Equal(
            Opening + " What the person told you about the campaign — the product, the audience, how it sells, in their own words — is the campaign's context, given at `jason campaign create` with `--context`, before any contact is added and before anything is sent. "
            + "It is the campaign's shared knowledge: a role that reads the campaign (`jason campaign get`) reads it whole, and a role that does not works from its own work item's context alone. So its keys are yours to name for what they hold, and none is reserved. "
            + "Keep the person's own words beside what you made of them, and put a link or a file in as what it says, briefly, with where it is, rather than pasted whole. "
            + "This `jason campaign create` makes Jason's own campaign; it is not the provider operation `campaign.create`, which makes a campaign at a provider.",
            paragraph);
        Assert.DoesNotContain("every one of Jason's roles", Flattened("managed-campaign-work"), StringComparison.Ordinal);

        // And the printed lines say it in the same order: under "The work itself" the first campaign create carries the
        // brief and comes before the first add-contacts, and the paragraph follows that block, before the one on keys.
        var from = text.IndexOf("\n## The work itself\n", StringComparison.Ordinal);
        var to = text.IndexOf("\n## ", from + 1, StringComparison.Ordinal);
        Assert.True(from >= 0 && to > from, "managed-campaign-work no longer has \"## The work itself\" followed by another section.");
        var work = text[from..to];

        var printed = SkillPack.PrintedCommands(work.Split('\n')).ToList();
        var create = printed.FindIndex(line => line.StartsWith("jason campaign create ", StringComparison.Ordinal));
        var contacts = printed.FindIndex(line => line.StartsWith("jason campaign add-contacts ", StringComparison.Ordinal));
        Assert.True(
            create >= 0 && contacts > create,
            $"Under \"The work itself\" the campaign create is printed at line {create} and the first add-contacts at line {contacts} of: {string.Join(" | ", printed)}");
        Assert.True(
            printed[create].Contains(" --context '{", StringComparison.Ordinal),
            $"The campaign create under \"The work itself\" gives the campaign no brief: {printed[create]}");

        var block = work.IndexOf(printed[create], StringComparison.Ordinal);
        var brief = work.IndexOf(Opening, StringComparison.Ordinal);
        var keys = work.IndexOf("**Give every create a key you could send again.**", StringComparison.Ordinal);
        Assert.True(
            block >= 0 && brief > block && keys > brief,
            $"The paragraph on the brief is at offset {brief} of \"The work itself\"; it belongs after the create (offset {block}) and before the paragraph on keys (offset {keys}).");
    }

    /// <summary>
    /// A brief changes after the campaign exists, and the change belongs where the brief went: the campaign's context,
    /// written with the verb that sets and unsets top-level keys and leaves the rest as it was. Without it a session that
    /// agreed a new direction keeps it in the conversation, where no role reads it, or creates a second campaign. The
    /// paragraph follows the one on the brief, and its line is printed beside it, where the sequence test runs it.
    /// </summary>
    [Fact]
    public void A_change_of_direction_goes_into_the_campaign_s_context()
    {
        const string Opening = "**A change of direction goes into the context as well.**";
        var text = File.ReadAllText(SkillPack.Find("managed-campaign-work").File).Replace("\r\n", "\n", StringComparison.Ordinal);

        var start = text.IndexOf(Opening, StringComparison.Ordinal);
        Assert.True(start >= 0, "managed-campaign-work has no paragraph on a change of direction.");
        var end = text.IndexOf("\n\n", start, StringComparison.Ordinal);
        Assert.Equal(
            Opening + " On a campaign that already exists, what the person changes about it is written with `jason campaign update-context`: `--set` writes the top-level keys it names, `--unset` removes a key, once for each, and every key it does not name stays as it was.",
            Whitespace().Replace(text[start..(end > start ? end : text.Length)], " ").Trim());

        var brief = text.IndexOf("**The brief becomes the campaign.**", StringComparison.Ordinal);
        var keys = text.IndexOf("**Give every create a key you could send again.**", StringComparison.Ordinal);
        var printed = text.IndexOf("\njason campaign update-context cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --set ", StringComparison.Ordinal);
        Assert.True(
            brief >= 0 && start > brief && printed > start && keys > printed,
            $"The paragraph on a change of direction is at offset {start} and its line at {printed}; they belong after the paragraph on the brief (offset {brief}) and before the one on keys (offset {keys}).");
    }

    /// <summary>
    /// A host loads a skill by its description. A description that named only turning an objective into work left a
    /// person's first question about the work — what do we do first — loading no skill of Jason's, and the session
    /// answered from whatever it already had. The description names that question too, beside the one it always named.
    /// </summary>
    [Fact]
    public void The_managed_path_s_description_names_the_first_question()
    {
        var description = SkillFrontMatter.Read(SkillPack.Find("managed-campaign-work").File).Top.GetValueOrDefault("description") ?? string.Empty;

        Assert.StartsWith(
            "Use when a person wants Jason to start outbound work for them, such as getting meetings or running a campaign, and asks what to do first, and when an interactive agent session should turn an outbound objective into durable campaign work that Jason performs",
            description,
            StringComparison.Ordinal);
    }
}
