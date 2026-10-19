using Jason.Contracts.Api;
using Jason.Runtime.Events;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// One rule says which campaign an event naming a provider campaign reaches: the store attributes by it, and the inbound
/// read leaves a campaign to the events only where it holds. Held here on the same pins both ways, so the two cannot come
/// to disagree.
/// </summary>
public class CampaignPinRuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(2, 0, 0)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(3, 1, 1)]
    public async Task The_inbound_skip_asks_exactly_what_the_store_attributes_by(int live, int archived, int elsewhere)
    {
        using var it = new EventStoreFixture();
        var holders = new List<Jason.Runtime.Persistence.Campaign>();
        for (var index = 0; index < live; index++)
        {
            holders.Add(await it.CampaignAsync(pins: (Reader, "p_1")));
        }

        for (var index = 0; index < archived; index++)
        {
            await it.CampaignAsync(CampaignStatus.Archived, pins: (Reader, "p_1"));
        }

        for (var index = 0; index < elsewhere; index++)
        {
            holders.Add(await it.CampaignAsync(pins: ("other", "p_1")));
        }

        var source = await it.SourceAsync();
        await it.StoreAsync(source, null, Page("c-1", Bounced(campaign: "p_1")));

        await using var db = it.Open();
        var attributed = await db.EventAttributions.AsNoTracking().Select(row => row.CampaignId).ToListAsync(Ct);
        foreach (var campaign in holders)
        {
            var reached = await CampaignPinRule.ReachesAsync(db, Reader, campaign.Id, Ct);
            Assert.True(
                reached == attributed.Contains(campaign.Id),
                $"Campaign {campaign.Id} ({live} live, {archived} archived, {elsewhere} at another plugin): the skip says {reached}, the store attributed {string.Join(",", attributed)}.");
        }
    }
}
