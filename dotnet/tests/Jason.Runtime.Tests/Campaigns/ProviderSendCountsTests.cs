using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Events;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Jason.Runtime.Tests.Events.EventStoreFixture;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// A message the account sent, said in a campaign as <c>provider_send_observed</c>, is a contact as an act of Jason's is:
/// <c>contacted</c> is the people an act operation about them succeeded for, or a send line named, in the period, each
/// person once; a send is dated by the provider's moment, not by when it was read; and a person's latest touch, the why
/// the Contacted list gives, is the later of the two. Over <see cref="CampaignWorld"/>'s week, where Contacted is Ivy,
/// Tess, Uma and Vic by their work.
/// </summary>
public class ProviderSendCountsTests
{
    private static readonly DateTimeOffset Since = new(CampaignWorld.Since, TimeSpan.Zero);

    private static readonly DateTimeOffset Until = new(CampaignWorld.Until, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Hal has no work in Spring; the sequence's send of the 14th alone makes him contacted, and one of the week's people.</summary>
    [Fact]
    public async Task A_send_alone_counts_the_person_contacted()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        Send(db, world.Spring, world.People["Hal"], "2026-10-14T08:00:00.000Z");
        await db.SaveChangesAsync(Ct);
        int[] spring = [world.Spring.Id];

        var counts = (await CampaignPeople.ActivityCountsAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct))[world.Spring.Id];
        Assert.Equal(5, counts.Contacted);
        Assert.Equal(17, counts.People);
        Assert.Equal(["Hal", "Ivy", "Tess", "Uma", "Vic"], await ListedAsync(db, world, spring, CampaignActivityFilter.Contacted, CampaignWorld.Since, CampaignWorld.Until));
        Assert.Contains("Hal", await ListedAsync(db, world, spring, CampaignActivityFilter.People, CampaignWorld.Since, CampaignWorld.Until));
    }

    /// <summary>
    /// Ivy was put into the sequence on the 12th and the sequence then sent her three steps: one person contacted, her
    /// latest touch the third step, and each step a happening of its own for the series to bucket.
    /// </summary>
    [Fact]
    public async Task An_enrolment_and_three_sends_are_one_person_contacted_last_by_the_latest_send()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        var ivy = world.People["Ivy"];
        Send(db, world.Spring, ivy, "2026-10-13T08:00:00.000Z");
        Send(db, world.Spring, ivy, "2026-10-14T08:00:00.000Z");
        Send(db, world.Spring, ivy, "2026-10-15T08:00:00.000Z");
        await db.SaveChangesAsync(Ct);
        int[] spring = [world.Spring.Id];

        Assert.Equal(4, (await CampaignPeople.ActivityCountsAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct))[world.Spring.Id].Contacted);
        Assert.Equal(4, (await CampaignPeople.ActivityTotalAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct)).Contacted);
        var listed = await CampaignPeople.ByMoment(db, spring, CampaignActivityFilter.Contacted, CampaignWorld.Since, CampaignWorld.Until).ToListAsync(Ct);
        Assert.Equal(Ms("2026-10-15T08:00:00Z"), Assert.Single(listed, person => person.ContactId == ivy.Id).Ms);
        var moments = await CampaignPeople.MomentsAsync(db, spring, CampaignActivityFilter.Contacted, CampaignWorld.Since, CampaignWorld.Until, null, false, Ct);
        Assert.Equal(4, moments.Count(moment => moment.ContactId == ivy.Id));
    }

    /// <summary>Hal is a member of Spring and of Autumn, and a send reached each: contacted in both, once in the total.</summary>
    [Fact]
    public async Task A_person_sent_to_in_two_campaigns_is_contacted_once_in_the_total()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        var hal = world.People["Hal"];
        Send(db, world.Spring, hal, "2026-10-13T08:00:00.000Z");
        Send(db, world.Autumn, hal, "2026-10-14T08:00:00.000Z");
        await db.SaveChangesAsync(Ct);
        int[] both = [world.Spring.Id, world.Autumn.Id];

        var counts = await CampaignPeople.ActivityCountsAsync(db, both, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct);
        Assert.Equal(5, counts[world.Spring.Id].Contacted);
        Assert.Equal(1, counts[world.Autumn.Id].Contacted);
        Assert.Equal(5, (await CampaignPeople.ActivityTotalAsync(db, both, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct)).Contacted);
        Assert.Equal(["Hal", "Ivy", "Tess", "Uma", "Vic"], await ListedAsync(db, world, both, CampaignActivityFilter.Contacted, CampaignWorld.Since, CampaignWorld.Until));
    }

    /// <summary>
    /// A send is dated by the moment the provider gives it: one from September read in this week's backfill counts in
    /// September, and one of the 14th read after the week ended counts in the week.
    /// </summary>
    [Fact]
    public async Task A_send_counts_in_the_period_the_provider_dates_it_not_the_one_it_was_read_in()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        Send(db, world.Spring, world.People["Hal"], "2026-09-20T08:00:00.000Z", written: new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc));
        Send(db, world.Spring, world.People["Gus"], "2026-10-14T08:00:00.000Z", written: new DateTime(2026, 10, 20, 10, 0, 0, DateTimeKind.Utc));
        await db.SaveChangesAsync(Ct);
        int[] spring = [world.Spring.Id];

        Assert.Equal(["Gus", "Ivy", "Tess", "Uma", "Vic"], await ListedAsync(db, world, spring, CampaignActivityFilter.Contacted, CampaignWorld.Since, CampaignWorld.Until));
        var september = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["Hal"], await ListedAsync(db, world, spring, CampaignActivityFilter.Contacted, september, september.AddDays(1)));
        Assert.Equal(1, (await CampaignPeople.ActivityCountsAsync(db, spring, september, september.AddDays(1), audience: null, withoutArchived: false, Ct))[world.Spring.Id].Contacted);
        var hal = world.People["Hal"].Id;
        Assert.DoesNotContain(
            await CampaignPeople.MomentsAsync(db, spring, CampaignActivityFilter.Contacted, september.AddDays(1), CampaignWorld.Since, null, false, Ct),
            moment => moment.ContactId == hal);
    }

    /// <summary>
    /// A send line about somebody who is no member of the campaign, or about nobody the runtime knows, joins no
    /// membership and counts nobody, in the count, the list and the series; Hal's beside them counts him.
    /// </summary>
    [Fact]
    public async Task A_send_about_a_non_member_or_about_nobody_counts_nobody()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        var stranger = new Contact { PublicId = PublicId.New("cnt"), FirstName = "Zed", CreatedAt = CampaignWorld.Since, UpdatedAt = CampaignWorld.Since };
        db.Contacts.Add(stranger);
        await db.SaveChangesAsync(Ct);
        Send(db, world.Spring, stranger, "2026-10-13T08:00:00.000Z");
        Send(db, world.Spring, null, "2026-10-13T09:00:00.000Z");
        Send(db, world.Spring, world.People["Hal"], "2026-10-13T10:00:00.000Z");
        await db.SaveChangesAsync(Ct);
        int[] spring = [world.Spring.Id];

        var counts = (await CampaignPeople.ActivityCountsAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct))[world.Spring.Id];
        Assert.Equal(5, counts.Contacted);
        Assert.Equal(17, counts.People);
        Assert.Equal(5, (await CampaignPeople.ActivityTotalAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct)).Contacted);
        Assert.Equal(["Hal", "Ivy", "Tess", "Uma", "Vic"], await ListedAsync(db, world, spring, CampaignActivityFilter.Contacted, CampaignWorld.Since, CampaignWorld.Until));
        var moments = await CampaignPeople.MomentsAsync(db, spring, CampaignActivityFilter.Contacted, CampaignWorld.Since, CampaignWorld.Until, null, false, Ct);
        Assert.Equal(5, moments.Count);
        Assert.DoesNotContain(moments, moment => moment.ContactId == stranger.Id);
    }

    /// <summary>
    /// The series buckets a send where its moment falls: Hal's send of the 14th stands in the 14th's Contacted and People,
    /// and Ivy's step of the 15th in the 15th's, though her enrolment, which is work, was on the 12th.
    /// </summary>
    [Fact]
    public async Task The_series_holds_a_send_in_the_bucket_of_its_moment_in_contacted_and_in_people()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        var contactedBefore = await ByDayAsync(api, world, "contacted");
        var peopleBefore = await ByDayAsync(api, world, "people");

        await SendAsync(api, world.Spring, world.People["Hal"], "2026-10-14T08:00:00.000Z");
        await SendAsync(api, world.Spring, world.People["Ivy"], "2026-10-15T08:00:00.000Z");

        var contacted = await ByDayAsync(api, world, "contacted");
        var people = await ByDayAsync(api, world, "people");
        Assert.Equal(contactedBefore.Keys, contacted.Keys);
        foreach (var (day, value) in contacted)
        {
            Assert.Equal(contactedBefore[day] + (day.Day is 14 or 15 ? 1 : 0), value);
            Assert.Equal(peopleBefore[day] + (day.Day is 14 or 15 ? 1 : 0), people[day]);
        }
    }

    /// <summary>
    /// Why the Contacted list counts a person is their latest touch in the period, of their act work and the sends: Ivy's
    /// is the sequence's step of the 15th, after her enrolment of the 12th and its step of the 13th; Hal's his only send; Tess's her own send of
    /// the 12th, later than the sequence's of the 11th. Under People a send names the count it is, contacted.
    /// </summary>
    [Fact]
    public async Task The_contacted_why_is_the_latest_of_the_work_and_the_sends()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        await SendAsync(api, world.Spring, world.People["Ivy"], "2026-10-15T08:00:00.000Z");
        await SendAsync(api, world.Spring, world.People["Ivy"], "2026-10-13T08:00:00.000Z");
        await SendAsync(api, world.Spring, world.People["Hal"], "2026-10-13T08:00:00.000Z");
        await SendAsync(api, world.Spring, world.People["Tess"], "2026-10-11T08:00:00.000Z");

        var contacted = await WhysAsync(api, world, CampaignActivityFilter.Contacted);
        Assert.Equal(new MembershipWhy("activity", null, "message_sent", At("2026-10-15T08:00:00Z"), null, null), contacted["Ivy"]);
        Assert.Equal(new MembershipWhy("activity", null, "message_sent", At("2026-10-13T08:00:00Z"), null, null), contacted["Hal"]);
        Assert.Equal(new MembershipWhy("activity", null, "message.send", At("2026-10-12T10:00:00Z"), null, null), contacted["Tess"]);

        var people = await WhysAsync(api, world, CampaignActivityFilter.People);
        Assert.Equal(new MembershipWhy("activity", "contacted", "message_sent", At("2026-10-15T08:00:00Z"), null, null), people["Ivy"]);
        Assert.Equal(new MembershipWhy("activity", "contacted", "message_sent", At("2026-10-13T08:00:00Z"), null, null), people["Hal"]);
        Assert.Equal("worked_on", people["Tess"].Text);
    }

    /// <summary>
    /// A send is no work of Jason's, no reply and no stop: Worked on, Replied and every count of now — the sequence and
    /// out among them — say what they said before the sends were read.
    /// </summary>
    [Fact]
    public async Task A_send_changes_no_count_but_contacted_and_people()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        int[] both = [world.Spring.Id, world.Autumn.Id];
        var summaryBefore = await CampaignPeople.SummaryCountsAsync(db, both, audience: null, withoutArchived: false, Ct);
        var totalBefore = await CampaignPeople.SummaryTotalAsync(db, both, audience: null, withoutArchived: false, Ct);
        var activityBefore = await CampaignPeople.ActivityTotalAsync(db, both, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct);
        var workedOnBefore = await ListedAsync(db, world, both, CampaignActivityFilter.WorkedOn, CampaignWorld.Since, CampaignWorld.Until);
        var repliedBefore = await ListedAsync(db, world, both, CampaignActivityFilter.Replied, CampaignWorld.Since, CampaignWorld.Until);

        // Hal is new, Ned in the sequence, Jon out by his reply, Finn idle: a send to each. Ned is already one of the
        // week's people, by his bounce; the other three become so.
        foreach (var name in new[] { "Hal", "Ned", "Jon", "Finn" })
        {
            Send(db, world.Spring, world.People[name], "2026-10-14T08:00:00.000Z");
        }

        await db.SaveChangesAsync(Ct);

        Assert.Equal(summaryBefore, await CampaignPeople.SummaryCountsAsync(db, both, audience: null, withoutArchived: false, Ct));
        Assert.Equal(totalBefore, await CampaignPeople.SummaryTotalAsync(db, both, audience: null, withoutArchived: false, Ct));
        Assert.Equal(workedOnBefore, await ListedAsync(db, world, both, CampaignActivityFilter.WorkedOn, CampaignWorld.Since, CampaignWorld.Until));
        Assert.Equal(repliedBefore, await ListedAsync(db, world, both, CampaignActivityFilter.Replied, CampaignWorld.Since, CampaignWorld.Until));
        var activity = await CampaignPeople.ActivityTotalAsync(db, both, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct);
        Assert.Equal(
            activityBefore with { People = activityBefore.People + 3, Contacted = activityBefore.Contacted + 4 },
            activity);
    }

    /// <summary>
    /// Ivy's enrolment, the campaign's act, was on the 12th, before a period that begins on the 13th; the sequence's step
    /// of the 14th is inside it. She is contacted once, in the count, the total and the list, by the send: its moment
    /// is her moment and her why, and the enrolment is no happening of the period.
    /// </summary>
    [Fact]
    public async Task An_act_before_the_period_and_a_send_inside_it_count_the_person_once_by_the_send()
    {
        var from = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);
        using (var database = new TestDatabase())
        {
            using var db = database.Open();
            var world = await CampaignWorld.WriteAsync(db, Ct);
            int[] spring = [world.Spring.Id];
            var ivy = world.People["Ivy"];
            var before = (await CampaignPeople.ActivityCountsAsync(db, spring, from, CampaignWorld.Until, audience: null, withoutArchived: false, Ct))[world.Spring.Id].Contacted;
            Assert.DoesNotContain("Ivy", await ListedAsync(db, world, spring, CampaignActivityFilter.Contacted, from, CampaignWorld.Until));
            Send(db, world.Spring, ivy, "2026-10-14T08:00:00.000Z");
            await db.SaveChangesAsync(Ct);

            Assert.Equal(before + 1, (await CampaignPeople.ActivityCountsAsync(db, spring, from, CampaignWorld.Until, audience: null, withoutArchived: false, Ct))[world.Spring.Id].Contacted);
            Assert.Equal(before + 1, (await CampaignPeople.ActivityTotalAsync(db, spring, from, CampaignWorld.Until, audience: null, withoutArchived: false, Ct)).Contacted);
            Assert.Single(await ListedAsync(db, world, spring, CampaignActivityFilter.Contacted, from, CampaignWorld.Until), "Ivy");
            var moments = await CampaignPeople.MomentsAsync(db, spring, CampaignActivityFilter.Contacted, from, CampaignWorld.Until, null, false, Ct);
            Assert.Equal(Ms("2026-10-14T08:00:00Z"), Assert.Single(moments, moment => moment.ContactId == ivy.Id).Ms);
        }

        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var apiWorld = await CampaignWorld.WriteAsync(api, Ct);
        await SendAsync(api, apiWorld.Spring, apiWorld.People["Ivy"], "2026-10-14T08:00:00.000Z");
        var whys = await WhysAsync(api, apiWorld, CampaignActivityFilter.Contacted, from, CampaignWorld.Until);
        Assert.Equal(new MembershipWhy("activity", null, "message_sent", At("2026-10-14T08:00:00Z"), null, null), whys["Ivy"]);
    }

    /// <summary>
    /// The mirror: Ivy's enrolment of the 12th is inside the week and the sequence's step of the 8th before it. She is
    /// one person contacted as she was, and her why is the enrolment.
    /// </summary>
    [Fact]
    public async Task An_act_inside_the_period_and_a_send_before_it_count_the_person_once_by_the_act()
    {
        using (var database = new TestDatabase())
        {
            using var db = database.Open();
            var world = await CampaignWorld.WriteAsync(db, Ct);
            int[] spring = [world.Spring.Id];
            var before = (await CampaignPeople.ActivityCountsAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct))[world.Spring.Id].Contacted;
            Send(db, world.Spring, world.People["Ivy"], "2026-10-08T08:00:00.000Z");
            await db.SaveChangesAsync(Ct);

            Assert.Equal(before, (await CampaignPeople.ActivityCountsAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct))[world.Spring.Id].Contacted);
            Assert.Equal(before, (await CampaignPeople.ActivityTotalAsync(db, spring, CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct)).Contacted);
            Assert.Single(await ListedAsync(db, world, spring, CampaignActivityFilter.Contacted, CampaignWorld.Since, CampaignWorld.Until), "Ivy");
        }

        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var apiWorld = await CampaignWorld.WriteAsync(api, Ct);
        await SendAsync(api, apiWorld.Spring, apiWorld.People["Ivy"], "2026-10-08T08:00:00.000Z");
        var whys = await WhysAsync(api, apiWorld, CampaignActivityFilter.Contacted, CampaignWorld.Since, CampaignWorld.Until);
        Assert.Equal("campaign.enroll", whys["Ivy"].Code);
        Assert.Equal(At("2026-10-12T10:00:00Z"), whys["Ivy"].At);
    }

    /// <summary>
    /// A past period, the 9th to the 14th, in which Ivy was sent a step on the 13th and another on the 15th, after it
    /// ended: the why names the send inside the period, never the later one.
    /// </summary>
    [Fact]
    public async Task The_why_of_a_past_period_never_names_a_send_after_it()
    {
        var end = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var world = await CampaignWorld.WriteAsync(api, Ct);
        await SendAsync(api, world.Spring, world.People["Ivy"], "2026-10-13T08:00:00.000Z");
        await SendAsync(api, world.Spring, world.People["Ivy"], "2026-10-15T08:00:00.000Z");
        await SendAsync(api, world.Spring, world.People["Hal"], "2026-10-15T08:00:00.000Z");

        var whys = await WhysAsync(api, world, CampaignActivityFilter.Contacted, CampaignWorld.Since, end);
        Assert.Equal(new MembershipWhy("activity", null, "message_sent", At("2026-10-13T08:00:00Z"), null, null), whys["Ivy"]);
        Assert.DoesNotContain("Hal", whys.Keys);
    }

    /// <summary>
    /// The exchange a consumer draws Contacted from, recorded: a campaign linked to its provider campaign, Ana put into
    /// the sequence by Jason on the 12th and Bo sent its first step on the 14th, that send read from the account's events
    /// and said by the runtime's own events store. Both are contacted; only Ana was worked on.
    /// </summary>
    [Fact]
    public async Task Contacted_counts_a_person_enrolled_and_one_the_sequence_sent_to_and_is_recorded()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(CampaignWorld.Now));
        var joined = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        string campaignId;
        await using (var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            var campaign = WorkItemFactory.NewCampaign("Q4 founders", CampaignStatus.Active, joined);
            campaign.EventsFrom = joined;
            db.Campaigns.Add(campaign);
            db.ExternalIds.Add(Pin(campaign, null, "campaign", "p_1", joined));
            var ana = Person(db, campaign, "Ana", "c_1", joined);
            Person(db, campaign, "Bo", "c_2", joined);
            var enrolment = WorkItemFactory.NewProviderOp(campaign, "campaign.enroll", joined);
            enrolment.Contact = ana;
            enrolment.Status = WorkItemStatus.Succeeded;
            enrolment.FinishedAt = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc);
            enrolment.UpdatedAt = enrolment.FinishedAt.Value;
            db.WorkItems.Add(enrolment);
            var source = new EventSource { PublicId = PublicId.New("evs"), PluginId = "reply", BindingIdentity = EventSource.NoBinding, CreatedAt = joined };
            db.EventSources.Add(source);
            await db.SaveChangesAsync(Ct);
            campaignId = campaign.PublicId;

            var stored = await scope.ServiceProvider.GetRequiredService<EventStore>().StoreAsync(
                db, source, null, Page("c-1", Sent(contact: "c_2", at: new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc))), "evr_01K7QN00000000000000000001", Ct);
            Assert.Equal(1, stored.Stored);
            Assert.Single(await db.Journal.AsNoTracking().Where(line => line.Kind == JournalKinds.ProviderSendObserved && line.CampaignId == campaign.Id).ToListAsync(Ct));
        }

        var week = await api.RecordAsync<CampaignActivityResponse>(
            Operations.CampaignActivity,
            "contacted-by-the-sequence",
            new { campaign_ids = new[] { campaignId }, since = "2026-10-10T00:00:00.000Z", until = "2026-10-16T09:00:00.000Z" },
            Ct);
        var row = Assert.Single(week.Campaigns);
        Assert.Equal((2, 2, 1), (row.Contacted, row.People, row.WorkedOn));
        Assert.Equal(2, week.Total.Contacted);
    }

    /// <summary>A member of the campaign, pinned at the provider by the identifier its events name them by.</summary>
    private static Contact Person(JasonDbContext db, Campaign campaign, string name, string pinned, DateTime joined)
    {
        var contact = new Contact { PublicId = PublicId.New("cnt"), FirstName = name, CreatedAt = joined, UpdatedAt = joined };
        db.Contacts.Add(contact);
        db.ExternalIds.Add(Pin(null, contact, "contact", pinned, joined));
        db.CampaignContacts.Add(new CampaignContact { Campaign = campaign, Contact = contact, AddedAt = joined, UpdatedAt = joined });
        return contact;
    }

    private static ExternalId Pin(Campaign? campaign, Contact? contact, string kind, string value, DateTime at) => new()
    {
        Campaign = campaign,
        Contact = contact,
        PluginId = "reply",
        Kind = kind,
        Value = value,
        RecordedAt = at,
        RecordedByAttemptId = "att_01K7QN00000000000000000001",
    };

    /// <summary>A send line of a campaign, as the events store says one: about the person given, or about nobody.</summary>
    private static void Send(JasonDbContext db, Campaign campaign, Contact? person, string moment, DateTime? written = null) =>
        db.Journal.Add(Line(campaign.Id, person, moment, written));

    /// <summary>The same line, written into a running runtime's database.</summary>
    private static async Task SendAsync(RuntimeApiFixture api, Campaign campaign, Contact person, string moment)
    {
        await using var scope = api.Resolve<IServiceScopeFactory>().CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        db.Journal.Add(Line(campaign.Id, person, moment, written: null));
        await db.SaveChangesAsync(Ct);
    }

    private static JournalEntry Line(int campaign, Contact? person, string moment, DateTime? written) => new()
    {
        PublicId = PublicId.New("jrn"),
        Key = "sha256:" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            "send|" + campaign.ToString(CultureInfo.InvariantCulture) + "|" + person?.PublicId + "|" + moment))),
        Ts = written ?? new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc),
        ActorType = ActorType.System,
        ActorId = "dispatcher",
        Kind = JournalKinds.ProviderSendObserved,
        CampaignId = campaign,
        ContactId = person?.Id,
        New = new JsonObject
        {
            ["plugin"] = "reply",
            ["event_id"] = PublicId.New("evt"),
            ["occurred_at"] = moment,
            ["channel"] = "email",
            ["contact_id"] = person?.PublicId,
            ["attributed_by"] = "campaign_pin",
            ["thread"] = "t-" + (person?.FirstName ?? "nobody"),
            ["message"] = "m-" + moment,
        },
    };

    private static async Task<string[]> ListedAsync(JasonDbContext db, CampaignWorld world, int[] campaigns, CampaignActivityFilter count, DateTime since, DateTime until)
    {
        var people = (await CampaignPeople.ByMoment(db, campaigns, count, since, until).ToListAsync(Ct)).Select(person => person.ContactId).ToList();
        return [.. world.People.Where(named => people.Contains(named.Value.Id)).Select(named => named.Key).Order(StringComparer.Ordinal)];
    }

    private static async Task<Dictionary<DateTimeOffset, int>> ByDayAsync(RuntimeApiFixture api, CampaignWorld world, string count) =>
        (await api.PostOkAsync<CampaignActivityByPeriodResponse>(
            Operations.CampaignActivityByPeriod,
            new { CampaignIds = new[] { world.Spring.PublicId }, Count = count, Period = "day", Since, Until },
            Ct)).Buckets.ToDictionary(bucket => bucket.Start, bucket => bucket.Value);

    /// <summary>Each person one activity count lists in Spring for the week, by name, with why it counts them.</summary>
    private static async Task<Dictionary<string, MembershipWhy>> WhysAsync(RuntimeApiFixture api, CampaignWorld world, CampaignActivityFilter count, DateTime? from = null, DateTime? to = null)
    {
        var since = from is { } f ? new DateTimeOffset(f, TimeSpan.Zero) : Since;
        var until = to is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : Until;
        var page = await api.PostOkAsync<Page<MembershipItemDto>>(
            Operations.CampaignListContacts, new { CampaignId = world.Spring.PublicId, Activity = count, Since = since, Until = until, Limit = 100 }, Ct);
        return page.Items.ToDictionary(item => item.Contact.FirstName!, item => item.Why!, StringComparer.Ordinal);
    }

    private static long Ms(string moment) => DateTimeOffset.Parse(moment, CultureInfo.InvariantCulture).ToUnixTimeMilliseconds();

    private static DateTimeOffset At(string moment) => DateTimeOffset.Parse(moment, CultureInfo.InvariantCulture);
}
