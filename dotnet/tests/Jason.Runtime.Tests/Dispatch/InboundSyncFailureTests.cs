using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Journal;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Dispatch.InboundSyncHarvestTests;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A read that ended without a page. The campaign keeps where its read started and tries again later each time — the
/// cadence doubled for each failure in a row, at most a day — and the next read starts again from its first page: a
/// thread that moved while the read waited is listed first, and a read that skipped the first pages would never list it.
/// </summary>
public class InboundSyncFailureTests
{
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private const string Sentinel = "SENTINEL_UNREADABLE_7f3c";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(WorkItemStatus.Failed)]
    [InlineData(WorkItemStatus.Cancelled)]
    [InlineData(WorkItemStatus.Expired)]
    public async Task A_read_that_ended_without_a_page_counts_a_failure_and_starts_again_from_the_first_page(WorkItemStatus ended)
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page(hasMore: true, Thread("th-1", Noon.AddMinutes(-1))));
        await harness.SyncAsync(Ct);
        var second = await NewestReadAsync(harness);
        await EndAsync(harness, second, ended);

        Assert.Equal(new InboundReport(0, 0), await harness.SyncAsync(Ct));

        await using (var db = harness.Open())
        {
            var row = await db.Campaigns.SingleAsync(c => c.Id == first.CampaignId, Ct);
            Assert.Null(row.InboundSyncItemId);
            Assert.Equal(1, row.InboundFailures);
            Assert.Equal(0, row.InboundOffset);
            Assert.Equal(Noon, row.InboundSyncedAt);
            Assert.Equal(Noon.AddHours(-2), row.InboundWatermark);
        }

        // Twice the cadence after the failed read started, and not before: from the first page, from the same start.
        harness.Clock.Advance(TimeSpan.FromSeconds(1_799));
        Assert.Equal(0, (await harness.SyncAsync(Ct)).Synced);
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        var retry = await NextReadAsync(harness);
        Assert.Equal(
            """{"input":{"source":"inbound","since":"2026-10-06T10:00:00Z","limit":100,"offset":0}}""",
            retry.Context.ToJsonString());
    }

    /// <summary>
    /// A page offset belongs to the read it was reached by. One left on a campaign with no read outstanding is never
    /// carried into a new read, whose start may be another: it would skip that many of the threads the new read lists.
    /// </summary>
    [Fact]
    public async Task A_new_read_starts_from_the_first_page_whatever_offset_was_left()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page());
        await harness.SyncAsync(Ct);
        await using (var db = harness.Open())
        {
            await db.Campaigns.ExecuteUpdateAsync(set => set.SetProperty(c => c.InboundOffset, 7), Ct);
        }

        harness.Clock.Advance(TimeSpan.FromSeconds(900));
        var next = await NextReadAsync(harness);

        Assert.Equal(0, (int?)next.Context["input"]!["offset"]);
    }

    [Fact]
    public async Task Failures_in_a_row_add_up_and_a_read_that_succeeds_clears_them()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        for (var failure = 1; failure <= 3; failure++)
        {
            await EndAsync(harness, read, WorkItemStatus.Failed);
            await harness.SyncAsync(Ct);
            Assert.Equal(failure, await FailuresAsync(harness, read.CampaignId));

            harness.Clock.Advance(TimeSpan.FromSeconds(900 * (1 << failure)));
            read = await NextReadAsync(harness);
        }

        await SucceedAsync(harness, read, Page());
        await harness.SyncAsync(Ct);

        Assert.Equal(0, await FailuresAsync(harness, read.CampaignId));
    }

    /// <summary>
    /// A read that succeeded with something that is not a page — the result was held to the operation's schema when it
    /// ended, so this is a fault — is a failed read. The log says what kind of failure it was, never what the result held.
    /// </summary>
    [Theory]
    [InlineData("""{"items":"SENTINEL_UNREADABLE_7f3c","has_more":false,"vendor":{"ordering":"last_activity_desc"}}""")]
    [InlineData("""{"items":[],"vendor":{"ordering":"last_activity_desc"}}""")]
    [InlineData("""{"items":[{"external_id":"th-1","last_activity_at":"SENTINEL_UNREADABLE_7f3c"}],"has_more":false}""")]
    public async Task A_result_that_is_not_a_page_is_a_failed_read_said_by_its_kind(string result)
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await SucceedAsync(harness, read, JsonNode.Parse(result)!.AsObject());

        Assert.Equal(new InboundReport(0, 0), await harness.SyncAsync(Ct));

        Assert.Equal(1, await FailuresAsync(harness, read.CampaignId));
        Assert.Equal(0, await LinesAsync(harness));
        Assert.Contains(harness.Logs.All, line => line.StartsWith($"The inbound read of campaign ", StringComparison.Ordinal));
        Assert.False(harness.Logs.Mentions(Sentinel));
    }

    /// <summary>A read whose attempt recorded no plugin cannot say which plugin's thread it found: a failed read too.</summary>
    [Fact]
    public async Task A_read_whose_attempt_names_no_plugin_is_a_failed_read()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await SucceedAsync(harness, read, Page(Thread("th-1", Noon.AddMinutes(-1))));
        await using (var db = harness.Open())
        {
            var attempt = await db.Attempts.SingleAsync(Ct);
            attempt.Provenance = attempt.Provenance! with { PluginId = null };
            await db.SaveChangesAsync(Ct);
        }

        await harness.SyncAsync(Ct);

        Assert.Equal(1, await FailuresAsync(harness, read.CampaignId));
        Assert.Equal(0, await LinesAsync(harness));
    }

    /// <summary>
    /// A failed read is the manager's to know about, through the rule every failure goes through: its failure line wakes
    /// one review, and another failure while that review is open wakes no second one.
    /// </summary>
    [Fact]
    public async Task A_failed_read_wakes_one_review_like_any_failure()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await FailedLineAsync(harness, read);

        Assert.Equal(1, await harness.SummonAsync(Ct));

        harness.Clock.Advance(TimeSpan.FromSeconds(1_800));
        var retry = await NextReadAsync(harness);
        await FailedLineAsync(harness, retry);
        Assert.Equal(0, await harness.SummonAsync(Ct));

        await using var db = harness.Open();
        Assert.Equal(1, await db.WorkItems.CountAsync(item => item.Role == ManagerCheckIn.Role, Ct));
    }

    /// <summary>
    /// The case starting again from the first page is for. A read of three pages fails on its second; while it waits two
    /// hours to try again, a thread from its third page moves and goes to the top of the provider's order. A read that
    /// resumed at the page it failed on would never list it, and the read after starts an hour before this one: the move
    /// would never be said. Starting again from the first page says it, once.
    /// </summary>
    [Fact]
    public async Task A_thread_that_moves_while_a_failed_read_waits_is_said_exactly_once()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Inbound.CurrentValue.SyncSeconds = 3_600;
        harness.Inbound.CurrentValue.LookbackSeconds = 3_600;
        var threads = new List<(string Id, DateTime Moved)>
        {
            ("th-1", Noon.AddMinutes(-1)), ("th-2", Noon.AddMinutes(-2)),
            ("th-3", Noon.AddMinutes(-3)), ("th-4", Noon.AddMinutes(-4)),
            ("th-5", Noon.AddMinutes(-5)), ("th-6", Noon.AddMinutes(-6)),
        };

        var read = await StartedAsync(harness);
        await ProvideAsync(harness, read, threads);
        await harness.SyncAsync(Ct);
        await EndAsync(harness, await NewestReadAsync(harness), WorkItemStatus.Failed);
        await harness.SyncAsync(Ct);

        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        threads.RemoveAll(thread => thread.Id == "th-5");
        threads.Insert(0, ("th-5", Noon.AddMinutes(10)));

        harness.Clock.Advance(TimeSpan.FromSeconds(7_200 - 600));
        read = await NextReadAsync(harness);
        while (read is not null)
        {
            await ProvideAsync(harness, read, threads);
            await harness.SyncAsync(Ct);
            read = await OutstandingAsync(harness, read.CampaignId);
        }

        harness.Clock.Advance(TimeSpan.FromSeconds(3_600));
        read = await NextReadAsync(harness);
        Assert.Equal("2026-10-06T13:00:00Z", (string?)read.Context["input"]!["since"]);
        await ProvideAsync(harness, read, threads);
        await harness.SyncAsync(Ct);

        await using var db = harness.Open();
        var moves = (await db.Journal.Where(entry => entry.Kind == JournalKinds.InboundReplyObserved).ToListAsync(Ct))
            .Where(entry => (string?)entry.New!["thread"] == "th-5")
            .Select(entry => (string?)entry.New!["last_activity_at"])
            .ToList();
        Assert.Equal(["2026-10-06T12:10:00.000Z"], moves);
    }

    /// <summary>
    /// The provider's side of a read: the threads that moved at or after the read's start, most recent first, two to a
    /// page from the read's own offset.
    /// </summary>
    private static async Task ProvideAsync(DispatchHarness harness, WorkItem read, List<(string Id, DateTime Moved)> threads)
    {
        var input = read.Context["input"]!;
        var since = DateTime.Parse((string)input["since"]!, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var offset = (int)input["offset"]!;
        var listed = threads.Where(thread => thread.Moved >= since).OrderByDescending(thread => thread.Moved).ToList();
        var page = listed.Skip(offset).Take(2).Select(thread => Thread(thread.Id, thread.Moved)).ToArray();
        await SucceedAsync(harness, read, Page(hasMore: offset + page.Length < listed.Count, page));
    }

    private static async Task<WorkItem?> OutstandingAsync(DispatchHarness harness, int campaign)
    {
        await using var db = harness.Open();
        var pointer = (await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct)).InboundSyncItemId;
        return pointer is null ? null : await db.WorkItems.AsNoTracking().SingleAsync(item => item.Id == pointer, Ct);
    }

    private static async Task EndAsync(DispatchHarness harness, WorkItem read, WorkItemStatus ended)
    {
        await using var db = harness.Open();
        var item = await db.WorkItems.SingleAsync(row => row.Id == read.Id, Ct);
        item.Status = ended;
        await db.SaveChangesAsync(Ct);
    }

    /// <summary>The read failed as the ordinary routine fails one: final, with its failure line in the campaign's chronicle.</summary>
    private static async Task FailedLineAsync(DispatchHarness harness, WorkItem read)
    {
        await EndAsync(harness, read, WorkItemStatus.Failed);
        await using var db = harness.Open();
        var item = await db.WorkItems.Include(row => row.Attempts).SingleAsync(row => row.Id == read.Id, Ct);
        var attempt = WorkItemFactory.NewAttempt(item, item.Attempts.Count + 1, AttemptStatus.Failed, harness.Clock.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(Ct);
        new JournalWriter(harness.Clock).Append(
            db,
            Jason.Runtime.Domain.Actors.Dispatcher,
            JournalKinds.WorkItemFailed,
            campaign: null,
            key: attempt.PublicId,
            updated: DocumentedLines.Failure(),
            workItem: item,
            attempt: attempt);
        await db.SaveChangesAsync(Ct);
        await harness.SyncAsync(Ct);
    }

    private static async Task<int> FailuresAsync(DispatchHarness harness, int campaign)
    {
        await using var db = harness.Open();
        return (await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct)).InboundFailures;
    }
}
