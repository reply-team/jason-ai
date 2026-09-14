using Jason.Runtime.Execution;

namespace Jason.Runtime.Tests.Execution;

public class FailureClassifierTests
{
    [Theory]
    [InlineData("lease_expired")]
    [InlineData("heartbeat_missed")]
    [InlineData("executor_exited")]
    [InlineData("rate_limited")]
    [InlineData("provider_unavailable")]
    [InlineData("timeout")]
    [InlineData("transient")]
    public void Losing_the_run_or_hitting_a_transient_condition_is_worth_another_attempt(string code) =>
        Assert.True(FailureClassifier.IsRetriable(code));

    [Theory]
    [InlineData("no_route")]
    [InlineData("role_not_launchable")]
    [InlineData("executor_launch_failed")]
    [InlineData("cancelled")]
    [InlineData("interrupted")]
    [InlineData("bad_input")]
    [InlineData("anything_else")]
    [InlineData("")]
    public void Everything_the_set_does_not_name_is_final(string code) =>
        Assert.False(FailureClassifier.IsRetriable(code));

    [Fact]
    public void The_set_is_the_one_the_wave_decided_and_matching_is_exact()
    {
        Assert.Equal(7, FailureClassifier.RetriableCodes.Count);
        Assert.False(FailureClassifier.IsRetriable("Lease_Expired"));
    }
}
