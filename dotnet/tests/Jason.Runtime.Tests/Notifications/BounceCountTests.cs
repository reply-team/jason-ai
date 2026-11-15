using System.Text.Json.Nodes;
using Jason.Contracts.Ids;
using Jason.Runtime.Notifications;
using Jason.Runtime.Tests.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The emails that bounced in a pointer's window are counted by the database, never by reading every line of the kind
/// into memory: a window grows for as long as no pointer reaches anybody. The count keeps a line exactly where
/// <see cref="BounceLines.Bounced"/> keeps its payload, counts an event once by its key, and is read through the kind
/// index bounded by the window.
/// </summary>
public sealed class BounceCountTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{"kind":"bounce"}""", 1)]
    [InlineData("""{"kind":"bounce","bounce":"hard"}""", 1)]
    [InlineData("""{"kind":"bo\u0075nce"}""", 1)]
    [InlineData("""{"kind":"Bounce"}""", 0)]
    [InlineData("""{"kind":"bounce "}""", 0)]
    [InlineData("""{"kind":"reply_detected"}""", 0)]
    [InlineData("""{"kind":7}""", 0)]
    [InlineData("""{"kind":true}""", 0)]
    [InlineData("""{"kind":null}""", 0)]
    [InlineData("""{"kind":{"kind":"bounce"}}""", 0)]
    [InlineData("""{"kind":["bounce"]}""", 0)]
    [InlineData("""{"event":{"kind":"bounce"}}""", 0)]
    [InlineData("""{}""", 0)]
    [InlineData(null, 0)]
    public async Task A_line_counts_exactly_where_its_payload_is_a_bounce(string? payload, int expected)
    {
        using var database = new TestDatabase();
        var id = await WriteAsync(database, "evt-1", payload);

        await using var read = database.Open();
        var counted = await BounceLines.Count(read, 0, id).SingleAsync(Ct);

        Assert.Equal(expected, counted);
        Assert.Equal(BounceLines.Bounced(payload is null ? null : JsonNode.Parse(payload)) ? 1 : 0, counted);
    }

    [Fact]
    public async Task An_event_is_counted_once_and_only_inside_the_window()
    {
        using var database = new TestDatabase();
        var ids = new List<int>();
        foreach (var (key, kind) in new[]
                 {
                     ("evt-before", "bounce"), ("evt-1", "bounce"), ("evt-1", "bounce"), ("evt-2", "bounce"),
                     ("evt-3", "reply_detected"), ("evt-after", "bounce"),
                 })
        {
            ids.Add(await WriteAsync(database, key, $$"""{"kind":"{{kind}}"}"""));
        }

        await using var read = database.Open();

        Assert.Equal(2, await BounceLines.Count(read, ids[0], ids[4]).SingleAsync(Ct));
        Assert.Equal(4, await BounceLines.Count(read, 0, ids[^1]).SingleAsync(Ct));
        Assert.Equal(0, await BounceLines.Count(read, ids[^1], ids[^1]).SingleAsync(Ct));
    }

    /// <summary>
    /// The count reads the journal through the kind index bounded by the window on both sides, never the whole table, and
    /// sorts nothing but the keys it counts apart.
    /// </summary>
    [Fact]
    public async Task The_count_reads_the_kind_index_bounded_by_the_window()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var plan = await QueryPlans.PlanAsync(db, BounceLines.Count(db, 10, 20).ToQueryString(), Ct);

        var shown = string.Join(" | ", plan);
        Assert.True(
            plan.Any(detail => detail.Contains("SEARCH journal USING INDEX ix_journal_kind (kind=? AND rowid>? AND rowid<?)", StringComparison.Ordinal)),
            $"The plan is: {shown}");
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN", StringComparison.Ordinal));
        Assert.All(
            plan.Where(detail => detail.Contains("TEMP B-TREE", StringComparison.Ordinal)),
            detail => Assert.Equal("USE TEMP B-TREE FOR count(DISTINCT)", detail));
    }

    /// <summary>
    /// One line of the kind, about no campaign, written past the writer's check: the journal's documents refuse most of
    /// these payloads at the write, but a line already in a database (a hand edit, an earlier version) is counted all the
    /// same, so the count is held to <see cref="BounceLines.Bounced"/> on every shape a payload can take.
    /// </summary>
    private static async Task<int> WriteAsync(TestDatabase database, string key, string? payload)
    {
        await using var db = database.Open();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, key, new_json) VALUES ({PublicId.New("jrn")}, '2026-11-15 12:00:00', 'system', 'events', {BounceLines.Kind}, {key}, {payload})",
            Ct);
        return await db.Database.SqlQuery<int>($"SELECT max(id) AS \"Value\" FROM journal").SingleAsync(Ct);
    }
}
