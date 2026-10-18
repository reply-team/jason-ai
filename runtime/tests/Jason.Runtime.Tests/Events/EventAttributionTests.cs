using Jason.Contracts.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// An event reaches a campaign by the provider's campaign identifier it names, pinned at the event's own plugin by
/// exactly one campaign that is not archived — and by nothing else. A campaign routed to the plugin, a campaign the
/// person is a member of, a campaign pinning the same value at another plugin: none of them is told.
/// </summary>
public class EventAttributionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_event_reaches_the_campaign_pinned_with_its_id_and_no_other()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var pinned = await store.CampaignAsync(pins: (Reader, "p_1"));
        var elsewhere = await store.CampaignAsync(pins: ("other-provider", "p_1"));
        var member = await store.CampaignAsync(pins: (Reader, "p_2"));
        var marta = await store.ContactAsync("c_1001", member: member);

        var result = await store.StoreAsync(source, null, Page("c-1", Moved(campaign: "p_1", contact: "c_1001")));

        Assert.Equal(1, result.Said);
        var line = Assert.Single(await store.LinesAsync(pinned, JournalKinds.InboundReplyObserved));
        Assert.Equal(marta.PublicId, (string?)line.New!["contact_id"]);
        Assert.Empty(await store.LinesAsync(elsewhere, JournalKinds.InboundReplyObserved));
        Assert.Empty(await store.LinesAsync(member, JournalKinds.InboundReplyObserved));

        await using var db = store.Open();
        var attribution = await db.EventAttributions.SingleAsync(Ct);
        Assert.Equal(pinned.Id, attribution.CampaignId);
        Assert.Equal(EventAttributionRules.CampaignPin, attribution.Rule);
        Assert.Equal(marta.Id, (await db.Events.SingleAsync(Ct)).ContactId);
    }

    /// <summary>An identifier two campaigns hold pinned at the plugin says nothing about which one it concerns, so it concerns neither.</summary>
    [Fact]
    public async Task An_identifier_two_campaigns_hold_concerns_neither()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        await store.CampaignAsync(pins: (Reader, "p_1"));
        await store.CampaignAsync(pins: (Reader, "p_1"));

        var result = await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: "p_1")));

        Assert.Equal(1, result.Stored);
        await using var db = store.Open();
        Assert.Equal(0, await db.EventAttributions.CountAsync(Ct));
        Assert.Empty(await store.AllLinesAsync());
    }

    /// <summary>An archived campaign is over and is told nothing, so the one live campaign holding the identifier is the one.</summary>
    [Fact]
    public async Task An_archived_campaign_is_not_among_those_that_hold_an_identifier()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var archived = await store.CampaignAsync(status: CampaignStatus.Archived, pins: (Reader, "p_1"));
        var live = await store.CampaignAsync(pins: (Reader, "p_1"));

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: "p_1")));

        Assert.Single(await store.LinesAsync(live, JournalKinds.ProviderEventObserved));
        Assert.Empty(await store.LinesAsync(archived, JournalKinds.ProviderEventObserved));
    }

    /// <summary>A value recorded beside a pin as a divergence is a disagreement, not a pin, and finds nobody.</summary>
    [Fact]
    public async Task A_value_kept_only_as_a_divergence_is_not_a_pin()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        await using (var db = store.Open())
        {
            await db.ExternalIds.ExecuteUpdateAsync(set => set.SetProperty(pin => pin.DivergedValue, "p_5"), Ct);
        }

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: "p_5")));

        Assert.Empty(await store.LinesAsync(campaign, JournalKinds.ProviderEventObserved));
    }

    /// <summary>The person a line names is the one contact pinned with the identifier the provider gave: two pinned with it, and nobody.</summary>
    [Fact]
    public async Task A_person_two_contacts_are_pinned_as_is_named_by_no_one()
    {
        using var store = new EventStoreFixture();
        var source = await store.SourceAsync();
        var campaign = await store.CampaignAsync(pins: (Reader, "p_1"));
        await store.ContactAsync("c_1001");
        await store.ContactAsync("c_1001");

        await store.StoreAsync(source, null, Page("c-1", Bounced(campaign: "p_1", contact: "c_1001")));

        var line = Assert.Single(await store.LinesAsync(campaign, JournalKinds.ProviderEventObserved));
        Assert.Null(line.New!["contact_id"]);
        await using var db = store.Open();
        Assert.Null((await db.Events.SingleAsync(Ct)).ContactId);
    }
}
