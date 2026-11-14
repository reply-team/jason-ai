using Jason.Runtime.Replies;
using Jason.Runtime.Tests.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// The lock's read names the index it is answered from, and SQLite answers every table it touches by the columns that
/// bound it: the contact's own lines by the contact, the contact's public id by its row, and the verdict that would lift
/// one by its campaign, its thread and its key. Nothing is bounded by a campaign alone or by the whole chronicle; the
/// plan is read from the very query the claim runs.
/// </summary>
public class OptOutPlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_lock_s_read_searches_every_table_by_the_columns_that_bound_it()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["v"] = "INDEX ix_journal_contact_id_public_id (contact_id=?)",
            ["contacts"] = "INTEGER PRIMARY KEY (rowid=?)",
            ["lift"] = "INDEX ix_journal_campaign_verdict_thread (campaign_id=? AND <expr>=? AND key=? AND rowid>?)",
        };

        var plan = await QueryPlans.PlanAsync(db, OptOuts.Standing(db, 7).ToQueryString(), Ct);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var detail in plan.Where(detail => detail.StartsWith("SEARCH ", StringComparison.Ordinal)))
        {
            var alias = detail.Split(' ')[1];
            Assert.True(expected.TryGetValue(alias, out var bound), $"The lock's plan searches {alias}, which it names no bound for: {string.Join(" | ", plan)}");
            Assert.True(
                detail.EndsWith(" USING " + bound, StringComparison.Ordinal) || detail.EndsWith(" USING COVERING " + bound, StringComparison.Ordinal),
                $"The lock's plan does not search {alias} by {bound}: {detail} | {string.Join(" | ", plan)}");
            seen.Add(alias);
        }

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), seen.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, detail => detail.Contains("TEMP B-TREE", StringComparison.Ordinal));
    }
}
