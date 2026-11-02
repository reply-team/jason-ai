namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_planner_is_told_that_nothing_here_stores_a_plan()
    {
        var skill = Flattened("planner");

        // The role whose name promises an artefact this build does not have. A planner that believed a plan
        // was stored somewhere would spend its attempt looking for the verb that reads it back, and would
        // write its horizon where nothing reads it instead of into work items a dispatcher will claim.
        Assert.Contains("There is no plan object in this build", skill, StringComparison.Ordinal);

        // Where the decision to create work comes from. The sentence that was here read it out of an
        // `allowed_operations` list, which no planner item carries: the literal reading was "never create
        // work", in the one section that exists to be honest about what this build does and does not have.
        // Pinned as a sentence rather than as the token, because the token also appears in the shared launch
        // contract, so the guard stayed green while the rule it was guarding was replaced.
        Assert.Contains(
            "Whether you create the work or only describe it comes from the brief",
            skill,
            StringComparison.Ordinal);

        CarriesTheLaunchContract("planner");
    }

    [Fact]
    public void The_planner_meets_a_population_that_moved_with_a_new_item_for_the_new_number()
    {
        var skill = Flattened("planner");

        // A person approved one population; a different one at execution is a different decision. A planner that
        // edited the failed item, or copied its input, would ask nobody about the number that is actually there.
        Assert.Contains("with both numbers in the failed attempt's `trace`. Read the count again and create a new item for the new number", skill, StringComparison.Ordinal);
        Assert.Contains("a different population is a different decision, never an edit of the old one", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The planner's judgement, which the session file shares byte for byte: who decides what, and who reads what the
    /// planner leaves in campaign context. Pinned here once; the craft guard carries it to the other file.
    /// </summary>
    [Fact]
    public void The_planners_judgement_says_who_decides_and_which_roles_read_the_campaign()
    {
        var skill = Flattened("planner");

        // A launched role is handed its own work item's context and nothing of the campaign's; it reads the campaign
        // only where its skill has it do so, and two of the nine never do. A planner told that every role reads the
        // context would leave a direction there and send a role that never reads it off without what it needs.
        Assert.Contains(
            "A role that does not read the campaign works from its own work item's context alone, so the work you ask of it carries what it needs.",
            skill,
            StringComparison.Ordinal);

        // The planner decides every turn, as campaign-planning says; going on from a pilot to the rest reaches more people,
        // so unless the campaign's context already allows it, the turn it decides on is to take it back to the person.
        // One rule in both texts: a block that called going on the planner's choice, or the person's, without the other
        // half, would give a session two answers to "who decides to go on". And nothing says "your decision covers the
        // rest" one sentence after "the rest" meant the remaining people, where it reads as the opposite.
        Assert.Contains("Going on from a pilot to the rest reaches more people, so unless the context already allows it, the turn you decide on is to take it back to them.", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("Your decision covers the rest", skill, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("is your decision, made on what came back", skill, StringComparison.OrdinalIgnoreCase);

        // And the turns of a pilot are named as campaign-planning names them, so the two texts a planner may read
        // agree: go on, change course, or take it back to the person.
        Assert.Contains("the choice is to go on, change course, or take it back to the person", skill, StringComparison.Ordinal);

        // Where a decision is the person's, the planner recommends and asks one question with the answers named. A planner
        // that asked an open question, or several, would hand the person a form to fill in where a choice was wanted, and
        // in a session the person is the one waiting for the next turn.
        Assert.Contains("ask one question with the answers named", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// The planner's judgement on the two direct sends, which the session file shares byte for byte. Pinned here once; the
    /// craft guard carries it to the other file.
    /// </summary>
    [Fact]
    public void The_planners_judgement_plans_a_direct_send_as_one_item_for_one_person_and_never_again_after_an_unknown_end()
    {
        var skill = Flattened("planner");

        // The fact the judgement about pilots rests on: a send is approved one person at a time, so it fits a few people
        // and a population is a campaign's. A planner that believed one approval covered a batch of sends would put two
        // hundred people behind a single question, or build a one-person campaign to send one message.
        Assert.Contains(
            "Each send is one work item for one person, and each waits for a person to approve exactly what it would send",
            skill,
            StringComparison.Ordinal);

        // The runtime never repeats an attempt whose send ended without knowing whether it went out, but a planner that
        // creates a new work item for the same message has made a second message, and nothing else stops it. The only safe
        // turn is back to the person, who can look at the provider.
        Assert.Contains(
            "a new item for the same message is a second message, so it goes back to the person",
            skill,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_planners_judgement_leaves_the_account_a_send_goes_from_to_the_person()
    {
        var skill = Flattened("planner");

        // Which account sends is not the planner's choice. A teammate's account can be listed and used, and a message from
        // it reaches the person's prospect under the teammate's name; a planner that picked the first account the listing
        // returned would do that without anyone having said so.
        Assert.Contains("a colleague's account is used only when the person names it", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void The_planners_judgement_leaves_the_watching_to_the_manager()
    {
        var skill = Flattened("planner");

        // The planner's turn ends with the direction in campaign context and the next steps as work. A planner that
        // believed it was to watch the work would hold its attempt open for a result, and one that believed nobody was
        // watching would plan a check behind every item; noticing a stall, or a hypothesis nobody went back to, is the
        // manager's.
        Assert.Contains("The manager keeps the work moving; you do not watch it.", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void The_launched_planner_asks_what_the_opening_needs_through_a_decision()
    {
        var skill = Flattened("planner");

        // The opening's order is the shared judgement's; a launched planner has nobody to ask in a conversation. One that
        // planned a first message while nobody had said who writes it would guess the sender from an account's name.
        Assert.Contains("The order the judgement above gives a first campaign is yours as well", skill, StringComparison.Ordinal);
        Assert.Contains("raise one decision that names what is missing, and answer with the plan up to that step", skill, StringComparison.Ordinal);
        Assert.Contains("Who writes and how they sound are the person's word, never a guess", skill, StringComparison.Ordinal);
    }

    /// <summary>
    /// A standing rule the person gives has one route from a launched planner, whatever its level: a decision that
    /// carries the rule in the person's words and names the memory file it belongs in, which the person or their own
    /// session writes. The planner's note is read by the planner alone, so a rule kept there never reaches the role that
    /// writes the next message; campaign context is the direction, not the person's file; and a launched role writes no
    /// file of theirs. The note may hold only that the decision was raised. Held whole, so a second route, a note made the
    /// rule's home again, or a dropped "until then" fails here.
    /// </summary>
    [Fact]
    public void The_launched_planner_raises_a_standing_rule_as_a_decision_and_keeps_it_nowhere_else()
    {
        var text = File.ReadAllText(SkillPack.Find("planner").File).Replace("\r\n", "\n", StringComparison.Ordinal);
        const string Lead = "**A standing rule reaches the person's file through the person.**";
        var from = text.IndexOf(Lead, StringComparison.Ordinal);
        Assert.True(from >= 0, $"roles/planner no longer says '{Lead}'.");
        var to = text.IndexOf("\n\n", from, StringComparison.Ordinal);

        Assert.Equal(
            "**A standing rule reaches the person's file through the person.** When the person gives a rule for the messages "
            + "after one — \"always include one proof point\" — you write no file of theirs, at any level, and you do not put "
            + "the rule into campaign context. Raise one decision that gives the rule in the person's own words and names the "
            + "file it belongs in: the client's, the project's or the campaign's `AGENTS.md`. The person, or their own session "
            + "the next time they sit with Jason, writes it there; until then the messages that follow do not carry it. Your "
            + "note may say that you raised that decision for that rule, so that it is not raised twice, and never holds the "
            + "rule itself.",
            Whitespace().Replace(text[from..(to < 0 ? text.Length : to)], " ").Trim());

        Assert.DoesNotContain("goes into your note at the level it belongs to", Flattened("planner"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The pilot is several runs of the planner, each raising the next step's work and ending by waiting for it, in the
    /// order the runtime's own end-to-end chain takes: research, the sends a person approves, a hold for the replies, the
    /// analyst, and the turn taken back to the person. Said in the launched planner's own part of the file, because the
    /// judgement it shares with the session file is the same wherever the planner works, and a session does not wait.
    /// </summary>
    [Fact]
    public void The_launched_planner_runs_a_pilot_as_waits_in_the_order_the_chain_takes()
    {
        var file = SkillPack.Find("planner").File;
        var skill = Flattened("planner");

        var steps = new[] { "**Research.**", "**The sends.**", "**The replies.**", "**The reading.**", "**The turn.**" };
        var at = steps.Select(step => skill.IndexOf(step, StringComparison.Ordinal)).ToList();
        Assert.True(
            at.All(index => index >= 0) && at.Zip(at.Skip(1)).All(pair => pair.First < pair.Second),
            $"The planner names the pilot's steps at [{string.Join(", ", at)}] for [{string.Join(", ", steps)}]: each must be there, in that order.");

        Assert.Contains("each run raises the next step's work, waits for it with `jason workitem wait`, and ends", skill, StringComparison.Ordinal);
        Assert.Contains("Raise a researcher's item for each of the ten, and wait on all ten.", skill, StringComparison.Ordinal);
        Assert.Contains("Each send parks at its claim for a person to approve what it would send", skill, StringComparison.Ordinal);
        Assert.Contains("hold: wait with `--until` alone", skill, StringComparison.Ordinal);
        Assert.Contains("A reply that arrives meanwhile is the manager's and the responder's, never yours.", skill, StringComparison.Ordinal);
        Assert.Contains("raise an analyst's item to read the pilot's results, and wait on it", skill, StringComparison.Ordinal);
        Assert.Contains("Their answer wakes the manager, not you.", skill, StringComparison.Ordinal);
        Assert.Contains("rather than wait for the same thing again", skill, StringComparison.Ordinal);

        // A printed moment is in the past by the time a planner reads it, and a wait until a past moment is refused.
        Assert.Contains("The moments in these two lines are examples: work yours out from now", skill, StringComparison.Ordinal);

        var waits = SkillPack.PrintedCommands(File.ReadAllLines(file))
            .Where(line => line.StartsWith("jason workitem wait ", StringComparison.Ordinal))
            .ToList();
        Assert.True(
            waits.Any(line => line.Contains(" --on ", StringComparison.Ordinal))
            && waits.Any(line => line.Contains(" --until ", StringComparison.Ordinal) && !line.Contains(" --on ", StringComparison.Ordinal)),
            $"The planner prints [{string.Join(" | ", waits)}]: a wait on the items a step raised, and a hold with --until alone.");

        // The craft block is the session file's as well, and a session never waits: the pilot as waits is the launched planner's.
        var craft = CraftBlock.Extract(file) ?? string.Empty;
        Assert.DoesNotContain("jason workitem wait", craft, StringComparison.Ordinal);

        // What is in flight includes a waiting item: a planner that read only what runs would raise the same work again.
        Assert.Contains(
            "jason workitem list --campaign cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --status scheduled --status processing --status awaiting_approval --status waiting",
            SkillPack.PrintedCommands(File.ReadAllLines(file)));
    }

    /// <summary>
    /// The planner's judgement on a template, which the session file shares byte for byte. Pinned here once; the craft guard
    /// carries it to the other file. A <c>message.send</c> item that carries a template has <c>compose</c> <c>template</c>
    /// and each text of its format as a template, on an email, a LinkedIn message or an InMail and never a voice message; a
    /// planner that raised it as final text would send the braces, or have the item refused.
    /// </summary>
    [Fact]
    public void The_planners_judgement_gives_a_message_send_that_carries_a_template_compose_template()
    {
        var skill = Flattened("planner");

        Assert.Contains(
"A `message.send` item that carries a template, as the copywriter's answer gives it, has `compose` `template` in its `message`, with each text of its format a template: the subject and the body of an email or an InMail, the body of a LinkedIn message. A voice message is never a template.",
            skill,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// What a campaign is for is its goals, settled with the person when the campaign is made, with the default stated
    /// aloud. The paragraph is in the craft block, so both seats carry it, and neither seat calls the campaign's aim by
    /// the word the runtime does not use. A seat that said nothing of the default would leave a person to learn from a
    /// campaign that behaved like a demo what it had been given.
    /// </summary>
    [Fact]
    public void The_planner_settles_the_goals_with_the_person_and_states_the_default_in_both_seats()
    {
        foreach (var name in new[] { "planner", "planner-in-session" })
        {
            var file = SkillPack.Find(name).File;
            var craft = Whitespace().Replace(CraftBlock.Extract(file) ?? string.Empty, " ");

            Assert.Contains("**A campaign is made with its goals.**", craft, StringComparison.Ordinal);
            Assert.Contains("They are written as the `goals` key of the campaign's context, the runtime's one word for it", craft, StringComparison.Ordinal);
            Assert.Contains(
                "Where nobody has said what the campaign is for, its goal is a demo, and you say so aloud rather than leave it unsaid.",
                craft,
                StringComparison.Ordinal);
            Assert.Contains("A goal is the person's word: you write the ones they gave and invent none for them.", craft, StringComparison.Ordinal);

            Assert.DoesNotContain("objective", File.ReadAllText(file), StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The launched planner reads the goals from the campaign before it plans, plans towards them in their order, and
    /// writes them only as the person gave them. The patch replaces the list, so it is told to send every goal back.
    /// </summary>
    [Fact]
    public void The_launched_planner_reads_the_goals_from_the_campaign_and_keeps_them_there()
    {
        var skill = Flattened("planner");

        Assert.Contains("**The goals are read from the campaign, and kept in it.**", skill, StringComparison.Ordinal);
        Assert.Contains("`jason campaign get` shows whether the context carries `goals`.", skill, StringComparison.Ordinal);
        Assert.Contains("Where it does not, the roles take the default, a demo, and your plan says so.", skill, StringComparison.Ordinal);
        Assert.Contains("write none the person has not given", skill, StringComparison.Ordinal);
        Assert.Contains("`campaign update-context` replaces the whole list, so read the goals that are there and send all of them back", skill, StringComparison.Ordinal);
        Assert.Contains(
            SkillPack.PrintedCommands(SkillPack.Find("planner").File),
            line => line.StartsWith("jason campaign update-context cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD --set '{\"goals\":[", StringComparison.Ordinal));
    }
}
