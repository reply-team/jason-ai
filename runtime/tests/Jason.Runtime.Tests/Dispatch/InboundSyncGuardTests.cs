using Jason.Contracts.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Plugins;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Dispatch.InboundSyncHarvestTests;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// What keeps one campaign's read honest when something else is going on: a row another writer moved is left as they
/// left it, a key is one campaign's and not another's, a failure count is cleared only by a read that completed, a page
/// offset belongs to the read that reached it, and a read that cannot be accounted for is not tried again every scan.
/// </summary>
public class InboundSyncGuardTests
{
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Another writer stamped the second campaign's read while the step was starting the first's: the step started
    /// nothing for the second, wrote nothing to its row, and said why.
    /// </summary>
    [Fact]
    public async Task A_campaign_moved_by_another_writer_while_its_read_was_starting_is_left_as_they_left_it()
    {
        using var harness = new DispatchHarness(Noon);
        await ReadableAsync(harness);
        var second = await ReadableAsync(harness);
        DbContext? saving = null;
        harness.InterfereOnceBeforeSaving(
            context => (saving = context) is not null,
            () => saving!.Database.ExecuteSqlRaw("UPDATE campaigns SET inbound_synced_at = '2026-10-06 11:00:00' WHERE id = {0}", second.Id));

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Synced);

