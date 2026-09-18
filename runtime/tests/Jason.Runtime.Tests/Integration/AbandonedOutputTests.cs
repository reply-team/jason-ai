using Jason.Contracts.Api;
using Jason.Runtime.Execution;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// What happens to a handler when a finished host leaves something behind that still holds its output.
/// <para>
/// A pipe's read end ends only when every writer has let go of it. An agent that can run shell commands starts
/// background ones and does not always wait for them, so the child can exit while a helper it left keeps the
/// pipe open — and a launcher that waits for the pipe is waiting for the helper. The attempt is already over by
/// then; what is still held is a handler, and a handler is a slot of <c>Dispatcher:MaxParallel</c> that comes
/// back only when the runtime restarts. Invisibly, which is the part that matters.
/// </para>
/// </summary>
public class AbandonedOutputTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_host_that_leaves_something_holding_its_pipes_still_frees_its_handler()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("abandoner", Ct, "abandon");

        var item = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "abandoner", context = new { brief = "leave something running" } },
            Ct);

        await host.ScanAsync(Ct);

        // The executor completed the attempt itself, so the work is done whatever its leftovers are doing. What
        // is being measured is that this returns at all: the grace is five seconds and the helper lives for
        // thirty, so a launcher that waited for the pipe would still be waiting when this gives up.
        var done = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Succeeded, Ct);

        Assert.True(
            await FakeHostRuntime.EventuallyAsync(
                async () => (await host.GetAsync(item.Id, Ct)).Attempts![0].Launch?.OutputAbandoned == true,
                Ct,
                timeoutMilliseconds: 20_000),
            "the attempt never recorded that its output was left behind");

        // And the handler is free: the registry holds nothing for an attempt that ended.
        Assert.DoesNotContain(done.Attempts![0].Id, host.Registry.AttemptIds);
    }

    /// <summary>
    /// The deadlock the pumps exist to prevent, actually reached. A pipe nobody empties fills — a few dozen
    /// kilobytes on most systems — and the child then blocks writing into it for ever, so a launcher that reads
    /// only after the child exits waits for a child that is waiting for the launcher. Two megabytes is past any
    /// such buffer by a wide margin; a test that writes a few kilobytes proves the file is written, not that the
    /// thing it is guarding against cannot happen.
    /// </summary>
    [Fact]
    public async Task A_host_that_writes_more_than_a_pipe_holds_is_drained_rather_than_deadlocked()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct);
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("flooder", Ct, "flood", "--megabytes", "2");

        var item = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "flooder", max_attempts = 1, context = new { brief = "say a great deal" } },
            Ct);

        await host.ScanAsync(Ct);

        // It never reports, so the attempt ends the way a silent executor's always has — which it can only do
        // if the child was able to finish writing.
        var ended = await host.WaitForStatusAsync(item.Id, WorkItemStatus.Failed, Ct);
        Assert.Equal(AttemptErrors.ExecutorExited, ended.LastError!.Code);

        var launch = ended.Attempts![0].Launch!;
        Assert.Equal(0, launch.ExitCode);
        Assert.False(launch.OutputAbandoned);

        // Past the cap, so the transcript says where it was cut rather than holding two megabytes.
        Assert.True(launch.StdoutTruncated);
        var transcript = new FileInfo(Path.Combine(launch.WorkDir, "stdout.log"));
        Assert.True(
            transcript.Length < 2 * 1024 * 1024,
            $"the transcript kept {transcript.Length} bytes, so the cap did not hold");
    }
}
