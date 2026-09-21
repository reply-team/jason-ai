namespace Jason.App.Tests.Skills;

public partial class RoleSkillTests
{
    [Fact]
    public void The_analyst_is_told_that_this_build_records_no_metrics()
    {
        var skill = Flattened("analyst");

        // The role whose job description implies a dashboard. An analyst that assumed opens and clicks were
        // somewhere would either hunt for them or, worse, report plausible ones.
        Assert.Contains("There is no metrics store in this build", skill, StringComparison.Ordinal);

        // And the one distinction that keeps its answer honest: a report is somebody's word, not a measurement.
        Assert.Contains("an admission, not a measurement", skill, StringComparison.Ordinal);

        CarriesTheLaunchContract("analyst");
    }
}
