using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// What becomes of a child when the runtime that launched it goes away. A shutdown deliberately does not kill
/// the children it is holding — their leases are still good, and a survivor finishes its work against the next
/// runtime — so the one thing that must not happen is a child that survives for ever: on Windows the handles it
/// inherited keep the test runner from seeing end of file, and a suite that finished in minutes sits there for
/// as long as the child lives.
/// </summary>
/// <remarks>
/// The lease is not the signal. While the runtime is alive the lease enforcer already ends an overdue attempt
/// and kills its child, and a host told to hang keeps its lease healthy by heartbeating — so a child that gave
/// up on its own lease would be answering a question the runtime answers better, and would change what the
/// lease and timeout tests are asserting. The signal is the runtime itself being gone.
/// </remarks>
public class FakeHostLifetimeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_host_the_runtime_walked_away_from_does_not_outlive_it()
    {
        var host = await FakeHostRuntime.StartAsync(Ct);
        var pid = 0;
        try
        {
            var campaign = await host.CampaignAsync(Ct);
            await host.RoleAsync("fake-mute", Ct, "mute");
            var created = await host.CreateAsync(
                new { campaign_id = campaign, kind = "ai_role", role = "fake-mute" },
                Ct);

            Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);

            // A muted host neither reports nor exits, which is the point of it: it is what a missed heartbeat
            // looks like. So the pid comes from the line it writes before it goes quiet.
            pid = await host.RunningPidAsync(created.Id, Ct);
            Assert.True(FakeHostRuntime.IsRunning(pid));

            // The runtime stops with the child still running — a drain abandons it by design. Nothing else is
            // disposed here: what is being asserted is the child's own doing, and a fixture that killed it
            // first would prove only that the fixture works.
            await host.StopRuntimeAsync();
            await FakeHostRuntime.AssertGoneAsync(pid, Ct);
        }
        finally
        {
            if (pid > 0)
            {
                FakeHostRuntime.Kill(pid);
            }
        }
    }

    [Fact]
    public async Task The_fixture_leaves_no_child_behind()
    {
        int pid;
        var host = await FakeHostRuntime.StartAsync(Ct);
        await using (host)
        {
            var campaign = await host.CampaignAsync(Ct);
            await host.RoleAsync("fake-hang", Ct, "hang");
            var created = await host.CreateAsync(
                new { campaign_id = campaign, kind = "ai_role", role = "fake-hang" },
                Ct);

            Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
            pid = await host.RunningPidAsync(created.Id, Ct);
            await host.WaitAsync(created.Id, item => item.Status == WorkItemStatus.Processing, Ct, "processing");
        }

        // Disposal cancelled the work, moved the clock and then killed by pid. A hanging host is the case the
        // fixture has to catch by itself, because nothing else will.
        await FakeHostRuntime.AssertGoneAsync(pid, Ct);
    }
}
