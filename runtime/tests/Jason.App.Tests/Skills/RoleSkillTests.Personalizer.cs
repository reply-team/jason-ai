namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_personalizer_is_told_that_nothing_here_stores_a_draft_about_a_person()
    {
        var skill = Flattened("personalizer");

        // The role most likely to write about a person into the person's own record, which is their data and
        // is read by everything else that touches them.
        Assert.Contains("There is no per-contact draft store in this build", skill, StringComparison.Ordinal);
        Assert.Contains("is not a place to park drafts about them", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("personalizer");
    }
}
