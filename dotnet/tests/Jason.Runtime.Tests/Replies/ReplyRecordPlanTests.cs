using Jason.Runtime.Replies;
using Jason.Runtime.Tests.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// The read that ties a decision to the reply its asker recorded a verdict on names the index it is answered from, as a
/// standing approval's later-verdict read does: the kind's own index, so the read is bounded by the verdicts and never
/// walks the chronicle. The plan is read from the very query the record runs.
/// </summary>
public class ReplyRecordPlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_askers_verdict_is_read_through_the_kinds_index()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var plan = await QueryPlans.PlanAsync(
            db, ReplyService.AskersVerdict(db, 3, "sha256:0f", "att_01K52JR0000000000000000007").ToQueryString(), Ct);

        Assert.True(
            plan.Any(detail => detail.Contains("SEARCH journal USING INDEX ix_journal_kind (kind=?)", StringComparison.Ordinal)),
            $"The plan is: {string.Join(" | ", plan)}");
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN journal", StringComparison.Ordinal));
    }
}
