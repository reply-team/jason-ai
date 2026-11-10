using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Harness;
using Jason.Runtime.Tests.Journal;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// A campaign that set an <c>audience_threshold</c> is told when its untouched people fall under it: the reserve step
/// counts them when a line that can change the number came after its last look, and says <c>audience_reserve_low</c> or
/// <c>audience_reserve_recovered</c> once per crossing, decided from the journal alone. It writes those lines and nothing
/// else; the review the low line wakes is the summon's.
/// </summary>
public class ReserveCounterTests
{
    private static readonly DateTime Noon = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reserve_that_falls_under_its_threshold_is_said_once_and_summons_the_review_in_the_same_scan()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness, people: 3, threshold: 5);
        harness.Status.State = DispatcherState.Draining;

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal((1, 1), (report.Reserved, report.Summoned));
        var line = Assert.Single(await ReserveLinesAsync(harness, campaign));
        Assert.Equal(JournalKinds.AudienceReserveLow, line.Kind);
        Assert.Equal((ActorType.System, "dispatcher"), (line.ActorType, line.ActorId));
        Assert.Null(line.Key);
        Assert.Null(line.WorkItemId);
        Assert.Equal("""{"untouched":3,"threshold":5}""", line.New!.ToJsonString());

        // The work the scan made is the summon's check-in alone, and the reserve is what it was summoned by.
        var made = Assert.Single(await ItemsAsync(harness, campaign));
        Assert.Equal(ManagerCheckIn.Role, made.Role);
        Assert.Equal(JournalKinds.AudienceReserveLow, (string?)made.Context["trigger"]);

        // The next scans read back what was said, the check-in's own line among what came after: the step is made anew in
        // a scope of its own every scan, as a runtime started again over this database makes it, and keeps nothing.
        for (var scan = 0; scan < 2; scan++)
        {
            Assert.Equal(0, (await harness.Runner.ScanOnceAsync(Ct)).Reserved);
        }

        Assert.Single(await ReserveLinesAsync(harness, campaign));
    }

    /// <summary>
    /// Under is strictly under: a reserve at its threshold is said nothing. A campaign with no threshold, or one that is
    /// not live, is not counted at all.
    /// </summary>
    [Theory]
    [InlineData("at the threshold", true)]
    [InlineData("no threshold", false)]
    [InlineData("paused", false)]
    public async Task Nothing_is_said_for_a_reserve_at_its_threshold_and_nothing_counted_where_nobody_asked(string campaignIs, bool counted)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = campaignIs switch
        {
            "at the threshold" => await CampaignAsync(harness, people: 6, threshold: 5),
            "no threshold" => await CampaignAsync(harness, people: 3, threshold: null),
            "paused" => await CampaignAsync(harness, people: 3, threshold: 5, CampaignStatus.Paused),
            _ => throw new ArgumentOutOfRangeException(nameof(campaignIs), campaignIs, null),
        };
        if (campaignIs == "at the threshold")
        {
            await TouchAsync(harness, campaign, (await MembersAsync(harness, campaign))[0]);
        }

        harness.Commands.Clear();
        Assert.Equal(0, await harness.ReserveAsync(Ct));

        Assert.Empty(await ReserveLinesAsync(harness, campaign));
        Assert.Equal(counted ? 1 : 0, Counts(harness));
    }

    /// <summary>
    /// Once per crossing: a top-up that leaves the reserve under says nothing more, one that brings it back to the
    /// threshold says it recovered and wakes nobody, and only then can it be said to be low again, which wakes the
    /// manager again.
    /// </summary>
    [Fact]
    public async Task A_reserve_is_said_to_be_low_again_only_after_it_recovered()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness, people: 3, threshold: 5);
        harness.Status.State = DispatcherState.Draining;
        Assert.Equal(1, (await harness.Runner.ScanOnceAsync(Ct)).Reserved);
        await EndCheckInAsync(harness, campaign);

        await AddAsync(harness, campaign, 1);
        Assert.Equal(0, (await harness.Runner.ScanOnceAsync(Ct)).Reserved);

        await AddAsync(harness, campaign, 1);
        var recovered = await harness.Runner.ScanOnceAsync(Ct);
        Assert.Equal((1, 0), (recovered.Reserved, recovered.Summoned));

        var members = await MembersAsync(harness, campaign);
        await TouchAsync(harness, campaign, members[0]);
        await TouchAsync(harness, campaign, members[1]);
        var low = await harness.Runner.ScanOnceAsync(Ct);
        Assert.Equal((1, 1), (low.Reserved, low.Summoned));

        var lines = await ReserveLinesAsync(harness, campaign);
        Assert.Equal(
            [JournalKinds.AudienceReserveLow, JournalKinds.AudienceReserveRecovered, JournalKinds.AudienceReserveLow],
            lines.Select(line => line.Kind));
        Assert.Equal("""{"untouched":5,"threshold":5}""", lines[1].New!.ToJsonString());
        Assert.Equal("""{"untouched":3,"threshold":5}""", lines[2].New!.ToJsonString());
        var checkIns = (await ItemsAsync(harness, campaign)).Where(item => item.Role == ManagerCheckIn.Role).ToList();
        Assert.Equal(2, checkIns.Count);
        Assert.Equal(JournalKinds.AudienceReserveLow, (string?)checkIns[1].Context["trigger"]);
    }

    /// <summary>
    /// A threshold moved while the reserve is low is read against what was last said: raised, the reserve is still low and
    /// nothing is said; lowered to the count, it is no longer under, and that is a recovery.
    /// </summary>
    [Fact]
    public async Task A_threshold_moved_while_the_reserve_is_low_says_only_a_recovery()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness, people: 3, threshold: 5);
        Assert.Equal(1, await harness.ReserveAsync(Ct));

        await ThresholdAsync(harness, campaign, 8);
        Assert.Equal(0, await harness.ReserveAsync(Ct));

        await ThresholdAsync(harness, campaign, 3);
        Assert.Equal(1, await harness.ReserveAsync(Ct));

        var lines = await ReserveLinesAsync(harness, campaign);
        Assert.Equal([JournalKinds.AudienceReserveLow, JournalKinds.AudienceReserveRecovered], lines.Select(line => line.Kind));
        Assert.Equal("""{"untouched":3,"threshold":3}""", lines[1].New!.ToJsonString());
    }

    /// <summary>
    /// The step counts only when a line of a kind that can change the number came after its last look. A look that finds
    /// nothing new writes nothing at all; one that finds lines of other kinds, its own among them, moves past them without
    /// counting, so the next look reads only what came after.
    /// </summary>
    [Fact]
    public async Task The_reserve_is_counted_only_when_a_line_that_can_change_it_came_after_the_last_look()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness, people: 3, threshold: 5);
        Assert.Equal(1, await harness.ReserveAsync(Ct));

        var own = (await ReserveLinesAsync(harness, campaign))[0].Id;
        harness.Commands.Clear();
        Assert.Equal(0, await harness.ReserveAsync(Ct));
        Assert.Equal(0, Counts(harness));
        Assert.Equal(own, await WatermarkAsync(harness, campaign));

        harness.Commands.Clear();
        Assert.Equal(0, await harness.ReserveAsync(Ct));
        Assert.Equal(0, Counts(harness));
        Assert.DoesNotContain(harness.Commands.Texts, text => text.Contains("UPDATE", StringComparison.Ordinal));

        var started = await LineAsync(harness, campaign, JournalKinds.CampaignStarted);
        harness.Commands.Clear();
        Assert.Equal(0, await harness.ReserveAsync(Ct));
        Assert.Equal(0, Counts(harness));
        Assert.Equal(started, await WatermarkAsync(harness, campaign));

        await AddAsync(harness, campaign, 1);
        harness.Commands.Clear();
        Assert.Equal(0, await harness.ReserveAsync(Ct));
        Assert.Equal(1, Counts(harness));
    }

    /// <summary>
    /// The step's reads of the journal run on every scan for every campaign that set a threshold, so they are held to a
    /// plan: the campaign's newest line and whether a change is among the new ones by the index on campaign and id, and
    /// the latest line of each reserve kind by that kind's own filtered index. None of them reads a table whole.
    /// </summary>
    [Fact]
    public async Task The_step_reads_the_journal_by_its_indexes()
    {
        using var harness = new DispatchHarness(Noon);
        await CampaignAsync(harness, people: 3, threshold: 5);

        harness.Commands.Clear();
        Assert.Equal(1, await harness.ReserveAsync(Ct));

        var reads = harness.Commands.Texts
            .Where(text => text.StartsWith("SELECT", StringComparison.Ordinal) && text.Contains("FROM \"journal\"", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(
            [
                "SEARCH j USING COVERING INDEX ix_journal_campaign_id_id (campaign_id=?)",

                // The constant row is the EXISTS itself; the lines it reads are the campaign's since the last look.
                "SCAN CONSTANT ROW / SCALAR SUBQUERY 1 / SEARCH j USING INDEX ix_journal_campaign_id_id (campaign_id=? AND id>? AND id<?)",
                "SEARCH j USING COVERING INDEX ix_journal_audience_reserve_low_campaign_id (campaign_id=?)",
                "SEARCH j USING COVERING INDEX ix_journal_audience_reserve_recovered_campaign_id (campaign_id=?)",
            ],
            reads.Select(read => string.Join(" / ", ResponderTests.PlanOf(harness, read))));
    }

    /// <summary>How many times the harness's contexts asked for the untouched count since the log was last cleared.</summary>
    private static int Counts(DispatchHarness harness) =>
        harness.Commands.Texts.Count(text => text.Contains("has_own_work", StringComparison.Ordinal));

    /// <summary>A campaign with people a person added and a threshold a person set, through the services the API calls.</summary>
    private static async Task<string> CampaignAsync(DispatchHarness harness, int people, int? threshold, CampaignStatus status = CampaignStatus.Active)
    {
        string campaign;
        await using (var db = harness.Open())
        {
            var row = WorkItemFactory.NewCampaign(status: status, now: Noon.AddDays(-1));
            db.Campaigns.Add(row);
            await db.SaveChangesAsync(Ct);
            campaign = row.PublicId;
        }

        await AddAsync(harness, campaign, people);
        if (threshold is { } value)
        {
            await ThresholdAsync(harness, campaign, value);
        }

        return campaign;
    }

    private static async Task AddAsync(DispatchHarness harness, string campaign, int people)
    {
        await using var db = harness.Open();
        var memberships = new MembershipService(db, new JournalWriter(harness.Clock), harness.Clock, TestCanceller.New(harness.Clock));
        var added = await memberships.AddAsync(
            new AddContactsRequest(campaign, null, [.. Enumerable.Range(0, people).Select(_ => new AddContactsItem(null, "Alex", "Example", null, null, null, null, null))], null, null),
            Ct);
        Assert.Equal(people, added.Summary.Added);
    }

    private static async Task ThresholdAsync(DispatchHarness harness, string campaign, int threshold)
    {
        await using var db = harness.Open();
        var service = new CampaignService(db, new JournalWriter(harness.Clock), harness.Clock, TestCanceller.New(harness.Clock), TestHarness.FoldersFor(db));
        await service.UpdateAsync(
            new CampaignUpdateRequest(campaign, Optional<string?>.Absent, Optional<string?>.Absent, Optional<int?>.Absent, null, null, AudienceThreshold: Optional<int?>.Of(threshold)),
            Ct);
    }

    /// <summary>A researcher's item about the person, created by a person: somebody has started on them.</summary>
    private static async Task TouchAsync(DispatchHarness harness, string campaign, string contact)
    {
        await using var db = harness.Open();
        var items = new WorkItemService(db, new JournalWriter(harness.Clock), harness.Clock, TestCanceller.New(harness.Clock), TestOptions.PluginSettings());
        await items.CreateAsync(
            new WorkItemCreateRequest(campaign, WorkItemKind.AiRole, "researcher", null, contact, null, null, null, null, null, null, null, null, null, null, null),
            Ct);
    }

    private static async Task<List<string>> MembersAsync(DispatchHarness harness, string campaign)
    {
        await using var db = harness.Open();
        return await db.CampaignContacts
            .Where(membership => membership.Campaign!.PublicId == campaign)
            .OrderBy(membership => membership.ContactId)
            .Select(membership => membership.Contact!.PublicId)
            .ToListAsync(Ct);
    }

    /// <summary>One line of the campaign's own, of a kind that cannot change the count; answers its id.</summary>
    private static async Task<int> LineAsync(DispatchHarness harness, string campaign, string kind)
    {
        await using var db = harness.Open();
        var row = await db.Campaigns.SingleAsync(c => c.PublicId == campaign, Ct);
        var entry = new JournalWriter(harness.Clock).AppendDocumented(db, Actors.Runtime, kind, row);
        await db.SaveChangesAsync(Ct);
        return entry.Id;
    }

    /// <summary>The check-in ends, as a run of it would end it, so the next trigger summons another.</summary>
    private static async Task EndCheckInAsync(DispatchHarness harness, string campaign)
    {
        await using var db = harness.Open();
        var checkIn = await db.WorkItems.SingleAsync(item => item.Campaign!.PublicId == campaign && item.Role == ManagerCheckIn.Role, Ct);
        checkIn.Status = WorkItemStatus.Succeeded;
        checkIn.FinishedAt = Noon;
        await db.SaveChangesAsync(Ct);
    }

    private static async Task<List<JournalEntry>> ReserveLinesAsync(DispatchHarness harness, string campaign)
    {
        await using var db = harness.Open();
        return await db.Journal
            .AsNoTracking()
            .Where(entry => entry.Campaign!.PublicId == campaign
                && (entry.Kind == JournalKinds.AudienceReserveLow || entry.Kind == JournalKinds.AudienceReserveRecovered))
            .OrderBy(entry => entry.Id)
            .ToListAsync(Ct);
    }

    private static async Task<List<WorkItem>> ItemsAsync(DispatchHarness harness, string campaign)
    {
        await using var db = harness.Open();
        return await db.WorkItems.AsNoTracking().Where(item => item.Campaign!.PublicId == campaign).OrderBy(item => item.Id).ToListAsync(Ct);
    }

    private static async Task<int> WatermarkAsync(DispatchHarness harness, string campaign)
    {
        await using var db = harness.Open();
        return await db.Campaigns.Where(c => c.PublicId == campaign).Select(c => c.AudienceWatermark).SingleAsync(Ct);
    }
}
