using System.Text.Json.Nodes;
using Jason.Contracts.Ids;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Tests.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The replies in a pointer's window are counted by the database, never by reading every verdict into memory: a window grows
/// for as long as no pointer reaches anybody. The count keeps a line exactly where <see cref="ReplyLines.Counts"/> keeps it —
/// a verdict a run recorded that says a person wrote it themselves, and not the chat's — counts a reply once by its key, and
/// leaves out a reply whose key was counted at or before the window's start.
/// </summary>
public sealed class ReplyCountTests
{
    private const string Attempt = "att_01JC0000000000000000000001";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("""{"nature":"person"}""", true, 1)]
    [InlineData("{\"nature\":\"pers\\u006fn\"}", true, 1)]
    [InlineData("""{"nature":"person","confined":false}""", true, 1)]
    [InlineData("""{"nature":"person","confined":true}""", true, 0)]
    [InlineData("""{"nature":"person"}""", false, 0)]
    [InlineData("""{"nature":"Person"}""", true, 0)]
    [InlineData("""{"nature":"automatic"}""", true, 0)]
    [InlineData("""{"nature":"bounce"}""", true, 0)]
    [InlineData("""{"nature":"opt_out"}""", true, 0)]
    [InlineData("""{"nature":null}""", true, 0)]
    [InlineData("""{"nature":7}""", true, 0)]
    [InlineData("""{"nature":["person"]}""", true, 0)]
    [InlineData("""{}""", true, 0)]
    [InlineData(null, true, 0)]
    public async Task A_line_counts_exactly_where_it_is_a_run_s_verdict_that_a_person_wrote(string? payload, bool inARun, int expected)
    {
        using var database = new TestDatabase();
        var attempt = inARun ? Attempt : null;
        var id = await WriteAsync(database, "sha256:reply-1", payload, attempt);

        await using var read = database.Open();
        var counted = await ReplyLines.Count(read, 0, id).SingleAsync(Ct);

        Assert.Equal(expected, counted);
        Assert.Equal(ReplyLines.Counts(payload is null ? null : JsonNode.Parse(payload), attempt) ? 1 : 0, counted);
    }

    [Fact]
    public async Task A_reply_is_counted_once_inside_the_window_and_not_again_after_it()
    {
        using var database = new TestDatabase();
        var ids = new List<int>();
        foreach (var (key, nature, attempt) in new[]
                 {
                     ("sha256:before", "person", Attempt),
                     ("sha256:said-by-a-person", "person", null),
                     ("sha256:one", "person", Attempt),
                     ("sha256:one", "person", Attempt),
                     ("sha256:before", "person", Attempt),
                     ("sha256:said-by-a-person", "person", Attempt),
                     ("sha256:two", "automatic", Attempt),
                     ("sha256:after", "person", Attempt),
                 })
        {
            ids.Add(await WriteAsync(database, key, $$"""{"nature":"{{nature}}"}""", attempt));
        }

        await using var read = database.Open();

        // After the first two lines: "one" twice is one, "before" was counted before the window, and the reply a person
        // recorded outside a run was never counted, so its verdict from a run counts now.
        Assert.Equal(2, await ReplyLines.Count(read, ids[1], ids[6]).SingleAsync(Ct));
        Assert.Equal(4, await ReplyLines.Count(read, 0, ids[^1]).SingleAsync(Ct));
        Assert.Equal(0, await ReplyLines.Count(read, ids[^1], ids[^1]).SingleAsync(Ct));
        Assert.Equal(1, await ReplyLines.Count(read, ids[6], ids[^1]).SingleAsync(Ct));
    }

    /// <summary>
    /// The count reads the journal through the kind index, the window bounded on both sides and what came before it bounded
    /// by the window's start, never the whole table.
    /// </summary>
    [Fact]
    public async Task The_count_reads_the_kind_index_bounded_by_the_window()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var plan = await QueryPlans.PlanAsync(db, ReplyLines.Count(db, 10, 20).ToQueryString(), Ct);

        var shown = string.Join(" | ", plan);
        Assert.True(
            plan.Any(detail => detail.Contains("SEARCH journal USING INDEX ix_journal_kind (kind=? AND rowid>? AND rowid<?)", StringComparison.Ordinal)),
            $"The plan is: {shown}");
        Assert.True(
            plan.Any(detail => detail.Contains("SEARCH journal USING INDEX ix_journal_kind (kind=? AND rowid<?)", StringComparison.Ordinal)),
            $"The plan is: {shown}");
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN journal", StringComparison.Ordinal));
    }

    /// <summary>
    /// What wakes a pointer for a reply is read the same way: the ends of runs and the verdicts in the batch through the kind
    /// index, a run's own verdicts through its work item, and the verdicts before one through the kind index again.
    /// </summary>
    [Fact]
    public async Task What_wakes_is_read_through_indexes_and_never_the_whole_table()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var plan = await QueryPlans.PlanAsync(db, ReplyLines.Waking(db, 10, 20).ToQueryString(), Ct);

        var shown = string.Join(" | ", plan);
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN journal", StringComparison.Ordinal) || detail.Contains("SCAN journal ", StringComparison.Ordinal));
        Assert.True(plan.Any(detail => detail.Contains("ix_journal_kind", StringComparison.Ordinal)), $"The plan is: {shown}");
    }

    /// <summary>
    /// One verdict, about no campaign, written past the writer's check: the journal's documents refuse most of these payloads
    /// at the write, but a line already in a database (a hand edit, an earlier version) is counted all the same, so the count
    /// is held to <see cref="ReplyLines.Counts"/> on every shape a payload can take.
    /// </summary>
    private static async Task<int> WriteAsync(TestDatabase database, string key, string? payload, string? attempt)
    {
        await using var db = database.Open();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO journal (public_id, ts, actor_type, actor_id, kind, key, new_json, attempt_id) VALUES ({PublicId.New("jrn")}, '2026-11-22 12:00:00', 'system', 'responder',{JournalKinds.ReplyClassified}, {key}, {payload}, {attempt})",
            Ct);
        return await db.Database.SqlQuery<int>($"SELECT max(id) AS \"Value\" FROM journal").SingleAsync(Ct);
    }
}
