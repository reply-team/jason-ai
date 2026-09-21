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
}
