using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Configuration;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Dispatch;

/// <summary>
/// The dispatcher follows each search a campaign's audience is filled by, from the search's row alone: one read of its
/// people a cadence after the last, or at once where the last said more were readable, never a second while one is under
/// way; and, once its reads have landed nobody for the stall bound, the search ends stalled, said once, and is read no
/// more. The step keeps nothing, so a runtime started again repeats nothing.
/// </summary>
public sealed class SearchPollTests
{
    private static readonly DateTime Noon = new(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A search is first read a cadence after it began, by one work item the dispatcher makes naming it. While that read is
    /// under way — and while a read somebody else made is — nothing more is made, however many scans and minutes pass,
    /// each step made anew as a restarted runtime makes it. The read ended, the next comes a cadence after the last began.
    /// </summary>
    [Fact]
    public async Task A_search_is_read_a_cadence_after_its_last_read_and_never_twice_at_once()
    {
        using var harness = new DispatchHarness(Noon);
        var search = await SearchAsync(harness);

        harness.Clock.Advance(TimeSpan.FromSeconds(299));
        Assert.Equal(new SearchPollReport(0, 0), await harness.PollAsync(Ct));

        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new SearchPollReport(1, 0), await harness.PollAsync(Ct));
        var read = Assert.Single(await ReadsAsync(harness));
        Assert.Equal(
            (WorkItemKind.ProviderOp, "audience.search_status", ActorType.System, "dispatcher", search.CampaignId),
            (read.Kind, read.Operation, read.CreatedByType, read.CreatedById, read.CampaignId));
        Assert.Equal($$$"""{"input":{"search":"{{{search.WorkItem}}}","limit":100}}""", read.Context.ToJsonString());
        Assert.Equal((Noon.AddMinutes(5), false), await NoteAsync(harness));

        for (var minutes = 0; minutes < 3; minutes++)
        {
            harness.Clock.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal(new SearchPollReport(0, 0), await harness.PollAsync(Ct));
        }

        await EndAsync(harness, read, WorkItemStatus.Succeeded);
        var someoneElses = await ReadBySomeoneAsync(harness, search);
        Assert.Equal(new SearchPollReport(0, 0), await harness.PollAsync(Ct));

        await EndAsync(harness, someoneElses, WorkItemStatus.Failed);
        Assert.Equal(new SearchPollReport(1, 0), await harness.PollAsync(Ct));
        Assert.Equal(3, (await ReadsAsync(harness)).Count);
        Assert.Equal((Noon.AddMinutes(20), false), await NoteAsync(harness));
    }

    /// <summary>
    /// A read that said more people are readable now brings the next at once, not a cadence later. The step takes that
    /// word when it makes the read, so a read that then fails waits like any failed read: twice the cadence.
    /// </summary>
    [Fact]
    public async Task More_to_read_brings_the_next_read_at_once_and_a_failed_one_waits()
    {
        using var harness = new DispatchHarness(Noon);
        await SearchAsync(harness, more: true, readAt: Noon.AddSeconds(-10));

        Assert.Equal(new SearchPollReport(1, 0), await harness.PollAsync(Ct));
        Assert.Equal((Noon, false), await NoteAsync(harness));

        await EndAsync(harness, Assert.Single(await ReadsAsync(harness)), WorkItemStatus.Failed);
        harness.Clock.Advance(TimeSpan.FromSeconds(599));
        Assert.Equal(new SearchPollReport(0, 0), await harness.PollAsync(Ct));
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(new SearchPollReport(1, 0), await harness.PollAsync(Ct));
    }

