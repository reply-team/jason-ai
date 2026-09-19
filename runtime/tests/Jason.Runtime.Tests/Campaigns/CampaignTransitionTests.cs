using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

public class CampaignTransitionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The three lifecycle operations, so one theory can drive the whole matrix.</summary>
    public enum Verb
    {
        Start,
        Pause,
        Archive,
    }

    [Theory]
    [InlineData(CampaignStatus.Draft, Verb.Start, JournalKinds.CampaignStarted)]
    [InlineData(CampaignStatus.Paused, Verb.Start, JournalKinds.CampaignStarted)]
    [InlineData(CampaignStatus.Active, Verb.Pause, JournalKinds.CampaignPaused)]
    [InlineData(CampaignStatus.Draft, Verb.Archive, JournalKinds.CampaignArchived)]
    [InlineData(CampaignStatus.Active, Verb.Archive, JournalKinds.CampaignArchived)]
    [InlineData(CampaignStatus.Paused, Verb.Archive, JournalKinds.CampaignArchived)]
    public async Task A_legal_transition_moves_the_campaign_and_journals_exactly_that_move(CampaignStatus from, Verb verb, string kind)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var id = await SeedAsync(service, from);
        var seeded = await db.Journal.CountAsync(Ct);
        clock.Advance(TimeSpan.FromMinutes(1));

        var after = await InvokeAsync(service, verb, id);

        var target = TargetOf(verb);
        Assert.Equal(target, after.Status);
        Assert.Equal(Noon.AddMinutes(1), after.UpdatedAt);
        Assert.Equal(target == CampaignStatus.Archived ? Noon.AddMinutes(1) : null, after.ArchivedAt);

        var entries = await db.Journal.OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal(seeded + 1, entries.Count);
        var entry = entries[^1];
        Assert.Equal(kind, entry.Kind);
        Assert.Equal("status", entry.Key);
        Assert.Equal(SnakeCaseEnumConverter<CampaignStatus>.Format(from), (string?)entry.Old);
        Assert.Equal(SnakeCaseEnumConverter<CampaignStatus>.Format(target), (string?)entry.New);
        Assert.Equal("because", entry.Reason);
    }

    [Theory]
    [InlineData(CampaignStatus.Active, Verb.Start)]
    [InlineData(CampaignStatus.Paused, Verb.Pause)]
    [InlineData(CampaignStatus.Archived, Verb.Archive)]
    public async Task Repeating_the_transition_the_campaign_is_already_in_changes_nothing(CampaignStatus state, Verb verb)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var clock = new FixedClock(Noon);
        var service = NewService(db, clock);
        var id = await SeedAsync(service, state);
        var seeded = await db.Journal.CountAsync(Ct);
        var before = await service.GetAsync(new CampaignGetRequest(id), Ct);
        clock.Advance(TimeSpan.FromMinutes(1));

        var after = await InvokeAsync(service, verb, id);

        Assert.Equal(state, after.Status);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.ArchivedAt, after.ArchivedAt);
        Assert.Equal(seeded, await db.Journal.CountAsync(Ct));
    }

    [Fact]
    public async Task A_draft_campaign_cannot_be_paused()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await SeedAsync(service, CampaignStatus.Draft);

        var error = await Assert.ThrowsAsync<ConflictException>(() => InvokeAsync(service, Verb.Pause, id));

        Assert.Equal("invalid_transition", error.Code);
        Assert.False(error.Retryable);
        Assert.Single(await db.Journal.ToListAsync(Ct));
    }

    [Theory]
    [InlineData(Verb.Start)]
    [InlineData(Verb.Pause)]
    public async Task An_archived_campaign_refuses_further_transitions(Verb verb)
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await SeedAsync(service, CampaignStatus.Archived);

        var error = await Assert.ThrowsAsync<ConflictException>(() => InvokeAsync(service, verb, id));

        Assert.Equal("campaign_archived", error.Code);
    }

    [Fact]
    public async Task An_archived_campaign_refuses_updates_and_context_edits()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));
        var id = await SeedAsync(service, CampaignStatus.Archived);

        var renamed = await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateAsync(new CampaignUpdateRequest(id, Optional<string?>.Of("EMEA"), Optional<string?>.Absent, Optional<int?>.Absent, null, null), Ct));
        var edited = await Assert.ThrowsAsync<ConflictException>(
            () => service.UpdateContextAsync(new CampaignUpdateContextRequest(id, new JsonObject { ["icp"] = "founders" }, null, null, null), Ct));

        Assert.Equal("campaign_archived", renamed.Code);
        Assert.Equal("campaign_archived", edited.Code);
    }

    [Fact]
    public async Task A_transition_on_an_unknown_campaign_is_not_found()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var service = NewService(db, new FixedClock(Noon));

        var error = await Assert.ThrowsAsync<NotFoundException>(() => InvokeAsync(service, Verb.Start, "cmp_01JASONNOTHERE"));

        Assert.Equal("campaign_not_found", error.Code);
    }

    private static CampaignStatus TargetOf(Verb verb) => verb switch
    {
        Verb.Start => CampaignStatus.Active,
        Verb.Pause => CampaignStatus.Paused,
        Verb.Archive => CampaignStatus.Archived,
        _ => throw new ArgumentOutOfRangeException(nameof(verb)),
    };

    private static Task<CampaignDto> InvokeAsync(CampaignService service, Verb verb, string id)
    {
        var request = new CampaignTransitionRequest(id, null, "because");
        return verb switch
        {
            Verb.Start => service.StartAsync(request, Ct),
            Verb.Pause => service.PauseAsync(request, Ct),
            Verb.Archive => service.ArchiveAsync(request, Ct),
            _ => throw new ArgumentOutOfRangeException(nameof(verb)),
        };
    }

    private static async Task<string> SeedAsync(CampaignService service, CampaignStatus status)
    {
        var campaign = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        var request = new CampaignTransitionRequest(campaign.Id, null, null);
        switch (status)
        {
            case CampaignStatus.Draft:
                break;
            case CampaignStatus.Active:
                await service.StartAsync(request, Ct);
                break;
            case CampaignStatus.Paused:
                await service.StartAsync(request, Ct);
                await service.PauseAsync(request, Ct);
                break;
            case CampaignStatus.Archived:
                await service.ArchiveAsync(request, Ct);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return campaign.Id;
    }

    private static CampaignService NewService(JasonDbContext db, TimeProvider clock) => new(db, new JournalWriter(clock), clock, TestCanceller.New(clock));
}
