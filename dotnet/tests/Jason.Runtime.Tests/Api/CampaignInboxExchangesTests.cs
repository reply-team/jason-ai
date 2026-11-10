using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Contacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Seed = Jason.Runtime.Tests.Api.RecordedExchangesTests.Seed;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// A campaign's Inbox, recorded from this runtime for the screen that renders it: the lines that say a reply was observed
/// in the campaign, each naming the plugin it was observed through, and each person's threads asked of that plugin.
/// </summary>
public class CampaignInboxExchangesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The Inbox at 09:00 UTC on 16 October: the table and the panel it opens from; the pilot's reply lines, newest first,
    /// one the provider could not tie to anybody, one from Paula through the account no route binds and one from Mateo
    /// through the routed one; Paula's threads refused and Mateo's read, each asked of the plugin its line names; and a
    /// campaign no reply was observed in.
    /// </summary>
    [Fact]
    public async Task Campaign_inbox_exchanges_for_the_screen()
    {
        var reads = new FakeProviderReads();
        await using var api = await RuntimeApiFixture.StartAsync(
            Ct,
            clock: new FixedClock(Nine),
            configureServices: services => services.AddSingleton<IProviderReadInvoker>(reads));
        var seed = await Seed.PeopleAsync(api);
        string[] replied = ["inbound_reply_observed"];

        // Paula answers in the pilot as well, through the account no route binds; and, last, a reply arrives on a thread the
        // provider ties to nobody Jason knows.
        using (var scope = api.Resolve<IServiceScopeFactory>().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var person = await db.Contacts.SingleAsync(c => c.PublicId == seed.Paula, Ct);
            var pilot = await db.Campaigns.SingleAsync(c => c.PublicId == seed.LatAm, Ct);
            db.Journal.Add(Observed("jrn_01K7QN00000000000000000197", 8, 50, pilot.Id, person.Id, "t_601", "email", seed.Paula));
            db.Journal.Add(Observed("jrn_01K7QN00000000000000000198", 8, 55, pilot.Id, null, "t_701", "linkedin", null));
            await db.SaveChangesAsync(Ct);
        }

        var shown = await api.RecordAsync<Page<CampaignSummaryDto>>(Operations.CampaignList, "inbox", new { limit = 100 }, Ct);
        Assert.Equal([seed.LatAm, seed.ReEngage], shown.Items.Select(c => c.Id).Order(StringComparer.Ordinal));
        Assert.Equal(2, (await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "inbox-all", new { campaign_ids = shown.Items.Select(c => c.Id) }, Ct)).Campaigns.Count);
        Assert.Equal(CampaignStatus.Active, (await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "inbox", new { campaign_id = seed.LatAm }, Ct)).Status);
        Assert.Single((await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "inbox-one", new { campaign_ids = new[] { seed.LatAm } }, Ct)).Campaigns);

        var lines = await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "by-campaign-replies", new { campaign_id = seed.LatAm, kinds = replied, limit = 50 }, Ct);
        Assert.Equal([null, seed.Paula, seed.Mateo], lines.Items.Select(e => e.ContactId));
        Assert.All(lines.Items, e => Assert.Equal("reply", e.New!["plugin"]!.GetValue<string>()));
        Assert.Equal("no_route", (await api.RecordErrorAsync(Operations.ContactThreads, "inbox-paula", new { contact_id = seed.Paula, plugin_id = "reply", limit = 100 }, HttpStatusCode.NotFound, Ct)).Code);
        reads.Answer(FakeProviderReads.Result(Seed.Threads(more: false)));
        Assert.Equal(2, (await api.RecordAsync<ContactThreadsResponse>(Operations.ContactThreads, "inbox-mateo", new { contact_id = seed.Mateo, plugin_id = "reply", limit = 100 }, Ct)).Items.Count);

        // The paused campaign, in which no reply was observed: its panel and its empty lines, after which nothing is read.
        Assert.Equal(CampaignStatus.Paused, (await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "inbox-quiet", new { campaign_id = seed.ReEngage }, Ct)).Status);
        Assert.Single((await api.RecordAsync<CampaignSummaryResponse>(Operations.CampaignSummary, "inbox-quiet", new { campaign_ids = new[] { seed.ReEngage } }, Ct)).Campaigns);
        Assert.Empty((await api.RecordAsync<Page<JournalEntryDto>>(Operations.JournalList, "by-campaign-replies-none", new { campaign_id = seed.ReEngage, kinds = replied, limit = 50 }, Ct)).Items);
    }

    /// <summary>A reply line written the way inbound sync writes it, at a moment on 16 October.</summary>
    private static JournalEntry Observed(string id, int hour, int minute, int campaign, int? contact, string thread, string channel, string? contactId)
    {
        var at = new DateTime(2026, 10, 16, hour, minute, 0, DateTimeKind.Utc);
        var moment = at.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
        return new JournalEntry
        {
            PublicId = id,
            Ts = at,
            ActorType = Actors.Dispatcher.Type,
            ActorId = Actors.Dispatcher.Id,
            Kind = JournalKinds.InboundReplyObserved,
            CampaignId = campaign,
            ContactId = contact,
            Key = Jason.Runtime.Dispatch.InboundSync.ObservationKey("reply", thread, moment),
            New = new JsonObject { ["plugin"] = "reply", ["thread"] = thread, ["channel"] = channel, ["last_activity_at"] = moment, ["unread"] = true, ["has_meeting_intent"] = null, ["contact_id"] = contactId },
        };
    }
}