    /// <summary>
    /// A search whose reads have landed nobody from the start of the quiet to the start of a read that began the stall
    /// bound later ends stalled once that read is over: the line says the provider's last word for it, wakes the review in
    /// the same scan, and no read of the search is made again. A read that began a minute short of the bound is followed by
    /// one more, so a campaign that sat still for hours is read before its search is called stalled.
    /// </summary>
    [Fact]
    public async Task Reads_that_land_nobody_for_the_bound_end_the_search_stalled_once_and_it_is_never_read_again()
    {
        using var harness = new DispatchHarness(Noon);
        harness.Status.State = DispatcherState.Draining;
        var search = await SearchAsync(harness, readAt: Noon.AddMinutes(59), state: "in_progress", found: 12, landed: 12);
        harness.Clock.Advance(TimeSpan.FromHours(3));

        Assert.Equal(new SearchPollReport(1, 0), await harness.PollAsync(Ct));
        await EndAsync(harness, Assert.Single(await ReadsAsync(harness)), WorkItemStatus.Succeeded);

        var scan = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal((0, 1, 1), (scan.Polled, scan.Stalled, scan.Summoned));
        await using (var db = harness.Open())
        {
            var row = await db.AudienceSearches.AsNoTracking().SingleAsync(Ct);
            Assert.Equal(("stalled", (DateTime?)Noon.AddHours(3)), (row.EndedAs, row.EndedAt));
            var line = Assert.Single(await db.Journal.AsNoTracking().Where(entry => entry.Kind == JournalKinds.AudienceSearchEnded).ToListAsync(Ct));
            Assert.Equal((ActorType.System, "dispatcher", search.WorkItem, search.WorkItem), (line.ActorType, line.ActorId, line.Key, line.WorkItemId));
            Assert.Equal(
                $$"""{"audience":"{{search.Audience}}","plugin":"stand-in-search","ended_as":"stalled","state":"in_progress","asked":25,"found":12,"landed":12,"ended_by":null}""",
                line.New!.ToJsonString());
        }

        harness.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(new SearchPollReport(0, 0), await harness.PollAsync(Ct));
        Assert.Single(await ReadsAsync(harness));
    }

    /// <summary>
    /// A search the provider holds spends nothing until a person acts there, so it is followed on the slower cadence of a
    /// held search and under its long bound: read an hour after the last read, never called stalled at the hour an
    /// ordinary search would be, and ended stalled, its state named, only once its reads have landed nobody for a week.
    /// </summary>
    [Fact]
    public async Task A_held_search_is_read_on_the_held_cadence_and_stalls_only_at_its_own_bound()
    {
        using var harness = new DispatchHarness(Noon);
        var search = await SearchAsync(harness, readAt: Noon, state: "paused_for_credits", found: 12, landed: 12);

        harness.Clock.Now = new DateTimeOffset(Noon.AddMinutes(59));
        Assert.Equal(new SearchPollReport(0, 0), await harness.PollAsync(Ct));
        harness.Clock.Now = new DateTimeOffset(Noon.AddMinutes(60));
        Assert.Equal(new SearchPollReport(1, 0), await harness.PollAsync(Ct));
        await EndAsync(harness, (await ReadsAsync(harness))[^1], WorkItemStatus.Succeeded);
        Assert.Equal(new SearchPollReport(0, 0), await harness.PollAsync(Ct));

        harness.Clock.Now = new DateTimeOffset(Noon.AddDays(7));
        Assert.Equal(new SearchPollReport(1, 0), await harness.PollAsync(Ct));
        await EndAsync(harness, (await ReadsAsync(harness))[^1], WorkItemStatus.Succeeded);
        Assert.Equal(new SearchPollReport(0, 1), await harness.PollAsync(Ct));

        await using var db = harness.Open();
        var line = Assert.Single(await db.Journal.AsNoTracking().Where(entry => entry.Kind == JournalKinds.AudienceSearchEnded).ToListAsync(Ct));
        Assert.Equal((search.WorkItem, "stalled", "paused_for_credits"), (line.Key, (string?)line.New!["ended_as"], (string?)line.New["state"]));
    }

