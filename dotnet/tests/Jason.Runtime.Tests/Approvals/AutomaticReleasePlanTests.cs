using Jason.Runtime.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Approvals;

/// <summary>
/// Each read the automatic step makes inside the claim's write transaction names the index it is answered from, and SQLite
/// answers it from that index: the item's own lines by the journal's work item index, and a campaign's verdicts on one
/// thread by the verdicts' thread index. None walks the journal. The plans are read from the very queries the claim runs.
/// </summary>
public class AutomaticReleasePlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("whether the send was written in the chat", "ix_journal_work_item_id_public_id (work_item_id=?)")]
    [InlineData("every review of the send", "ix_journal_work_item_id_public_id (work_item_id=?)")]
    [InlineData("a critic's created line and its context's changes", "ix_journal_work_item_id_public_id (work_item_id=?)")]
    [InlineData("the newest verdict on the reply's thread", "ix_journal_campaign_verdict_thread (campaign_id=? AND <expr>=?)")]
    [InlineData("the verdict that stands for the reply an answer names", "ix_journal_campaign_verdict_thread (campaign_id=? AND <expr>=? AND key=?)")]
    [InlineData("every verdict on the reply's thread, for an escalation", "ix_journal_campaign_verdict_thread (campaign_id=? AND <expr>=?)")]
    public async Task AutomaticRelease_EachRead_UsesTheIndexItNames(string read, string search)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        IQueryable query = read switch
        {
            "whether the send was written in the chat" => AutomaticRelease.Confined(db, "wi_01K52JR0000000000000000007"),
            "every review of the send" => AutomaticRelease.ReviewLines(db, "wi_01K52JR0000000000000000007"),
            "a critic's created line and its context's changes" => AutomaticRelease.CriticLines(db, "wi_01K52JR0000000000000000008"),
            "the newest verdict on the reply's thread" => AutomaticRelease.NewestOnThread(db, 3, "501", "stand-in-provider"),
            "the verdict that stands for the reply an answer names" => AutomaticRelease.StandingUnder(db, 3, "501", "sha256:verdict"),
            "every verdict on the reply's thread, for an escalation" => AutomaticRelease.OnThread(db, 3, "501", "stand-in-provider"),
            _ => throw new ArgumentOutOfRangeException(nameof(read), read, "no such read"),
        };

        var plan = await QueryPlans.PlanAsync(db, query.ToQueryString(), Ct);

        Assert.True(
            plan.Any(detail => detail.Contains("SEARCH journal USING INDEX " + search, StringComparison.Ordinal)),
            $"The plan of {read} is: {string.Join(" | ", plan)}");
        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN journal", StringComparison.Ordinal));
    }
}
