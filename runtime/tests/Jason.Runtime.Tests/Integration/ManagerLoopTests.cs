using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;

namespace Jason.Runtime.Tests.Integration;

/// <summary>
/// The loop end to end, against real child processes: something happens, or enough time passes, and a manager
/// is launched to look at the campaign. Everything here goes through the parts that ship — the chronicle, the
/// summon, the claim, the launcher and the API — so what is proved is that they fit together, not that each of
/// them behaves as its own test says.
/// </summary>
public class ManagerLoopTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Every role launchable through the stand-in host, which is how the built-in manager gets a command
    /// without anybody registering a role called "manager" that already exists. A role with a command of its
    /// own — the ones the tests add — still wins over this.
    /// </summary>
    private static string Settings(int reviewSeconds)
    {
        var command = string.Join(
            ",",
            new[] { "dotnet", Execution.FakeAgentHost.Dll, "manager" }.Select(part => JsonSerializer.Serialize(part)));

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{{\"Dispatcher\":{{\"TickSeconds\":3600,\"RetryDelaySeconds\":0,\"ExitGraceSeconds\":30,"
            + $"\"AiRole\":{{\"TimeoutSeconds\":60,\"HeartbeatSeconds\":10,\"MaxAttempts\":2}}}},"
            + $"\"Roles\":{{\"DefaultEntryCommand\":[{command}]}},"
            + $"\"Manager\":{{\"ReviewSeconds\":{reviewSeconds},\"TimeoutSeconds\":60}}}}");
    }

    /// <summary>
    /// A failure is the manager's inbox. The item fails for real — a child that crashes, an attempt that ends,
    /// a line in the chronicle — and the next scan turns that line into a review, claims it, and launches a
    /// manager that answers in the shape a check-in is held to.
    /// </summary>
    [Fact]
    public async Task A_failed_item_puts_a_manager_on_the_queue_and_a_succeeded_one_does_not()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 86_400));
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-crash", Ct, "crash");
        var doomed = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "fake-crash", max_attempts = 1 },
            Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
        await host.WaitForStatusAsync(doomed.Id, WorkItemStatus.Failed, Ct);
        var failedAttempt = (await host.GetAsync(doomed.Id, Ct)).Attempts!.Single();

        // The next scan reads that line, summons a review, and hands it out in the same pass.
        var report = await host.ScanAsync(Ct);
        Assert.Equal(1, report.Summoned);
        Assert.Equal(1, report.Claimed);

        var checkIn = await CheckInAsync(host, campaign);
        host.Track(checkIn.Id);
        Assert.Equal("triggered", (string?)checkIn.Context["review_intent"]);
        Assert.Equal("workitem_failed", (string?)checkIn.Context["trigger"]);
        Assert.Equal(failedAttempt.Id, (string?)checkIn.Context["cause"]!["attempt_id"]);

        // The chain the review belongs to is the chain of the thing it is about — handed on one hop, exactly
        // as it stands. Nothing in this chain ever ran under a profile (the role's own entry command launched
        // it), so what is handed on is root: the crashed item's own record, not a record invented for the
        // review. That a pinned profile comes through as inherited is proved against a database in
        // ManagerCheckInTests; what matters here is that the record comes from the cause at all.
        Assert.Equal(LineageState.Root, checkIn.Lineage!.State);

        var finished = await host.WaitForStatusAsync(checkIn.Id, WorkItemStatus.Succeeded, Ct);
        Assert.Equal("nothing", (string?)finished.Result!["outcome"]);
        Assert.NotNull((string?)finished.Result["summary"]);
    }

    /// <summary>
    /// And the other half of the same rule: work that went well is not something to wake anybody for. Reviewing
    /// every successful step was rejected as cost without value, and this is where that decision lives.
    /// </summary>
    [Fact]
    public async Task A_succeeded_item_summons_nobody()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 86_400));
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-succeed", Ct, "succeed");
        var good = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "fake-succeed" },
            Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
        await host.WaitForStatusAsync(good.Id, WorkItemStatus.Succeeded, Ct);

        var report = await host.ScanAsync(Ct);

        Assert.Equal(0, report.Summoned);
        Assert.Equal(0, report.Claimed);
    }

    /// <summary>
    /// Silence is the case the cadence exists for: work that simply sits there produces no line at all, so
    /// nothing would ever summon a review of it.
    /// </summary>
    [Fact]
    public async Task A_due_review_is_created_with_nothing_in_the_chronicle_to_cause_it()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 3_600));
        var campaign = await host.CampaignAsync(Ct);

        // Not yet: the campaign went live a moment ago.
        Assert.Equal(0, (await host.ScanAsync(Ct)).Summoned);

        host.Clock.Advance(TimeSpan.FromSeconds(3_600));
        var report = await host.ScanAsync(Ct);
        Assert.Equal(1, report.Summoned);

        var checkIn = await CheckInAsync(host, campaign);
        host.Track(checkIn.Id);
        Assert.Equal("scheduled", (string?)checkIn.Context["review_intent"]);
        Assert.Null(checkIn.Context["trigger"]);
        Assert.Equal(LineageState.Root, checkIn.Lineage!.State);

        var finished = await host.WaitForStatusAsync(checkIn.Id, WorkItemStatus.Succeeded, Ct);
        Assert.Equal("nothing", (string?)finished.Result!["outcome"]);

        // And the loop keeps going: that review is over, the next one falls due, and the campaign is looked at
        // again without anybody asking. That a second one is never created while the first is still open is
        // asserted where no child process can finish it first — in the summoner's own tests.
        host.Clock.Advance(TimeSpan.FromDays(7));
        Assert.Equal(1, (await host.ScanAsync(Ct)).Summoned);
    }

    /// <summary>
    /// A review is work, so it waits its turn like work. The campaign is busy, the check-in is created and not
    /// claimed, and nothing about being a review jumps the queue.
    /// </summary>
    [Fact]
    public async Task A_check_in_for_a_busy_campaign_is_queued_rather_than_skipped()
    {
        await using var host = await FakeHostRuntime.StartAsync(Ct, Settings(reviewSeconds: 3_600));
        var campaign = await host.CampaignAsync(Ct);
        await host.RoleAsync("fake-hang", Ct, "hang");
        // A budget long enough to outlive the clock move below: what is being tested is a campaign that is
        // busy when the review falls due, not one whose lease ran out while nobody was looking.
        var busy = await host.CreateAsync(
            new { campaign_id = campaign, kind = "ai_role", role = "fake-hang", timeout_seconds = 86_400, heartbeat_seconds = 0 },
            Ct);

        Assert.Equal(1, (await host.ScanAsync(Ct)).Claimed);
        await host.WaitAsync(busy.Id, item => item.Status == WorkItemStatus.Processing, Ct, "processing");

        host.Clock.Advance(TimeSpan.FromSeconds(3_600));
        var report = await host.ScanAsync(Ct);

        Assert.Equal(1, report.Summoned);
        Assert.Equal(0, report.Claimed);

        var checkIn = await CheckInAsync(host, campaign);
        host.Track(checkIn.Id);
        Assert.Equal(WorkItemStatus.Created, checkIn.Status);
    }

    private static async Task<WorkItemDto> CheckInAsync(FakeHostRuntime host, string campaignId)
    {
        var page = await host.Fixture.PostOkAsync<Page<WorkItemDto>>(
            Operations.WorkItemList,
            new { campaign_id = campaignId, role = ManagerCheckIn.Role },
            Ct);

        // Read back one by one: a listing leaves lineage out, and lineage is half of what these tests are about.
        return await host.GetAsync(Assert.Single(page.Items!).Id, Ct);
    }
}
