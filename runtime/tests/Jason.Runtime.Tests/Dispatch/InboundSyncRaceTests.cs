using Jason.Runtime.Dispatch;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Dispatch.InboundSyncHarvestTests;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The inbound read and the events store both say a moved thread, under one key, and the database refuses a second line
/// under it. Each reads what has been said inside the transaction it writes in, and both transactions take the writer
/// when they begin, so whichever writes second sees what the first said: the race between them costs neither a failure.
/// </summary>
public class InboundSyncRaceTests
{
    private static readonly DateTime Noon = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_line_the_events_store_writes_while_a_read_is_accounted_for_costs_the_read_nothing()
    {
        using var harness = new DispatchHarness(Noon);
        var read = await StartedAsync(harness);
        var moved = Noon.AddMinutes(-5);
        await SucceedAsync(harness, read, Page(Thread("th-1", moved)));
        var key = InboundSync.ObservationKey("reader", "th-1", InboundSync.Instant(new DateTimeOffset(moved)));

        // The store, writing the same move on a connection of its own, just as the read looks at what it has said.
        bool? landed = null;
        harness.Commands.BeforeNextJournalRead(() => landed = TryWriteLine(harness, read.CampaignId, key));

        await harness.SyncAsync(Ct);

        Assert.True(landed is false, $"The store's line {(landed is null ? "was never tried" : "landed between the read's look at what it had said and its write")}.");
        Assert.Equal(1, await LinesAsync(harness));
        await using var db = harness.Open();
        var row = await db.Campaigns.SingleAsync(campaign => campaign.Id == read.CampaignId, Ct);
        Assert.True(
            row.InboundFailures == 0 && row.InboundSyncItemId is null,
            $"The read was not accounted for: {row.InboundFailures} failures, read {row.InboundSyncItemId} outstanding.");
    }

    /// <summary>
    /// Writes the line the events store would, on a connection that waits a second for the writer: true when it landed,
    /// false when another transaction held the writer throughout.
    /// </summary>
    private static bool TryWriteLine(DispatchHarness harness, int campaign, string key)
    {
        string file;
        using (var db = harness.Open())
        {
            file = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, DefaultTimeout = 1 }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, campaign_id, key) VALUES ('jrn_01JB6K8TQ2W9V4MZ0C3Y7H5NRX', '2026-10-06 12:00:00', 'system', 'dispatcher', 'inbound_reply_observed', {campaign}, '{key}')";
        try
        {
            command.ExecuteNonQuery();
            return true;
        }
        catch (SqliteException refused) when (refused.SqliteErrorCode is 5 or 6)
        {
            return false;
        }
    }
}
