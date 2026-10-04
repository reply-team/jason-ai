namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_manager_is_told_which_items_run_without_anybody_and_proposes_the_rest()
    {
        var skill = Flattened("manager");

        // Two items run without waiting for anybody, because their not running is the harm. A manager told there was
        // one would treat the record of a stop as something to hold for a decision.
        Assert.Contains("Two items run even while Jason's campaign is paused, and nobody approves them, because their not running is the harm.", skill, StringComparison.Ordinal);
        Assert.Contains("`optout.record`", skill, StringComparison.Ordinal);

        // An incident pause is created, Jason's campaign paused, a person asked, and the run ended: a campaign runs one
        // item at a time, so the provider's pause is claimed only after the run that created it.
        Assert.Contains("create the provider's pause with a high `--priority`", skill, StringComparison.Ordinal);
        Assert.Contains("and then end the run", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("Do both, the provider's first", skill, StringComparison.Ordinal);

        // A population that moved is a new decision, and never an edit of the old one.
        Assert.Contains("Read it again with `enrollment.list` and create a new `campaign.activate` for the new number.", skill, StringComparison.Ordinal);

        // Taking a person out of a campaign is a decision for a person, which the manager prepares and never makes.
        Assert.Contains("`enrollment.stop`", skill, StringComparison.Ordinal);
        Assert.Contains("You do not approve it. You created it; a person decides it.", skill, StringComparison.Ordinal);
    }
}
