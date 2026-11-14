using System.Net;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Harness;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Campaigns;

/// <summary>
/// A campaign's own send mode, for its cold contacts and for its warm ones: a person's standing word about how the
/// campaign's sends go. It is a field of the campaign beside the person who set it, because the claim reads both; only a
/// named person sets it, each mode that moves is one <c>campaign_updated</c> line, and the campaign's answer shows the
/// campaign's own modes, null where it follows the installation.
/// </summary>
public class CampaignSendModeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Noon = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTimeOffset Nine = new(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly ActorRef Marta = new(ActorType.Human, "Marta");

    private static readonly ActorRef Ivo = new(ActorType.Human, "Ivo");

    /// <summary>A campaign made sets none, and says so with both members present and null.</summary>
    [Fact]
    public async Task A_new_campaign_sets_no_send_mode_of_its_own_and_says_null_for_both()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();

        var created = await NewService(db).CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        Assert.Equal(new SendModesDto(null, null), created.SendModes);
        Assert.Contains("\"send_modes\":{\"cold\":null,\"warm\":null}", JsonSerializer.Serialize(created, JasonJson.Options), StringComparison.Ordinal);
        var row = db.Campaigns.AsNoTracking().Single();
        Assert.Equal((null, null, null, null), (row.SendModeCold, row.SendModeColdSetBy, row.SendModeWarm, row.SendModeWarmSetBy));
    }

    /// <summary>
    /// Set, a mode is on the campaign with its setter's name beside it, and in the chronicle under the person who set it;
    /// asked for again, nothing is written; the other mode is left alone until it is named; null gives a mode back to the
    /// installation and takes the setter's name with it.
    /// </summary>
    [Fact]
    public async Task Each_mode_is_set_kept_and_given_back_with_one_line_per_change()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        var cold = await service.SetSendModeAsync(Set(created.Id, cold: Optional<SendMode?>.Of(SendMode.Automatic), reason: "she trusts the cold copy"), Ct);
        Assert.Equal(new SendModesDto(SendMode.Automatic, null), cold.SendModes);
        await service.SetSendModeAsync(Set(created.Id, cold: Optional<SendMode?>.Of(SendMode.Automatic)), Ct);
        var warm = await service.SetSendModeAsync(Set(created.Id, warm: Optional<SendMode?>.Of(SendMode.EveryMessage)), Ct);
        Assert.Equal(new SendModesDto(SendMode.Automatic, SendMode.EveryMessage), warm.SendModes);

        var row = db.Campaigns.AsNoTracking().Single();
        Assert.Equal((SendMode.Automatic, "Marta", SendMode.EveryMessage, "Marta"), (row.SendModeCold, row.SendModeColdSetBy, row.SendModeWarm, row.SendModeWarmSetBy));

        var cleared = await service.SetSendModeAsync(Set(created.Id, cold: Optional<SendMode?>.Of(null)), Ct);
        Assert.Equal(new SendModesDto(null, SendMode.EveryMessage), cleared.SendModes);
        row = db.Campaigns.AsNoTracking().Single();
        Assert.Equal((null, null, SendMode.EveryMessage, "Marta"), (row.SendModeCold, row.SendModeColdSetBy, row.SendModeWarm, row.SendModeWarmSetBy));

        var lines = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.CampaignUpdated).OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal(
            [
                ("send_mode_cold", (string?)null, (string?)"automatic", "she trusts the cold copy"),
                ("send_mode_warm", null, "every_message", null),
                ("send_mode_cold", "automatic", null, null),
            ],
            lines.Select(line => (line.Key!, (string?)line.Old, (string?)line.New, line.Reason)));
        Assert.All(lines, line => Assert.Equal((ActorType.Human, "Marta"), (line.ActorType, line.ActorId)));
    }

    /// <summary>
    /// The modes in force on the campaign's answer: by default every message, the installation's preference where the
    /// campaign sets none, and the campaign's own over it; each says where it comes from, and never who set it.
    /// </summary>
    [Fact]
    public async Task The_answer_says_the_modes_in_force_and_where_each_comes_from()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        Assert.Equal(InForce((SendMode.EveryMessage, SendModeSource.Default), (SendMode.EveryMessage, SendModeSource.Default)), created.SendModesInForce);

        var clock = new FixedClock(Noon);
        await new Jason.Runtime.Preferences.PreferenceService(db, new JournalWriter(clock), clock)
            .SetAsync(new PreferenceSetRequest("send_mode.warm", new System.Text.Json.Nodes.JsonObject { ["mode"] = "automatic" }, Marta, null), Ct);
        var own = await service.SetSendModeAsync(Set(created.Id, cold: Optional<SendMode?>.Of(SendMode.Automatic)), Ct);

        Assert.Equal(InForce((SendMode.Automatic, SendModeSource.Campaign), (SendMode.Automatic, SendModeSource.Preference)), own.SendModesInForce);
        var json = JsonSerializer.Serialize(own, JasonJson.Options);
        Assert.Contains(
            "\"send_modes_in_force\":{\"cold\":{\"mode\":\"automatic\",\"source\":\"campaign\"},\"warm\":{\"mode\":\"automatic\",\"source\":\"preference\"}}",
            json,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Marta", json, StringComparison.Ordinal);
        Assert.Equal(own.SendModesInForce, (await service.GetAsync(new CampaignGetRequest(created.Id), Ct)).SendModesInForce);
    }

    /// <summary>Both in one request are two lines, one per mode, in the same act.</summary>
    [Fact]
    public async Task Both_modes_at_once_are_one_line_each()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        var both = await service.SetSendModeAsync(Set(created.Id, Optional<SendMode?>.Of(SendMode.Automatic), Optional<SendMode?>.Of(SendMode.Automatic)), Ct);

        Assert.Equal(new SendModesDto(SendMode.Automatic, SendMode.Automatic), both.SendModes);
        Assert.Equal(
            ["send_mode_cold", "send_mode_warm"],
            await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.CampaignUpdated).OrderBy(e => e.Id).Select(e => e.Key!).ToListAsync(Ct));
    }

    /// <summary>
    /// A second person who sets the mode a campaign already holds takes it over: the claim releases in the name beside
    /// the mode, so that name moves to theirs and one line says so, its old and new the same word under the new setter;
    /// what is in force does not move.
    /// </summary>
    [Fact]
    public async Task A_second_person_setting_the_same_mode_becomes_its_setter_with_one_line()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        var first = await service.SetSendModeAsync(Set(created.Id, cold: Optional<SendMode?>.Of(SendMode.Automatic)), Ct);

        var second = await service.SetSendModeAsync(Set(created.Id, cold: Optional<SendMode?>.Of(SendMode.Automatic), actor: Ivo, reason: "Ivo answers for it now"), Ct);

        Assert.Equal(first.SendModesInForce, second.SendModesInForce);
        Assert.Equal(new SendModesDto(SendMode.Automatic, null), second.SendModes);
        var row = db.Campaigns.AsNoTracking().Single();
        Assert.Equal((SendMode.Automatic, "Ivo"), (row.SendModeCold, row.SendModeColdSetBy));
        var lines = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.CampaignUpdated).OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.Equal(2, lines.Count);
        var line = lines[1];
        Assert.Equal(
            ("send_mode_cold", (string?)"automatic", (string?)"automatic", (string?)"Ivo answers for it now", ActorType.Human, (string?)"Ivo"),
            (line.Key!, (string?)line.Old, (string?)line.New, line.Reason, line.ActorType, line.ActorId));
    }

    /// <summary>The same person setting the mode they already set writes nothing, and keeps the campaign as it was.</summary>
    [Fact]
    public async Task The_same_person_setting_the_same_mode_again_writes_nothing()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await service.SetSendModeAsync(Set(created.Id, cold: Optional<SendMode?>.Of(SendMode.Automatic)), Ct);
        var before = db.Campaigns.AsNoTracking().Single().UpdatedAt;

        await service.SetSendModeAsync(Set(created.Id, cold: Optional<SendMode?>.Of(SendMode.Automatic)), Ct);

        var row = db.Campaigns.AsNoTracking().Single();
        Assert.Equal((SendMode.Automatic, "Marta", before), (row.SendModeCold, row.SendModeColdSetBy, row.UpdatedAt));
        Assert.Equal(1, await db.Journal.AsNoTracking().CountAsync(e => e.Kind == JournalKinds.CampaignUpdated, Ct));
    }

    public static TheoryData<ActorRef?, string> NotAPerson => new()
    {
        { new ActorRef(ActorType.Role, "manager"), "send_mode_not_human" },
        { new ActorRef(ActorType.Attempt, "att_01K7QN0000000000000000000A"), "send_mode_not_human" },
        { new ActorRef(ActorType.Human), "actor_required" },
        { null, "actor_required" },
    };

    /// <summary>Only a person who gives their name sets it: a role, an attempt and a nameless person are refused, and nothing moves.</summary>
    [Theory]
    [MemberData(nameof(NotAPerson))]
    public async Task Only_a_named_person_sets_it(ActorRef? actor, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        var refused = await Assert.ThrowsAsync<InvalidRequestException>(() =>
            service.SetSendModeAsync(Set(created.Id, cold: Optional<SendMode?>.Of(SendMode.Automatic)) with { Actor = actor }, Ct));

        Assert.Equal(code, refused.Code);
        Assert.Null(db.Campaigns.AsNoTracking().Single().SendModeCold);
        Assert.DoesNotContain(await db.Journal.AsNoTracking().Select(e => e.Kind).ToListAsync(Ct), kind => kind == JournalKinds.CampaignUpdated);
    }

    [Fact]
    public async Task A_request_that_names_neither_mode_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => service.SetSendModeAsync(Set(created.Id), Ct));

        var detail = Assert.Single(refused.Details!);
        Assert.Equal(("cold", "required"), (detail.Field, detail.Code));
    }

    [Fact]
    public async Task An_archived_campaign_is_refused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var service = NewService(db);
        var created = await service.CreateAsync(new CampaignCreateRequest("LatAm", null, null, null), Ct);
        await service.ArchiveAsync(new CampaignTransitionRequest(created.Id, null, null), Ct);

        var refused = await Assert.ThrowsAsync<ConflictException>(() =>
            service.SetSendModeAsync(Set(created.Id, cold: Optional<SendMode?>.Of(SendMode.Automatic)), Ct));

        Assert.Equal("campaign_archived", refused.Code);
    }

    /// <summary>
    /// Over the API: set, read back by <c>campaign.get</c>, given back, refused to a role, and refused a mode word the
    /// write does not take. <c>campaign.update</c> takes no mode, whatever member a body names, so the person-only act has
    /// one door.
    /// </summary>
    [Fact]
    public async Task A_campaign_s_send_mode_is_set_read_given_back_and_refused_over_the_api()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, clock: new FixedClock(Nine));
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { name = "Spring outreach" }, Ct);
        var marta = new { type = "human", id = "Marta" };

        var set = await api.RecordAsync<CampaignDto>(
            Operations.CampaignSetSendMode, "set", new { campaign_id = campaign.Id, cold = "automatic", actor = marta, reason = "she read the first ten" }, Ct);
        Assert.Equal(new SendModesDto(SendMode.Automatic, null), set.SendModes);

        var read = await api.RecordAsync<CampaignDto>(Operations.CampaignGet, "send-mode-override", new { campaign_id = campaign.Id }, Ct);
        Assert.Equal(new SendModesDto(SendMode.Automatic, null), read.SendModes);

        var cleared = await api.RecordAsync<CampaignDto>(Operations.CampaignSetSendMode, "cleared", new { campaign_id = campaign.Id, cold = (string?)null, actor = marta }, Ct);
        Assert.Equal(new SendModesDto(null, null), cleared.SendModes);

        var role = await api.RecordErrorAsync(
            Operations.CampaignSetSendMode, "not-human", new { campaign_id = campaign.Id, warm = "automatic", actor = new { type = "role", id = "manager" } }, HttpStatusCode.BadRequest, Ct);
        Assert.Equal("send_mode_not_human", role.Code);

        var (word, _) = await api.PostAsync(Operations.CampaignSetSendMode, new { campaign_id = campaign.Id, cold = "default", actor = marta }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, word);

        await api.PostAsync(Operations.CampaignUpdate, new { campaign_id = campaign.Id, send_modes = new { cold = "automatic" }, cold = "automatic" }, Ct);
        Assert.Equal(new SendModesDto(null, null), (await api.PostOkAsync<CampaignDto>(Operations.CampaignGet, new { campaign_id = campaign.Id }, Ct)).SendModes);
    }

    private static SendModesInForce InForce((SendMode Mode, SendModeSource Source) cold, (SendMode Mode, SendModeSource Source) warm) =>
        new(new SendModeInForce(cold.Mode, cold.Source), new SendModeInForce(warm.Mode, warm.Source));

    private static CampaignSetSendModeRequest Set(
        string campaignId,
        Optional<SendMode?> cold = default,
        Optional<SendMode?> warm = default,
        string? reason = null,
        ActorRef? actor = null) =>
        new(campaignId, cold, warm, actor ?? Marta, reason);

    private static CampaignService NewService(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        return new(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestHarness.FoldersFor(db));
    }
}
