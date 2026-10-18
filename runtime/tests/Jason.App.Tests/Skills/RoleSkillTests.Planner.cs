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
    }
}
