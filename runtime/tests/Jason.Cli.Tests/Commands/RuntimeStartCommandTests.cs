using Jason.Cli.Commands;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests.Commands;

public class RuntimeStartCommandTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_runtime_that_is_already_running_is_reported_and_nothing_is_launched()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var processes = new FakeProcessControl { OnLaunch = _ => throw new InvalidOperationException("a live runtime must never be launched again") };
        var (env, output, error) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Empty(processes.Launches);
        Assert.Equal(RuntimeVerbs.InfoJson("rt_LIVE") + System.Environment.NewLine, output.ToString());
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public async Task Without_a_runtime_one_is_launched_in_the_data_directory_and_awaited()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(dir.Paths.Root, Assert.Single(processes.Launches));
        Assert.Contains("rt_NEW", output.ToString(), StringComparison.Ordinal);
    }

    /// <summary>And what it launches is another copy of this very program.</summary>
    /// <remarks>
    /// The seam takes the executable from its caller because not every caller wants the same one: an applier
    /// starts the build it has just unpacked, at a path of its own. So <c>runtime start</c> has to name its own,
    /// and naming the wrong one is the mistake that hides — a runtime started from the wrong binary answers
    /// every check the right one would, in the same data directory, under the same descriptor.
    /// </remarks>
    [Fact]
    public async Task What_runtime_start_launches_is_this_executable()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, _, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        var launched = Assert.Single(processes.Executables);
        Assert.Equal(SelfExecutable.Command, launched);
        Assert.NotEmpty(launched);
    }

    [Fact]
    public async Task A_stale_descriptor_is_replaced_by_the_instance_that_actually_answers()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_OLD"));

        // The port in the stale descriptor is answered by somebody else entirely, so the claim is not believed.
        var handler = RuntimeVerbs.EchoesTheDescriptor(dir, named => named == "rt_OLD" ? "rt_SOMEONE_ELSE" : named);
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.PublishesAfterAWhile(dir, RuntimeVerbs.Descriptor("rt_NEW")) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, handler, processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Single(processes.Launches);
        var text = output.ToString();
        Assert.Contains("rt_NEW", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rt_OLD", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_that_dies_before_publishing_exits_1_and_points_at_the_logs()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl { OnLaunch = RuntimeVerbs.DiesWith(exitCode: 1) };
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.RuntimeStartFailed, envelope.Code);
        Assert.Contains(dir.Paths.LogsDirectory, envelope.Message, StringComparison.Ordinal);
        Assert.Contains("1", envelope.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_that_never_publishes_a_descriptor_exits_1_after_the_wait()
    {
        using var dir = new TempPaths();
        var processes = new FakeProcessControl();
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), processes);

        var exit = await RuntimeStartCommand.RunAsync(env, human: false, Ct, RuntimeVerbs.ShortTimeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.ApiError, exit);
        var envelope = RuntimeVerbs.Envelope(output.ToString());
        Assert.Equal(CliErrors.RuntimeStartFailed, envelope.Code);
        Assert.Contains(dir.Paths.LogsDirectory, envelope.Message, StringComparison.Ordinal);
        Assert.Single(processes.Launches);
    }

    [Fact]
    public async Task Human_mode_renders_the_running_runtime_for_people()
    {
        using var dir = new TempPaths();
        dir.WriteDescriptor(RuntimeVerbs.Descriptor("rt_LIVE"));
        var (env, output, _) = RuntimeVerbs.Environment(dir, RuntimeVerbs.EchoesTheDescriptor(dir), new FakeProcessControl());

        var exit = await RuntimeStartCommand.RunAsync(env, human: true, Ct, RuntimeVerbs.Timeout, RuntimeVerbs.Poll);

        Assert.Equal(ExitCodes.Success, exit);
        var text = output.ToString();
        Assert.Contains("Runtime:", text, StringComparison.Ordinal);
        Assert.Contains("rt_LIVE", text, StringComparison.Ordinal);
        Assert.Contains(RuntimeVerbs.BaseUrl, text, StringComparison.Ordinal);
        Assert.DoesNotContain("{", text, StringComparison.Ordinal);
    }
}
