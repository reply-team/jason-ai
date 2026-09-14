using System.Net;
using Jason.Cli.Commands;
using Jason.Cli.Tests.Process;

namespace Jason.Cli.Tests.Commands;

public class RuntimeStopCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nothing_to_stop_exits_3_with_no_descriptor()
    {
        using var dir = new TempPaths();
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.NeverCalled(), new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Equal(CliErrors.NoDescriptor, RuntimeVerbs.Envelope(output.ToString()).Code);
    }

    [Fact]
    public async Task A_runtime_that_goes_away_prints_the_acknowledgement_and_exits_0()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var body = RuntimeVerbs.ShutdownJson("rt_LIVE");
        var handler = new FakeHandler(_ =>
        {
            // What the runtime does moments after acknowledging: the descriptor goes, then the process.
            File.Delete(dir.Paths.DescriptorFile);
            return RuntimeVerbs.Response(HttpStatusCode.OK, body);
        });
        var (env, output, error) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(body + System.Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task A_process_that_outlives_its_acknowledgement_exits_1_with_shutdown_timeout()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var processes = new FakeProcessControl { RunningPids = { RuntimeVerbs.Pid } };
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE")));
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, processes);

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.ShutdownTimeout, envelope.Code);
        Assert.True(envelope.Retryable);
    }

    [Fact]
    public async Task A_rejected_token_exits_3_with_unauthorized()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.Unauthorized, "{\"error\":{\"code\":\"unauthorized\",\"message\":\"x\",\"retryable\":false}}"));
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.RuntimeUnavailable, exit);
        Assert.Equal(CliErrors.Unauthorized, RuntimeVerbs.Envelope(output.ToString()).Code);
    }

    [Fact]
    public async Task An_api_error_is_printed_verbatim_and_exits_1()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        const string Envelope = "{\"error\":{\"code\":\"internal_error\",\"message\":\"nope\",\"retryable\":true}}";
        var handler = new FakeHandler(_ => RuntimeVerbs.Response(HttpStatusCode.InternalServerError, Envelope));
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Equal(Envelope + System.Environment.NewLine, output.ToString());
    }

    [Fact]
    public async Task Human_mode_names_the_instance_that_stopped()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var handler = new FakeHandler(_ =>
        {
            File.Delete(dir.Paths.DescriptorFile);
            return RuntimeVerbs.Response(HttpStatusCode.OK, RuntimeVerbs.ShutdownJson("rt_LIVE"));
        });
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, new FakeProcessControl());

        var exit = await RuntimeStopCommand.RunAsync(env, human: true, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        var text = output.ToString();
        Assert.Contains("rt_LIVE", text, StringComparison.Ordinal);
        Assert.Contains("77", text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }
}
