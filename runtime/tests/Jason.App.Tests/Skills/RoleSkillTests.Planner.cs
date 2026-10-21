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
}