    /// <summary>
    /// A read that ends without an answer — the provider no longer holds the search, its route changed — is not made
    /// again every cadence: each of the dispatcher's reads in a row that failed doubles the wait for the next, as the
    /// inbound read waits. They land nobody, so they count toward the stall bound, and the search ends there.
    /// </summary>
    [Fact]
    public async Task Failed_reads_double_the_wait_and_count_toward_the_stall()
    {
        using var harness = new DispatchHarness(Noon);
        await SearchAsync(harness);
        var made = new List<DateTime>();
        for (var minute = 0; minute <= 120; minute++)
        {
            harness.Clock.Now = new DateTimeOffset(Noon.AddMinutes(minute));
            var report = await harness.PollAsync(Ct);
            if (report.Stalled == 1)
            {
                break;
            }

            if (report.Read == 1)
            {
                made.Add(harness.Clock.GetUtcNow().UtcDateTime);
                await EndAsync(harness, (await ReadsAsync(harness))[^1], WorkItemStatus.Failed);
            }
        }

        Assert.Equal([Noon.AddMinutes(5), Noon.AddMinutes(15), Noon.AddMinutes(35), Noon.AddMinutes(75)], made);
        await using var db = harness.Open();
        var row = await db.AudienceSearches.AsNoTracking().SingleAsync(Ct);
        Assert.Equal(("stalled", 3, (DateTime?)Noon.AddMinutes(76)), (row.EndedAs, row.Failures, row.EndedAt));
    }

