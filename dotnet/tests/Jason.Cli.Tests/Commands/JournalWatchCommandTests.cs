using System.Net;
using System.Net.Sockets;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// <c>jason journal watch</c> where no runtime answers: what it says without a descriptor and with one whose runtime is
/// gone, and how it refuses a usage error. What it prints from a runtime that answers is held against a real one by
/// <c>JournalWatchTests</c>.
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
    public async Task A_watch_whose_runtime_is_gone_is_exit_3_runtime_unreachable()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(new RuntimeDescriptor("v1", "0.1.0-dev", "rt_GONE", 77, $"http://127.0.0.1:{FreedPort()}", "the-token", DateTimeOffset.UnixEpoch));
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["journal", "watch"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains($"\"code\":\"{CliErrors.RuntimeUnreachable}\"", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_watch_given_an_argument_it_does_not_take_is_a_usage_error()
    {
        using var dir = new TempPaths();

        var exit = await CliApp.RunAsync(["journal", "watch", "cmp_A"], new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths), Ct);

        Assert.Equal(ExitCodes.Usage, exit);
    }

    /// <summary>A loopback port a listener held a moment ago and gave back, so nothing answers on it.</summary>
    private static int FreedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
