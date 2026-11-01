using System.Net;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Commands;

public class RuntimeRestartCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Restarting_something_that_is_not_running_simply_starts_it()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeRestartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(processes.Launches);

        // "Not running" is not a failure of a restart, so the stop step's envelope never reaches the caller.
        var text = output.ToString();
        Assert.Contains("rt_NEW", text, StringComparison.Ordinal);
        Assert.DoesNotContain(CliErrors.NoDescriptor, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_live_runtime_is_stopped_and_a_new_instance_is_started()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD"));
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("system.shutdown", StringComparison.Ordinal))
            {
                File.Delete(dir.Paths.DescriptorFile);
                return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_OLD"));
            }

            var current = new Jason.Cli.Discovery.DescriptorReader(dir.Paths).Read()
                ?? throw new HttpRequestException("connection refused");
            return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson(current.InstanceId));
        });
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, processes);

        var exit = await RuntimeRestartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(processes.Launches);
        var text = output.ToString();
        Assert.Contains("rt_NEW", text, StringComparison.Ordinal);
        Assert.DoesNotContain("stopping", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A runtime that stopped but could not remove its descriptor has stopped. A restart used to time out there and
    /// start nothing; it starts the new instance now, and the stop's line about the descriptor reaches standard error.
    /// The old descriptor's address answers nothing, so the start replaces it as it replaces any stale one.
    /// </summary>
    [Fact]
    public async Task A_runtime_that_stopped_but_left_its_descriptor_behind_is_restarted()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD"));
        var stopped = false;
        var handler = new FakeHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("system.shutdown", StringComparison.Ordinal))
            {
                // The process goes; the descriptor stays, as one held by another program does.
                stopped = true;
                return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_OLD"));
            }

            var current = new Jason.Cli.Discovery.DescriptorReader(dir.Paths).Read();
            if (current is null || (stopped && current.InstanceId == "rt_OLD"))
            {
                throw new HttpRequestException("connection refused");
            }

            return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.InfoJson(current.InstanceId));
        });
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, error) = RuntimeVerbs.Environment(dir, handler, processes);

        var exit = await RuntimeRestartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(processes.Launches);
        Assert.Contains("rt_NEW", output.ToString(), StringComparison.Ordinal);
        Assert.Contains(
            $"Its endpoint descriptor at '{dir.Paths.DescriptorFile}' was left behind: the runtime could not remove it.",
            error.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stop_that_fails_leaves_the_runtime_alone_and_reports_why()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        const string Envelope = "{\"error\":{\"code\":\"internal_error\",\"message\":\"nope\",\"retryable\":true}}";
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.InternalServerError, Envelope));
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("nothing was stopped, so nothing may be started") };
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, processes);

        var exit = await RuntimeRestartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Empty(processes.Launches);
        Assert.Equal(Envelope + System.Environment.NewLine, output.ToString());
    }
}
