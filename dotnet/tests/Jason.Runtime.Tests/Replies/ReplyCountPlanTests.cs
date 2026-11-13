using Jason.Runtime.Replies;
using Jason.Runtime.Tests.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// The count of judged replies reads the verdicts through the kind's own index, as every other verdict read does, and
/// each standing verdict by its row id: bounded by the verdicts, never a walk of the chronicle. The plan is read from
/// the very query the count runs.
/// </summary>
public class ReplyCountPlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_verdicts_are_read_through_the_kinds_index_and_never_by_a_scan_of_the_chronicle()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var since = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);

        var plan = await QueryPlans.PlanAsync(db, ReplyCounts.Read(db, [3, 4], since, since.AddDays(1)).ToQueryString(), Ct);

        Assert.True(
            plan.Any(detail => detail.Contains("USING INDEX ix_journal_kind (kind=?)", StringComparison.Ordinal)),
            $"The plan is: {string.Join(" | ", plan)}");
        Assert.Contains("SEARCH j USING INTEGER PRIMARY KEY (rowid=?)", plan);
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN journal", StringComparison.Ordinal) || detail.StartsWith("SCAN j ", StringComparison.Ordinal) || detail == "SCAN j");
    }
}
