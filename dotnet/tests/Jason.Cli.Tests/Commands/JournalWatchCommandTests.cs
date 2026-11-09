using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// <c>jason journal watch</c> where no runtime answers: what it says without a descriptor and how it refuses a usage
/// error. What it prints from a runtime that answers is held against a real one by <c>JournalWatchTests</c>.
/// </summary>
public class JournalWatchCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Without_a_runtime_a_watch_is_exit_3_with_no_descriptor()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["journal", "watch"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains($"\"code\":\"{CliErrors.NoDescriptor}\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_watch_given_an_argument_it_does_not_take_is_a_usage_error()
    {
        using var dir = new TempPaths();

        var exit = await CliApp.RunAsync(["journal", "watch", "cmp_A"], new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths), Ct);

        Assert.Equal(ExitCodes.Usage, exit);
    }
}
