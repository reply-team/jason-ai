using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

public class CampaignPeopleActivityTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task People_is_everyone_anything_happened_to_in_the_period()
    {
        Assert.Equal(
            ["Ada", "Ben", "Cleo", "Ivy", "Kit", "Lou", "Max", "Ned", "Oli", "Pia", "Quinn", "Rae", "Tess", "Uma", "Vic", "Wes"],
            await TheWeekAsync(CampaignActivityFilter.People));
    }

    [Fact]
    public async Task Added_counts_a_first_add_and_a_re_add_in_the_period()
    {
        Assert.Equal(["Ada", "Quinn"], await TheWeekAsync(CampaignActivityFilter.Added));
    }

    [Fact]
    public async Task Worked_on_counts_finished_work_and_never_a_rejection()
    {
        Assert.Equal(
            ["Ben", "Cleo", "Ivy", "Lou", "Pia", "Tess", "Uma", "Vic", "Wes"],
            await TheWeekAsync(CampaignActivityFilter.WorkedOn));
    }

    [Fact]
    public async Task Contacted_counts_exactly_the_four_succeeded_act_operations()
    {
        Assert.Equal(["Ivy", "Tess", "Uma", "Vic"], await TheWeekAsync(CampaignActivityFilter.Contacted));
    }

    [Fact]
    public async Task Replied_counts_the_first_reply_only()
    {
        Assert.DoesNotContain("Jon", await TheWeekAsync(CampaignActivityFilter.Replied));
        Assert.Equal(["Jon"], await HappenedAsync(CampaignActivityFilter.Replied, Utc(2026, 10, 5), Utc(2026, 10, 6)));
    }

    [Fact]
    public async Task Replied_dates_a_reply_by_the_provider_moment()
    {
        Assert.Equal(["Oli", "Rae"], await TheWeekAsync(CampaignActivityFilter.Replied));
        Assert.Equal(["Sam"], await HappenedAsync(CampaignActivityFilter.Replied, Utc(2026, 3, 10), Utc(2026, 3, 11)));
    }

    [Fact]
    public async Task Bounced_counts_the_people_a_bounce_names_hard_or_soft()
    {
        Assert.Equal(["Max", "Ned"], await TheWeekAsync(CampaignActivityFilter.Bounced));
    }

    [Fact]
    public async Task Bounced_dates_a_bounce_by_the_provider_moment()
    {
        // Both bounces happened at seven on the 13th and were written at ten.
        var seven = new DateTime(2026, 10, 13, 7, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["Max", "Ned"], await HappenedAsync(CampaignActivityFilter.Bounced, seven, seven.AddMinutes(1)));
        Assert.Empty(await HappenedAsync(CampaignActivityFilter.Bounced, seven.AddMinutes(1), seven.AddHours(4)));
    }

    [Fact]
    public async Task A_bounce_that_names_nobody_counts_nobody()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        db.Journal.Add(new JournalEntry
        {
            PublicId = PublicId.New("jrn"),
            Key = "sha256:" + new string('b', 64),
            Ts = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc),
            ActorType = ActorType.System,
            Kind = JournalKinds.ProviderEventObserved,
            CampaignId = world.Spring.Id,
            New = new JsonObject
            {
                ["kind"] = "bounce",
                ["plugin"] = "reply",
                ["event_id"] = PublicId.New("evt"),
                ["occurred_at"] = "2026-10-14T08:00:00.000Z",
                ["channel"] = null,
                ["contact_id"] = null,
                ["attributed_by"] = "campaign_pin",
                ["message"] = "m-nobody",
                ["bounce"] = "hard",
            },
        });
        await db.SaveChangesAsync(Ct);

        var counts = await CampaignPeople.ActivityCountsAsync(db, [world.Spring.Id], CampaignWorld.Since, CampaignWorld.Until, audience: null, withoutArchived: false, Ct);
        Assert.Equal(2, counts[world.Spring.Id].Bounced);
        Assert.Equal(2, (await CampaignPeople.MomentsAsync(db, [world.Spring.Id], CampaignActivityFilter.Bounced, CampaignWorld.Since, CampaignWorld.Until, null, false, Ct)).Count);
    }

    [Fact]
    public async Task Opted_out_comes_from_the_journal_even_after_the_entry_is_lifted()
    {
        Assert.Equal(["Kit", "Uma"], await TheWeekAsync(CampaignActivityFilter.OptedOut));
    }

    [Fact]
    public async Task Removed_counts_a_person_taken_out_in_the_period_and_nobody_taken_out_before_it()
    {
        Assert.Equal(["Oli"], await TheWeekAsync(CampaignActivityFilter.Removed));
        Assert.Equal(["Quinn"], await HappenedAsync(CampaignActivityFilter.Removed, Utc(2026, 10, 1), Utc(2026, 10, 3)));
    }

    [Fact]
    public async Task The_period_includes_since_and_excludes_until()
    {
        var raeReplied = new DateTime(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
        var oliReplied = new DateTime(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["Rae"], await HappenedAsync(CampaignActivityFilter.Replied, raeReplied, oliReplied));

        var thirteenth = new DateTime(2026, 10, 13, 10, 0, 0, DateTimeKind.Utc);
        var fourteenth = new DateTime(2026, 10, 14, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["Ben", "Pia", "Uma", "Wes"], await HappenedAsync(CampaignActivityFilter.WorkedOn, thirteenth, fourteenth));

        var adaAdded = new DateTime(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);
        Assert.Equal(["Ada"], await HappenedAsync(CampaignActivityFilter.Added, adaAdded, fourteenth));
    }

    [Fact]
    public async Task A_person_removed_after_the_fact_still_counts_in_the_period()
    {
        Assert.Contains("Oli", await TheWeekAsync(CampaignActivityFilter.Replied));
    }

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static Task<string[]> TheWeekAsync(CampaignActivityFilter filter) =>
        HappenedAsync(filter, CampaignWorld.Since, CampaignWorld.Until);

    /// <summary>The people a count counts in the period, as the activity list reads them: each once, by their latest moment.</summary>
    private static async Task<string[]> HappenedAsync(CampaignActivityFilter filter, DateTime since, DateTime until)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var world = await CampaignWorld.WriteAsync(db, Ct);
        var people = (await CampaignPeople.ByMoment(db, [world.Spring.Id], filter, since, until).ToListAsync(Ct)).Select(person => person.ContactId).ToList();
        var names = await db.Contacts.Where(contact => people.Contains(contact.Id)).Select(contact => contact.FirstName!).ToListAsync(Ct);
        return [.. names.Order(StringComparer.Ordinal)];
    }
}