    /// <summary>
    /// A read that answered puts the cadence back: the wait doubles only for reads in a row that failed.
    /// </summary>
    [Fact]
    public async Task A_read_that_answered_puts_the_cadence_back()
    {
        using var harness = new DispatchHarness(Noon);
        await SearchAsync(harness);
        harness.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, (await harness.PollAsync(Ct)).Read);
        await EndAsync(harness, (await ReadsAsync(harness))[^1], WorkItemStatus.Failed);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, (await harness.PollAsync(Ct)).Read);
        await EndAsync(harness, (await ReadsAsync(harness))[^1], WorkItemStatus.Succeeded);

        harness.Clock.Advance(TimeSpan.FromSeconds(299));
        Assert.Equal(0, (await harness.PollAsync(Ct)).Read);
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, (await harness.PollAsync(Ct)).Read);
        await using var db = harness.Open();
        Assert.Equal(0, (await db.AudienceSearches.AsNoTracking().SingleAsync(Ct)).Failures);
    }

    /// <summary>
    /// A search a stop is under way for is neither read nor called stalled: the stop's answer decides how it ends.
    /// </summary>
    [Fact]
    public async Task A_search_being_stopped_is_neither_read_nor_called_stalled()
    {
        using var harness = new DispatchHarness(Noon);
        var search = await SearchAsync(harness, readAt: Noon.AddHours(2));
        harness.Clock.Advance(TimeSpan.FromHours(3));
        await using (var db = harness.Open())
        {
            var campaign = await db.Campaigns.SingleAsync(c => c.Id == search.CampaignId, Ct);
            var stop = WorkItemFactory.NewProviderOp(campaign, "audience.search_cancel", harness.Clock.GetUtcNow().UtcDateTime);
            stop.Context = new JsonObject { ["input"] = new JsonObject { ["search"] = search.WorkItem } };
            db.WorkItems.Add(stop);
            await db.SaveChangesAsync(Ct);
        }

        Assert.Equal(new SearchPollReport(0, 0), await harness.PollAsync(Ct));
        Assert.Empty(await ReadsAsync(harness));
    }

    /// <summary>
    /// The step's first look may see the dispatcher's read still under way after it ended: then nothing is decided this
    /// scan, rather than the read being counted as one that failed. Only a read that failed, was cancelled or expired
    /// counts; one that answered counts nothing, whatever the row's tally said.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Processing, 0, "None")]
    [InlineData(WorkItemStatus.Succeeded, 3, "Read")]
    [InlineData(WorkItemStatus.Failed, 0, "None")]
    [InlineData(WorkItemStatus.Expired, 0, "None")]
    public void Only_a_read_that_ended_without_an_answer_lengthens_the_wait(WorkItemStatus read, int failures, string expected)
    {
        var search = new SearchPoll.Followed(1, 1, "cmp_1", "wi_1", More: false, ReadAt: Noon, read, failures, QuietSince: Noon, CreatedAt: Noon, State: "in_progress");

        Assert.Equal(expected, SearchPoll.Decide(search, new DispatcherOptions(), Noon.AddMinutes(5)).ToString());
    }

    /// <summary>A search of a campaign that does not run is neither read nor called stalled: its campaign is paused, not its search.</summary>
    [Fact]
    public async Task A_paused_campaign_s_searches_are_left_as_they_are()
    {
        using var harness = new DispatchHarness(Noon);
        await SearchAsync(harness, readAt: Noon.AddHours(2), status: CampaignStatus.Paused);
        harness.Clock.Advance(TimeSpan.FromHours(3));

        Assert.Equal(new SearchPollReport(0, 0), await harness.PollAsync(Ct));

        Assert.Empty(await ReadsAsync(harness));
    }

    private sealed record Seeded(int CampaignId, string WorkItem, string Audience);

    /// <summary>A search for twenty-five people that the provider took at noon, its row as the reads left it.</summary>
    private static async Task<Seeded> SearchAsync(
        DispatchHarness harness,
        bool more = false,
        DateTime? readAt = null,
        string? state = null,
        int? found = null,
        int landed = 0,
        CampaignStatus status = CampaignStatus.Active)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(status: status, now: Noon);
        var audience = new Audience
        {
            PublicId = PublicId.New("aud"),
            Campaign = campaign,
            Number = 1,
            Definition = new JsonObject { ["industries"] = new JsonArray("Logistics") },
            DefinitionHash = "sha256:aa",
            CreatedAt = Noon,
        };
        var search = WorkItemFactory.NewProviderOp(campaign, "audience.search", Noon);
        search.Status = WorkItemStatus.Succeeded;
        db.AddRange(campaign, audience, search);
        db.AudienceSearches.Add(new AudienceSearch
        {
            Campaign = campaign,
            Audience = audience,
            SearchWorkItem = search,
            PluginId = "stand-in-search",
            Reference = "srch-1",
            State = state,
            More = more,
            Asked = 25,
            Found = found,
            Landed = landed,
            QuietSince = Noon,
            ReadAt = readAt,
            CreatedAt = Noon,
        });
        await db.SaveChangesAsync(Ct);
        return new Seeded(campaign.Id, search.PublicId, audience.PublicId);
    }

    /// <summary>A read of the search a role made, still under way.</summary>
    private static async Task<WorkItem> ReadBySomeoneAsync(DispatchHarness harness, Seeded search)
    {
        await using var db = harness.Open();
        var campaign = await db.Campaigns.SingleAsync(c => c.Id == search.CampaignId, Ct);
        var read = WorkItemFactory.NewProviderOp(campaign, "audience.search_status", harness.Clock.GetUtcNow().UtcDateTime);
        read.Context = new JsonObject { ["input"] = new JsonObject { ["search"] = search.WorkItem, ["limit"] = 10 } };
        db.WorkItems.Add(read);
        await db.SaveChangesAsync(Ct);
        return read;
    }

    private static async Task<List<WorkItem>> ReadsAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        return await db.WorkItems.AsNoTracking().Where(item => item.Operation == "audience.search_status").OrderBy(item => item.Id).ToListAsync(Ct);
    }

    private static async Task EndAsync(DispatchHarness harness, WorkItem read, WorkItemStatus status)
    {
        await using var db = harness.Open();
        await db.WorkItems.Where(item => item.Id == read.Id).ExecuteUpdateAsync(set => set.SetProperty(item => item.Status, status), Ct);
    }

    /// <summary>When the dispatcher last made a read of the search, and whether the row still says more is readable.</summary>
    private static async Task<(DateTime?, bool)> NoteAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var row = await db.AudienceSearches.AsNoTracking().SingleAsync(Ct);
        return (row.ReadAt, row.More);
    }
}
