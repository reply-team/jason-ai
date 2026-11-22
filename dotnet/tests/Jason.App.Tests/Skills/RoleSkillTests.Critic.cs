using Jason.Runtime.Tests;

namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    [Critical(Family.D4, "a role's skill teaches the role what was not decided")]
    public void The_critic_is_told_that_its_verdict_stops_nothing_by_itself()
    {
        var skill = Flattened("critic");

        // A critic that believed it could stop something would report the work blocked and stop there, and
        // nobody downstream would learn what was wrong with the draft.
        Assert.Contains("You veto nothing and cancel nothing.", skill, StringComparison.Ordinal);
        Assert.Contains("for anything else your verdict is your result", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("critic");
    }
}
