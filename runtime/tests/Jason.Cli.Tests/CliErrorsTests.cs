using Jason.Cli;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests;

public class CliErrorsTests
{
    [Fact]
    [Critical(Family.R2, "one error envelope on both sides")]
    public void Cli_side_failures_use_the_api_error_envelope() =>
        Assert.Equal(
            "{\"error\":{\"code\":\"no_descriptor\",\"message\":\"gone\",\"retryable\":true}}",
            CliErrors.Serialize(CliErrors.NoDescriptor, "gone", retryable: true));

    [Fact]
    [Critical(Family.R2, "the four exit codes")]
    public void Exit_codes_follow_the_contract()
    {
        Assert.Equal(0, ExitCodes.Success);
        Assert.Equal(1, ExitCodes.ApiError);
        Assert.Equal(2, ExitCodes.Usage);
        Assert.Equal(3, ExitCodes.RuntimeUnavailable);
    }
}