        await using var db = harness.Open();
        var row = await db.Campaigns.SingleAsync(c => c.Id == second.Id, Ct);
        Assert.Null(row.InboundSyncItemId);
        Assert.Equal(Noon.AddHours(-1), row.InboundSyncedAt);
        Assert.Equal(1, await db.WorkItems.CountAsync(Ct));
        Assert.Contains($"Campaign {second.PublicId} was moved by another writer while its inbound sync was reading it", harness.Logs.All);
    }

    /// <summary>The same while a read was accounted for: the second campaign's lines are not written, and its read stays.</summary>
    [Fact]
    public async Task A_campaign_moved_by_another_writer_while_its_read_was_accounted_for_says_nothing()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness);
        var second = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page(Thread("th-1", Noon.AddMinutes(-1))));
        await SucceedAsync(harness, second, Page(Thread("th-2", Noon.AddMinutes(-2))));
        DbContext? saving = null;
        harness.InterfereOnceBeforeSaving(
            context => (saving = context) is not null,
            () => saving!.Database.ExecuteSqlRaw("UPDATE campaigns SET inbound_synced_at = '2026-10-06 11:00:00' WHERE id = {0}", second.CampaignId));

        Assert.Equal(1, (await harness.SyncAsync(Ct)).Observed);

        await using var db = harness.Open();
        var lines = await db.Journal.Where(entry => entry.Kind == JournalKinds.InboundReplyObserved).ToListAsync(Ct);
        Assert.Equal(first.CampaignId, Assert.Single(lines).CampaignId);
        Assert.Equal(second.Id, (await db.Campaigns.SingleAsync(c => c.Id == second.CampaignId, Ct)).InboundSyncItemId);
    }

    /// <summary>A key is one campaign's: two campaigns whose reads find the same thread at the same moment each say it.</summary>
    [Fact]
    public async Task Two_campaigns_that_find_the_same_move_each_say_it()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await StartedAsync(harness);
        var second = await StartedAsync(harness);
        await SucceedAsync(harness, first, Page(Thread("th-1", Noon.AddMinutes(-1))));
        await SucceedAsync(harness, second, Page(Thread("th-1", Noon.AddMinutes(-1))));

        Assert.Equal(2, (await harness.SyncAsync(Ct)).Observed);
    }

    /// <summary>
    /// A page that leads to another clears nothing: a read whose second page fails every time would otherwise reset its
    /// count on every first page, and never wait longer than twice the cadence.
    /// </summary>
    [Fact]
    public async Task Only_a_read_that_completes_clears_the_failures()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await SetFailuresAsync(harness, read.CampaignId, 3);
        await SucceedAsync(harness, read, Page(hasMore: true, Thread("th-1", Noon.AddMinutes(-1))));

        await harness.SyncAsync(Ct);
        Assert.Equal(3, await FailuresOfAsync(harness, read.CampaignId));

        await SucceedAsync(harness, await NewestReadAsync(harness), Page(Thread("th-2", Noon.AddMinutes(-2))));
        await harness.SyncAsync(Ct);
        Assert.Equal(0, await FailuresOfAsync(harness, read.CampaignId));
    }

    /// <summary>A page offset belongs to the read that reached it: the next page follows the read's own first page, whatever the row says.</summary>
    [Fact]
    public async Task The_next_page_follows_the_read_s_own_offset()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await using (var db = harness.Open())
        {
            await db.Campaigns.ExecuteUpdateAsync(set => set.SetProperty(c => c.InboundOffset, 7), Ct);
        }

        await SucceedAsync(harness, read, Page(hasMore: true, Thread("th-1", Noon.AddMinutes(-1)), Thread("th-2", Noon.AddMinutes(-2))));
        await harness.SyncAsync(Ct);

        Assert.Equal(2, (int?)(await NewestReadAsync(harness)).Context["input"]!["offset"]);
    }

    /// <summary>A skip is a read that found nothing to fail at: a campaign skipped after reads that failed starts its count again.</summary>
    [Fact]
    public async Task A_skip_clears_the_failures()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await ReadableAsync(harness);
        await SetFailuresAsync(harness, campaign.Id, 5);
        await using (var db = harness.Open())
        {
            await db.ExternalIds.ExecuteDeleteAsync(Ct);
        }

        await harness.SyncAsync(Ct);

        Assert.Equal(0, await FailuresOfAsync(harness, campaign.Id));
    }

    /// <summary>
    /// A read whose accounting throws — a write that will not land — counts as a failed read: it is said once, the
    /// campaign waits longer, and its next read starts again from the first page, rather than the same failure being
    /// tried and said every scan.
    /// </summary>
    [Fact]
    public async Task A_read_that_cannot_be_accounted_for_counts_as_a_failure_and_is_said_once()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await SucceedAsync(harness, read, Page(Thread("th-1", Noon.AddMinutes(-1))));
        harness.InterfereOnceBeforeSaving(() => throw new InvalidOperationException("the writer was busy"));

        Assert.Equal(new Jason.Runtime.Dispatch.InboundReport(0, 0), await harness.SyncAsync(Ct));
        await harness.SyncAsync(Ct);

        await using var db = harness.Open();
        var row = await db.Campaigns.SingleAsync(c => c.Id == read.CampaignId, Ct);
        Assert.Null(row.InboundSyncItemId);
        Assert.Equal(1, row.InboundFailures);
        Assert.Equal(0, row.InboundOffset);
        Assert.Equal(0, await LinesAsync(harness));
        Assert.Single(harness.Logs.Warnings, line => line.StartsWith($"The inbound sync of campaign {row.PublicId} failed", StringComparison.Ordinal));
    }

    /// <summary>
    /// The lookup of what a campaign has already said names the kind as a literal, which is what lets SQLite use the
    /// journal's index for that kind alone: an index with a filter serves a query only when the query's own condition
    /// implies the filter, and a parameter implies nothing.
    /// </summary>
    [Fact]
    public async Task What_a_campaign_has_said_is_looked_up_through_the_index_for_that_kind()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await SucceedAsync(harness, read, Page(Thread("th-1", Noon.AddMinutes(-1))));
        harness.Commands.Clear();

        await harness.SyncAsync(Ct);

        var lookup = Assert.Single(harness.Commands.Texts, text => text.Contains("FROM \"journal\"", StringComparison.Ordinal)
            && text.Contains("\"key\" ", StringComparison.Ordinal)
            && text.StartsWith("SELECT", StringComparison.Ordinal));
        Assert.Contains("\"kind\" = 'inbound_reply_observed'", lookup, StringComparison.Ordinal);

        // And SQLite answers that shape from the index for the kind.
        await using var db = harness.Open();
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(Ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT key FROM journal WHERE campaign_id = 1 AND kind = 'inbound_reply_observed' AND key IN ('a', 'b')";
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(Ct))
        {
            while (await reader.ReadAsync(Ct))
            {
                plan.Add(reader.GetString(reader.GetOrdinal("detail")));
            }
        }

        Assert.Contains(plan, detail => detail.Contains("ix_journal_campaign_kind_key", StringComparison.Ordinal));
    }

    /// <summary>
    /// A read ends at the furthest offset the operation takes, and the watermark moves as for any complete read: a thread
    /// further down the provider's order than a million threads that moved before the read began is not said. A stated
    /// bound, not a page the step asks for and is refused.
    /// </summary>
    [Fact]
    public async Task A_read_that_reaches_the_furthest_offset_ends_there()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await using (var db = harness.Open())
        {
            var item = await db.WorkItems.SingleAsync(row => row.Id == read.Id, Ct);
            item.Context["input"]!["offset"] = Jason.Runtime.Dispatch.InboundSync.MaximumOffset - 1;
            await db.SaveChangesAsync(Ct);
        }

        await SucceedAsync(harness, read, Page(hasMore: true, Thread("th-1", Noon.AddMinutes(-1)), Thread("th-2", Noon.AddMinutes(-2))));
        await harness.SyncAsync(Ct);

        await using var after = harness.Open();
        Assert.Equal(1, await after.WorkItems.CountAsync(Ct));
        var row = await after.Campaigns.SingleAsync(c => c.Id == read.CampaignId, Ct);
        Assert.Null(row.InboundSyncItemId);
        Assert.Equal(Noon.AddHours(-1), row.InboundWatermark);
        Assert.Equal(2, await LinesAsync(harness));
    }

    /// <summary>
    /// What a read listed is what its attempt was claimed with: a context edited while the read ran changes neither the
    /// start the harvest holds it to nor the page the next one follows.
    /// </summary>
    [Fact]
    public async Task A_read_is_accounted_for_by_the_input_it_ran_with_not_by_its_context_since()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await SucceedAsync(harness, read, Page(hasMore: true, Thread("th-old", Noon.AddHours(-1).AddMinutes(-30)), Thread("th-1", Noon.AddMinutes(-1))));
        await using (var db = harness.Open())
        {
            var item = await db.WorkItems.SingleAsync(row => row.Id == read.Id, Ct);
            item.Context = new System.Text.Json.Nodes.JsonObject
            {
                ["input"] = new System.Text.Json.Nodes.JsonObject { ["source"] = "inbound", ["since"] = "2026-10-06T11:00:00Z", ["limit"] = 100, ["offset"] = 40 },
            };
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(2, (await harness.SyncAsync(Ct)).Observed);

        var next = (await NewestReadAsync(harness)).Context["input"]!;
        Assert.Equal(2, (int?)next["offset"]);
        Assert.Equal("2026-10-06T10:00:00Z", (string?)next["since"]);
    }

    /// <summary>
    /// A read whose accounting threw while it was still running is left to finish: counting it as failed would start a
    /// second read beside it.
    /// </summary>
    [Fact]
    public async Task A_read_still_running_when_its_accounting_threw_is_left_to_finish()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        await using (var db = harness.Open())
        {
            await db.WorkItems.Where(row => row.Id == read.Id).ExecuteUpdateAsync(set => set.SetProperty(row => row.Status, WorkItemStatus.Processing), Ct);
        }

        harness.Commands.FailNextWorkItemRead();
        await harness.SyncAsync(Ct);

        await using var after = harness.Open();
        var row = await after.Campaigns.SingleAsync(c => c.Id == read.CampaignId, Ct);
        Assert.Equal(read.Id, row.InboundSyncItemId);
        Assert.Equal(0, row.InboundFailures);
    }

    /// <summary>A live campaign routed to a plugin that reads conversations, linked there, with no read outstanding.</summary>
    private static async Task<Campaign> ReadableAsync(DispatchHarness harness)
    {
        Load(harness, TestPlugins.Loaded("reader", operations: ["conversation.list"]));
        RouteAll(harness, "reader");
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon.AddDays(-1));
        campaign.InboundWatermark = Noon.AddHours(-2);
        db.Campaigns.Add(campaign);
        db.ExternalIds.Add(new ExternalId
        {
            Campaign = campaign,
            PluginId = "reader",
            Kind = "campaign",
            Value = "seq-" + Guid.NewGuid().ToString("N"),
            RecordedAt = Noon.AddDays(-1),
            RecordedByAttemptId = "att_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
        });
        await db.SaveChangesAsync(Ct);
        return campaign;
    }

    private static async Task SetFailuresAsync(DispatchHarness harness, int campaign, int failures)
    {
        await using var db = harness.Open();
        await db.Campaigns.Where(c => c.Id == campaign).ExecuteUpdateAsync(set => set.SetProperty(c => c.InboundFailures, failures), Ct);
    }

    private static async Task<int> FailuresOfAsync(DispatchHarness harness, int campaign)
    {
        await using var db = harness.Open();
        return (await db.Campaigns.SingleAsync(c => c.Id == campaign, Ct)).InboundFailures;
    }
}
