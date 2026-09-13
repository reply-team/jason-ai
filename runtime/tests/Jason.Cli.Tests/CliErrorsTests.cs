using Jason.Cli;

namespace Jason.Cli.Tests;

public class CliErrorsTests
{
    [Fact]
    public void Cli_side_failures_use_the_api_error_envelope() =>
        Assert.Equal(
            "{\"error\":{\"code\":\"no_descriptor\",\"message\":\"gone\",\"retryable\":true}}",
            CliErrors.Serialize(CliErrors.NoDescriptor, "gone", retryable: true));

    [Fact]
    public void Exit_codes_follow_the_contract()
    {
        Assert.Equal(0, ExitCodes.Success);
        Assert.Equal(1, ExitCodes.ApiError);
        Assert.Equal(2, ExitCodes.Usage);
        Assert.Equal(3, ExitCodes.RuntimeUnavailable);
    }
}
