using System.Net;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// A role may pause a campaign; only a person, or their own session on their word, starts one. A start, or a resume, sent
/// from inside a run is refused by the header before its body is read, so before its actor, whatever actor it names: a
/// manager's check-in that started its own campaign under a role's or a person's name would write a start that is not the
/// check-in's own line, and that line would summon the next check-in, which could do the same. Outside any run a start is
/// what it always was, and it summons the manager.
/// </summary>
public class CampaignStartInsideARunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Nine = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static readonly object Ada = new { type = "human", id = "ada" };

    public static TheoryData<string> Actors => ["attempt", "role", "human"];

    /// <summary>The default actor of a run's CLI, a role's name and a person's name are each refused alike.</summary>
    [Theory]
    [MemberData(nameof(Actors))]
    [Critical(Family.M1, "a run cannot start a campaign, so no check-in can summon its successor by a start")]
    public async Task A_start_from_inside_a_check_ins_run_is_refused_whatever_actor_it_names(string actor)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var campaign = await PausedAsync(api);
        var attempt = await RunAsync(api, campaign, ManagerCheckIn.Role, checkIn: true);

        var (status, text) = await api.PostAsync(Operations.CampaignStart, new { campaign_id = campaign, actor = Actor(actor, attempt) }, Inside(attempt), Ct);

        AssertRefused(status, text);
        await AssertStillPausedAsync(api, campaign);
    }

    [Fact]
    public async Task A_start_from_inside_the_run_of_a_role_that_is_no_check_in_is_refused()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var campaign = await PausedAsync(api);
        var attempt = await RunAsync(api, campaign, "planner", checkIn: false);

        var (status, text) = await api.PostAsync(Operations.CampaignStart, new { campaign_id = campaign, actor = Actor("attempt", attempt) }, Inside(attempt), Ct);

        AssertRefused(status, text);
        await AssertStillPausedAsync(api, campaign);
    }

    /// <summary>Stopping is protective, so a run may still pause its campaign.</summary>
    [Fact]
    public async Task A_pause_from_inside_a_run_still_pauses()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring outreach" }, Ct)).Id;
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign, actor = Ada }, Ct);
        var attempt = await RunAsync(api, campaign, ManagerCheckIn.Role, checkIn: true);

        var (status, text) = await api.PostAsync(Operations.CampaignPause, new { campaign_id = campaign, actor = Actor("attempt", attempt) }, Inside(attempt), Ct);

        Assert.True(status == HttpStatusCode.OK, text);
        Assert.Equal(CampaignStatus.Paused, JsonSerializer.Deserialize<CampaignDto>(text, JasonJson.Options)!.Status);
    }

    /// <summary>A person's start outside any run writes its line and summons the campaign's manager, as before.</summary>
    [Fact]
    public async Task A_persons_start_outside_any_run_writes_its_line_and_summons_the_manager()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring outreach" }, Ct)).Id;

        var started = await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign, actor = Ada }, Ct);

        Assert.Equal(CampaignStatus.Active, started.Status);
        Assert.Equal(1, await StartLinesAsync(api, campaign));
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<Summoner>().SummonAsync(db, Ct));
        var checkIn = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Role == ManagerCheckIn.Role, Ct);
        Assert.Equal(JournalKinds.CampaignStarted, (string?)checkIn.Context["trigger"]);
    }

    private static Dictionary<string, string> Inside(string attempt) => new() { ["Jason-Attempt"] = attempt };

    private static object Actor(string kind, string attempt) => kind switch
    {
        "attempt" => new { type = "attempt", id = attempt },
        "role" => new { type = "role", id = ManagerCheckIn.Role },
        _ => Ada,
    };

    private static void AssertRefused(HttpStatusCode status, string text)
    {
        Assert.True(status == HttpStatusCode.Forbidden, $"campaign.start answered {(int)status}: {text}");
        var error = JsonSerializer.Deserialize<ErrorResponse>(text, JasonJson.Options)!.Error;
        Assert.Equal("decided_inside_a_run", error.Code);
        Assert.False(error.Retryable);
    }

    /// <summary>The campaign stays paused, and the one start line is the person's first start, written before the pause.</summary>
    private static async Task AssertStillPausedAsync(RuntimeApiFixture api, string campaign)
    {
        var read = await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = campaign }, Ct);
        Assert.Equal(CampaignStatus.Paused, read.Status);
        Assert.Equal(1, await StartLinesAsync(api, campaign));
    }

    private static async Task<int> StartLinesAsync(RuntimeApiFixture api, string campaign)
    {
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        return await db.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.CampaignStarted && e.Campaign!.PublicId == campaign, Ct);
    }

    /// <summary>A campaign a person started and then paused.</summary>
    private static async Task<string> PausedAsync(RuntimeApiFixture api)
    {
        var campaign = (await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring outreach" }, Ct)).Id;
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { campaign_id = campaign, actor = Ada }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignPause, new { campaign_id = campaign, actor = Ada }, Ct);
        return campaign;
    }

    /// <summary>A role's work in the campaign under its running attempt; a check-in is the manager's work the runtime created.</summary>
    private static async Task<string> RunAsync(RuntimeApiFixture api, string campaignId, string role, bool checkIn)
    {
        var now = Nine.UtcDateTime;
        using var scope = api.Resolve<IServiceScopeFactory>().CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var campaign = await db.Campaigns.SingleAsync(c => c.PublicId == campaignId, Ct);
        var item = WorkItemFactory.NewAiRole(campaign, role, now: now);
        if (checkIn)
        {
            item.CreatedByType = ActorType.System;
            item.CreatedById = "dispatcher";
        }

        item.Status = WorkItemStatus.Processing;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, now);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return attempt.PublicId;
    }
}
