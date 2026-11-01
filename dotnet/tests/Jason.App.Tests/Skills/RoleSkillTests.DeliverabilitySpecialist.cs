namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_deliverability_specialist_is_told_that_there_is_nothing_here_to_warm()
    {
        var skill = Flattened("deliverability-specialist");

        // The role whose entire profession is acts this build cannot perform. Told plainly, it reads what is
        // there and reports; told nothing, it spends a paid attempt looking for a verb that warms something.
        Assert.Contains("There is nothing to warm in this build", skill, StringComparison.Ordinal);
        Assert.Contains("say what a person would have to do outside Jason", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("deliverability-specialist");
    }
}
