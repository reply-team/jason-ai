using Jason.Runtime.Replies;
using Jason.Runtime.Tests.Approvals;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Replies;

/// <summary>
/// Each read warmth makes names the index it is answered from, and SQLite answers every table it touches from that index,
/// never by walking it: the person's own lines by the contact, the campaign's unnamed lines and the verdicts that name the contact on their thread by the campaign, a verdict and the one that replaced it by the campaign's own lines, the
/// campaign's own observed lines by their filtered index, a responder by the campaign's items of the same person, its
/// attempts by the item, and an attempt's start by the item's own lines. The plans are read from the very queries the
/// claim runs.
/// </summary>
public class WarmthPlanTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string[]> Reads() => new()
    {
        {
            "awaiting",
            [
                "o: ix_journal_contact_id_public_id (contact_id=?)",
                "v: ix_journal_campaign_id_id (campaign_id=?)",
                "later: ix_journal_campaign_id_id (campaign_id=? AND id>?)",
                "u: ix_journal_campaign_kind_key (campaign_id=? AND kind=?)",
                "n: ix_journal_campaign_id_id (campaign_id=?)",
                "nl: ix_journal_campaign_id_id (campaign_id=? AND id>?)",
                "r: ix_work_items_campaign_id_contact_id (campaign_id=? AND contact_id=?)",
                "a: ix_attempts_work_item_id_number (work_item_id=?)",
                "s: ix_journal_work_item_id_public_id (work_item_id=?)",
            ]
        },
        {
            "warming",
            [
                "v: ix_journal_contact_id_public_id (contact_id=?)",
                "later: ix_journal_campaign_id_id (campaign_id=? AND id>?)",
                "o: ix_journal_campaign_kind_key (campaign_id=? AND kind=?)",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(Reads))]
    public async Task Each_read_of_warmth_uses_the_indexes_it_names(string read, string[] searches)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var query = read == "awaiting" ? Warmth.Awaiting(db, 3, 7) : Warmth.Warming(db, 3, 7);

        var plan = await QueryPlans.PlanAsync(db, query.ToQueryString(), Ct);

        // SQLite writes a subquery it turns into a join as "SEARCH a EXISTS USING INDEX ...", so the table's alias and the
        // index are matched apart.
        foreach (var search in searches)
        {
            var (alias, index) = (search[..search.IndexOf(':', StringComparison.Ordinal)], search[(search.IndexOf(':', StringComparison.Ordinal) + 2)..]);
            Assert.True(
                plan.Any(detail => detail.StartsWith($"SEARCH {alias} ", StringComparison.Ordinal)
                    && detail.Contains("USING INDEX " + index, StringComparison.Ordinal)),
                $"The plan of {read} does not read {alias} through {index}: {string.Join(" | ", plan)}");
        }

        Assert.DoesNotContain(plan, detail => detail.StartsWith("SCAN", StringComparison.Ordinal));
    }
}
