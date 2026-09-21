namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_copywriter_is_told_that_nothing_here_stores_a_message()
    {
        var skill = Flattened("copywriter");

        // Writing is not sending, and there is nowhere to put a draft that anything reads back. A copywriter
        // that believed otherwise would write into its note and report the work done, and the campaign would
        // have messaging nobody can find.
        Assert.Contains("There is no message store and no template store in this build", skill, StringComparison.Ordinal);
        Assert.Contains("Nothing is sent by being written", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("copywriter");
    }
}
