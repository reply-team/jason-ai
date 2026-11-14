using Jason.Runtime.Replies;
using Jason.Runtime.Tests.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// Each read warmth makes names the index it is answered from, and SQLite answers every table it touches from that index
/// by the columns that bound it, never by walking it and never by the campaign alone: the contact's own lines and
/// verdicts by the contact; the verdicts on a thread by the campaign and the thread; the verdict that replaced one by the
/// campaign, the thread and its key; the observed lines on a thread by the campaign and the thread; a responder by the
/// campaign's items of the same person, the read it was created with read from that same row; its creation by the
/// item's own lines; the
/// contact's public id by its row. The plans are read from the very queries the claim runs.
/// </summary>
public class WarmthPlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Each alias of a read, and the index and bound columns every search of it must use.</summary>
    public static TheoryData<string, string[]> Reads() => new()
    {
        {
            "awaiting",
            [
                "o: INDEX ix_journal_contact_id_public_id (contact_id=?)",
                "n: INDEX ix_journal_contact_id_public_id (contact_id=?)",
                "contacts: INTEGER PRIMARY KEY (rowid=?)",
                "nl: INDEX ix_journal_campaign_verdict_thread (campaign_id=? AND <expr>=? AND key=? AND rowid>?)",
                "u: INDEX ix_journal_campaign_thread (campaign_id=? AND <expr>=?)",
                "v: INDEX ix_journal_campaign_verdict_thread (campaign_id=? AND <expr>=?)",
                "later: INDEX ix_journal_campaign_verdict_thread (campaign_id=? AND <expr>=? AND key=? AND rowid>?)",
                "r: INDEX ix_work_items_campaign_id_contact_id (campaign_id=? AND contact_id=?)",
                "c: INDEX ix_journal_work_item_id_public_id (work_item_id=?)",
            ]
        },
        {
            "warming",
            [
                "v: INDEX ix_journal_contact_id_public_id (contact_id=?)",
                "contacts: INTEGER PRIMARY KEY (rowid=?)",
                "later: INDEX ix_journal_campaign_verdict_thread (campaign_id=? AND <expr>=? AND key=? AND rowid>?)",
                "o: INDEX ix_journal_campaign_thread (campaign_id=? AND <expr>=?)",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task Each_read_of_warmth_searches_every_table_by_the_columns_that_bound_it(string read, string[] searches)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var query = read == "awaiting" ? Warmth.Awaiting(db, 3, 7) : Warmth.Warming(db, 3, 7);
        var expected = searches.ToDictionary(
            search => search[..search.IndexOf(':', StringComparison.Ordinal)],
            search => search[(search.IndexOf(':', StringComparison.Ordinal) + 2)..],
            StringComparer.Ordinal);

        var plan = await QueryPlans.PlanAsync(db, query.ToQueryString(), Ct);

        // SQLite writes a subquery it turns into a join as "SEARCH a EXISTS USING INDEX ...", and an index that holds every
        // column a search needs as "USING COVERING INDEX ...", so the alias, the index and its bound columns are matched
        // apart. Every search of the plan is held to its alias's bound, and every alias is searched; the outer read of the
        // union's own rows, "SEARCH (subquery-N)", is no table.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var detail in plan.Where(detail => detail.StartsWith("SEARCH ", StringComparison.Ordinal) && !detail.StartsWith("SEARCH (", StringComparison.Ordinal)))
        {
            var alias = detail.Split(' ')[1];
            Assert.True(expected.TryGetValue(alias, out var bound), $"The plan of {read} searches {alias}, which it names no bound for: {string.Join(" | ", plan)}");
            Assert.True(
                detail.EndsWith(" USING " + bound, StringComparison.Ordinal) || detail.EndsWith(" USING COVERING " + bound, StringComparison.Ordinal),
                $"The plan of {read} does not search {alias} by {bound}: {detail} | {string.Join(" | ", plan)}");
            seen.Add(alias);
        }

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN", StringComparison.Ordinal));
    }

    /// <summary>
    /// The lines that name nobody are reached from the contact's own verdicts, never the other way round: the plan reads
    /// the contact's verdicts first and the unnamed lines of each one's thread inside that loop.
    /// </summary>
    [Fact]
    public async Task The_unnamed_lines_are_read_from_the_contacts_own_verdicts_by_their_thread()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var plan = await QueryPlans.PlanAsync(db, Warmth.Awaiting(db, 3, 7).ToQueryString(), Ct);

        var verdicts = plan.FindIndex(detail => detail.StartsWith("SEARCH n ", StringComparison.Ordinal));
        var unnamed = plan.FindIndex(detail => detail.StartsWith("SEARCH u ", StringComparison.Ordinal));
        Assert.True(verdicts >= 0 && unnamed > verdicts, $"The unnamed lines are not read inside the contact's verdicts: {string.Join(" | ", plan)}");
    }
}
