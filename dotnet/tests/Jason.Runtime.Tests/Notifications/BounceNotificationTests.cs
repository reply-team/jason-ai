using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// An email that bounced is said by a pointer: the provider events the store said as a bounce in a campaign, counted as
/// unread events are, in the window since the last pointer that reached anybody — and by event, however many campaigns
/// one reached. What the store did not say, history or another kind of event, is not counted.
/// </summary>
public sealed class BounceNotificationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.N3, "bounces are told")]
    public async Task A_bounce_in_three_campaigns_wakes_one_pointer_that_counts_it_once()
    {
        using var harness = await StartedAsync();
        var (store, source) = await WorldAsync(harness, campaigns: 3);
        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));
        Assert.Equal(3, (await store.AllLinesAsync()).Count);

        var request = await HandedOverAsync(harness);
        Assert.True(request is not null, "A bounce woke no pointer.");
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(request, Ct);

        Assert.Equal(1, line!.Bounces);
        Assert.Equal("1 email bounced. Ask your agent what bounced.", Assert.Single(toast.Messages).Text);
        Assert.Equal(1, (await LastLineAsync(harness))["counts"]!["bounces"]!.GetValue<int>());
    }

    [Fact]
    public async Task Two_bounces_are_counted_by_event()
    {
        using var harness = await StartedAsync();
        var (store, source) = await WorldAsync(harness, campaigns: 2);
        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null), Bounced(campaign: null, at: Noon.AddHours(-2))));

        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal(2, line!.Bounces);
    }

    /// <summary>A reply the provider detected and an invitation accepted are events of the same line's kind, and neither is a bounce.</summary>
    [Fact]
    public async Task A_detected_reply_and_an_accepted_invitation_are_no_bounce()
    {
        using var harness = await StartedAsync();
        var (store, source) = await WorldAsync(harness, campaigns: 1);
        await store.StoreAsync(source, null, Page("c-1", Detected(campaign: null), Accepted(campaign: null)));
        Assert.Equal(2, (await store.AllLinesAsync()).Count);

        Assert.Null(await HandedOverAsync(harness));

        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync((await HandedOverAsync(harness))!, Ct);
        Assert.Equal((1, 0), (line!.Approvals, line.Bounces));
    }

    /// <summary>
    /// A bounce from before the campaign was live is history: the store attributes it and says no line, so no pointer is
    /// woken and none counts it.
    /// </summary>
    [Fact]
    public async Task A_bounce_kept_as_history_is_not_counted()
    {
        using var harness = await StartedAsync();
        var (store, source) = await WorldAsync(harness, campaigns: 1);
        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null, at: Noon.AddDays(-40))));
        await using (var db = harness.Open())
        {
            Assert.Equal(1, await db.EventAttributions.CountAsync(Ct));
        }

        Assert.Empty(await store.AllLinesAsync());
        Assert.Null(await HandedOverAsync(harness));

        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync((await HandedOverAsync(harness))!, Ct);
        Assert.Equal(0, line!.Bounces);
    }

    /// <summary>A bounce no pin matches has nowhere to point: kept, said nowhere, and not counted.</summary>
    [Fact]
    public async Task A_bounce_that_reached_no_campaign_is_not_counted()
    {
        using var harness = await StartedAsync();
        var (store, source) = await WorldAsync(harness, campaigns: 1);
        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null, contact: "c_9999")));

        Assert.Null(await HandedOverAsync(harness));
    }

    [Fact]
    public async Task A_pointer_that_reached_nobody_leaves_its_bounces_for_the_next_one()
    {
        using var harness = await StartedAsync();
        var (store, source) = await WorldAsync(harness, campaigns: 2);
        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));
        var unheard = await HandedOverAsync(harness);
        var nobody = await Sender(harness, FakeChannel.Failing("notification_rejected", FailureClass.Permanent)).SendAsync(unheard!, Ct);
        harness.NotifierState.Release(unheard!.NotificationId);
        Assert.Equal(1, nobody!.Bounces);

        await store.StoreAsync(source, "c-1", Page("c-2", Bounced(campaign: null, at: Noon.AddHours(-1))));
        var heard = await HandedOverAsync(harness);
        var toast = FakeChannel.Delivering();
        var reached = await Sender(harness, toast).SendAsync(heard!, Ct);

        Assert.Equal(2, reached!.Bounces);
        Assert.StartsWith("2 emails bounced.", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bounce_is_pointed_at_once_and_not_again()
    {
        using var harness = await StartedAsync();
        var (store, source) = await WorldAsync(harness, campaigns: 1);
        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));
        var first = await HandedOverAsync(harness);
        await Sender(harness, FakeChannel.Delivering()).SendAsync(first!, Ct);
        harness.NotifierState.Release(first!.NotificationId);

        harness.Clock.Advance(TimeSpan.FromHours(1));
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        var later = await HandedOverAsync(harness);
        var toast = FakeChannel.Delivering();
        var line = await Sender(harness, toast).SendAsync(later!, Ct);

        Assert.Equal(0, line!.Bounces);
        Assert.DoesNotContain("bounced", Assert.Single(toast.Messages).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bounce_said_with_approvals_is_said_last_and_left_to_the_agent()
    {
        using var harness = await StartedAsync();
        await Waiting.ApprovalsAsync(harness, 2, Ct);
        var (store, source) = await WorldAsync(harness, campaigns: 1);
        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: null)));
        var toast = FakeChannel.Delivering();

        var line = await Sender(harness, toast).SendAsync((await HandedOverAsync(harness))!, Ct);

        Assert.Equal((2, 1), (line!.Approvals, line.Bounces));
        Assert.Equal(
            "2 approvals are waiting in Jason. 1 email bounced. Ask your agent, or run jason approval list and jason decision list.",
            Assert.Single(toast.Messages).Text);
    }

    /// <summary>
    /// A start that resumes behind the newest line folds what lies between into one burst, and a bounce in what it folds is
    /// pointed at as one read line by line is.
    /// </summary>
    [Fact]
    public async Task A_bounce_written_before_a_start_is_pointed_at_after_it()
    {
        using var harness = new DispatchHarness(Noon);
        harness.NotificationsOn();
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, new JsonObject
        {
            ["notification_id"] = "ntf_earlier",
            ["counts"] = new JsonObject { ["approvals"] = 1, ["decisions"] = 0 },
            ["covered_through"] = covered.PublicId,
            ["channels"] = new JsonArray(),
            ["last_delivered_at"] = null,
            ["delivered_through"] = covered.PublicId,
        });
        var (store, source) = await WorldAsync(harness, campaigns: 2);
        await store.StoreAsync(source, null, Page("c-1", Detected(campaign: null), Bounced(campaign: null)));

        await harness.Runner.ScanOnceAsync(Ct);
        var request = await HandedOverAsync(harness);

        Assert.True(request is not null, "A start that folded a bounce handed nothing over.");
        var line = await Sender(harness, FakeChannel.Delivering()).SendAsync(request, Ct);
        Assert.Equal(1, line!.Bounces);
    }

    /// <summary>
    /// A start that folds only lines of the bounce's kind that are no bounce wakes nothing: the fold reads the kind as the
    /// scan does.
    /// </summary>
    [Fact]
    public async Task A_start_that_folds_only_other_events_wakes_nothing()
    {
        using var harness = new DispatchHarness(Noon);
        harness.NotificationsOn();
        var covered = await harness.WriteLineAsync(JournalKinds.WorkItemCreated, Ct);
        await harness.WriteLineAsync(JournalKinds.NotificationSent, Ct, new JsonObject
        {
            ["notification_id"] = "ntf_earlier",
            ["counts"] = new JsonObject { ["approvals"] = 1, ["decisions"] = 0 },
            ["covered_through"] = covered.PublicId,
            ["channels"] = new JsonArray(),
            ["last_delivered_at"] = null,
            ["delivered_through"] = covered.PublicId,
        });
        var (store, source) = await WorldAsync(harness, campaigns: 1);
        await store.StoreAsync(source, null, Page("c-1", Detected(campaign: null), Accepted(campaign: null)));

        await harness.Runner.ScanOnceAsync(Ct);

        Assert.Null(await HandedOverAsync(harness));
    }

    /// <summary>The account, the campaigns that hold the person, and the store over the harness's database.</summary>
    private static async Task<(EventStoreFixture Store, EventSource Source)> WorldAsync(DispatchHarness harness, int campaigns)
    {
        var store = new EventStoreFixture(harness.Open, harness.Clock);
        var source = await store.SourceAsync();
        var marta = await store.ContactAsync("c_1001");
        for (var index = 0; index < campaigns; index++)
        {
            await store.MemberAsync(marta, await store.CampaignAsync());
        }

        return (store, source);
    }

    private static async Task<DispatchHarness> StartedAsync()
    {
        var harness = new DispatchHarness(Noon);
        harness.NotificationsOn();
        await harness.Runner.ScanOnceAsync(Ct);
        Assert.True(harness.NotifierState.Started);
        return harness;
    }

    private static async Task<NotificationRequest?> HandedOverAsync(DispatchHarness harness)
    {
        var before = harness.Queue.Requests.Count;
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);
        return harness.Queue.Requests.Count > before ? harness.Queue.Requests[^1] : null;
    }

    private static NotificationSender Sender(DispatchHarness harness, params INotificationChannel[] channels) => new(
        harness.Scopes,
        new FakeChannels(channels),
        harness.NotifierState,
        harness.NotificationStatus,
        harness.Resolve<LiveSettings<NotificationsOptions>>(),
        harness.Clock,
        harness.Resolve<ILogger<NotificationSender>>());

    private static async Task<JsonNode> LastLineAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var line = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.NotificationSent).OrderByDescending(e => e.Id).FirstAsync(Ct);
        return line.New!;
    }
}
