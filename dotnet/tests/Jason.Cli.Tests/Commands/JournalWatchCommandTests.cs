using System.Net;
using System.Net.Sockets;
using Jason.Cli.Http;
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

    /// <summary>
    /// A runtime that is gone, and one that takes the connection and never answers, are both unreachable: the watch gives
    /// the upgrade the ordinary wait and no longer, though the stream it opens then runs as long as it is read, and so does
    /// the verb that asks the runtime what it says about itself.
    /// </summary>
    [Fact]
    public async Task A_watch_whose_runtime_is_gone_or_never_answers_the_upgrade_is_exit_3_runtime_unreachable()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(new RuntimeDescriptor("v1", "0.1.0-dev", "rt_GONE", 77, $"http://127.0.0.1:{FreedPort()}", "the-token", DateTimeOffset.UnixEpoch));
        var output = new StringWriter();

        var exit = await CliApp.RunAsync(["journal", "watch"], new CliEnvironment(output, new StringWriter(), dir.Paths), Ct);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Contains($"\"code\":\"{CliErrors.RuntimeUnreachable}\"", output.ToString(), StringComparison.Ordinal);

        string[][] verbs = [["journal", "watch"], ["runtime", "status"]];
        foreach (var verb in verbs)
        {
            var clock = new FixedClock(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
            var asked = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var silent = new StringWriter();
            var run = CliApp.RunAsync(verb, new CliEnvironment(silent, new StringWriter(), dir.Paths, new Silent(asked), Clock: clock), Ct);
            var request = await asked.Task.WaitAsync(Ct);
            clock.Advance(RuntimeClient.Ordinary);

            Assert.True(request.IsCancellationRequested, $"'{string.Join(' ', verb)}' was still waiting after the ordinary wait ran out");
            Assert.Equal(ExitCodes.RuntimeUnavailable, await run.WaitAsync(Ct));
            Assert.Contains($"\"code\":\"{CliErrors.RuntimeUnreachable}\"", silent.ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>A runtime that takes the upgrade and answers only when it is given up on.</summary>
    private sealed class Silent(TaskCompletionSource<CancellationToken> asked) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            asked.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
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
