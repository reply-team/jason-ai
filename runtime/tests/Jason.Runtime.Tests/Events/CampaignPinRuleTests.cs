using Jason.Contracts.Api;
using Jason.Runtime.Events;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// One rule says which campaign an event naming a provider campaign reaches: the store attributes by it, and the inbound
/// read leaves a campaign to the events only where it holds. This proves the two agree, on the same pins both ways; it
/// does not prove the rule itself, which both could get wrong alike — what holds that an archived holder keeps no live one
/// from the events is the skip's own test,
/// <c>InboundSyncSkipTests.An_archived_campaign_holding_the_same_provider_campaign_does_not_keep_a_live_one_from_the_events</c>.
/// </summary>
public class CampaignPinRuleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    // Critical: one function for skip and attribution.
    [InlineData(1, 0, 0, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.N2)])]
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

    /// <summary>
    /// The inbound read asks the campaign rule alone: a campaign the store reaches only by the person — the provider
    /// campaign it is linked to held by two, or no provider campaign of its own — is still read by the inbound read, which
    /// lists the campaign's own threads, and an event the campaign rule would not give it is never what that read waits for.
    /// </summary>
    [Fact]
    public async Task The_inbound_skip_asks_the_campaign_rule_alone_and_not_the_person_s()
    {
        using var it = new EventStoreFixture();
        var shared = await it.CampaignAsync(pins: (Reader, "p_1"));
        await it.CampaignAsync(pins: (Reader, "p_1"));
        var unlinked = await it.CampaignAsync();
        var marta = await it.ContactAsync("c_1001");
        await it.MemberAsync(marta, shared);
        await it.MemberAsync(marta, unlinked);
        var source = await it.SourceAsync();

        await it.StoreAsync(source, null, Page("c-1", Bounced(campaign: "p_1")));

        await using var db = it.Open();
        var attributed = await db.EventAttributions.AsNoTracking().OrderBy(row => row.CampaignId).Select(row => row.CampaignId).ToListAsync(Ct);
        Assert.True(attributed.SequenceEqual([shared.Id, unlinked.Id]), $"The person's campaigns {shared.Id} and {unlinked.Id} were not reached: {string.Join(",", attributed)}.");
        foreach (var campaign in new[] { shared, unlinked })
        {
            var reached = await CampaignPinRule.ReachesAsync(db, Reader, campaign.Id, Ct);
            Assert.False(reached, $"The skip would leave campaign {campaign.Id}, reached only by the person, to the events.");
        }
    }
}
