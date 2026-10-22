using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// Where the API measures a value against a schema — a provider operation's arguments when an item is written, an
/// answer against the shape its item asked for — a pattern that did not finish matching is not a verdict on the value.
/// Nothing has happened yet, so the request is answered as one to send again: <c>validation_timed_out</c>, 503,
/// retryable, and never the 400 that would tell its sender to change something that may be perfectly right.
/// </summary>
public sealed class PatternTimeoutAtTheApiTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A complete set of <c>list_membership.add</c> arguments; the channel is the one held to a pattern.</summary>
    private static JsonObject Membership(string channel = "email") => new()
    {
        ["list"] = new JsonObject { ["external_id"] = "lst_7" },
        ["channel"] = channel,
    };

    [Fact]
    public async Task Arguments_whose_pattern_times_out_once_are_measured_warm_and_the_item_is_created()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db);
        var timing = new TimingOut(1);

        var item = await NewService(db, timing).CreateAsync(Create(campaign.PublicId, Membership()), Ct);

        Assert.Equal("list_membership.add", item.Operation);
        Assert.Equal(2, timing.Calls);
    }

    [Fact]
    public async Task Arguments_whose_pattern_times_out_twice_are_answered_as_a_request_to_send_again()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db);

        var error = await Assert.ThrowsAsync<DomainException>(
            () => NewService(db, TimingOut.Always()).CreateAsync(Create(campaign.PublicId, Membership()), Ct));

        Assert.Equal((503, "validation_timed_out", true), (error.StatusCode, error.Code, error.Retryable));
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("context.input/channel", "pattern_timed_out"), (detail.Field, detail.Code));
        Assert.Empty(await db.WorkItems.AsNoTracking().ToListAsync(Ct));
    }

    /// <summary>
    /// A value that is wrong somewhere else is wrong whatever the unanswered pattern would have said, so the verdict
    /// decides and the answer is the ordinary refusal — naming only what is known to be wrong.
    /// </summary>
    [Fact]
    public async Task Arguments_wrong_elsewhere_are_refused_for_what_is_wrong_and_not_for_the_timeout()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var campaign = Seed(db);
        var arguments = Membership();
        arguments["list"] = "lst_7";

        var error = await Assert.ThrowsAsync<ValidationException>(
            () => NewService(db, TimingOut.Always()).CreateAsync(Create(campaign.PublicId, arguments), Ct));

        Assert.Equal(("context.input/list", "invalid"), (Assert.Single(error.Details!).Field, error.Details![0].Code));
    }

    [Fact]
    public async Task A_patch_whose_arguments_time_out_twice_is_answered_as_a_request_to_send_again()
    {
        using var database = new TestDatabase();
        using var db = database.Open();
        var item = WorkItemFactory.NewProviderOp(WorkItemFactory.NewCampaign(), "list_membership.add");
        item.Context = new JsonObject { ["input"] = Membership() };
        db.WorkItems.Add(item);
        db.SaveChanges();

        var error = await Assert.ThrowsAsync<DomainException>(() => NewService(db, TimingOut.Always()).UpdateAsync(
            new WorkItemUpdateRequest(item.PublicId, new JsonObject { ["input"] = Membership("sms") }, null, default, default, default, default, default, default, default, default, null, null),
            Ct));

        Assert.Equal((503, "validation_timed_out", true), (error.StatusCode, error.Code, error.Retryable));
        Assert.Equal("email", (await db.WorkItems.AsNoTracking().SingleAsync(Ct)).Context["input"]!["channel"]!.GetValue<string>());
    }

    /// <summary>Through the runtime's own composition: the host's timing reaches the site, and the envelope says 503.</summary>
    [Fact]
    [Critical(Family.R2, "the timeout answer")]
    public async Task The_api_answers_503_validation_timed_out_retryable()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct, configureServices: services => services.AddSingleton<PatternTiming>(TimingOut.Always()));
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "LatAm founders" }, Ct);

        var error = await api.PostErrorAsync(
            Operations.WorkItemCreate,
            new { CampaignId = campaign.Id, Kind = "provider_op", Operation = "list_membership.add", Context = new JsonObject { ["input"] = Membership() } },
            HttpStatusCode.ServiceUnavailable,
            Ct);

        Assert.Equal(("validation_timed_out", true), (error.Code, error.Retryable));
    }

    [Fact]
    public async Task An_answer_whose_shape_times_out_twice_is_answered_as_one_to_send_again_and_the_attempt_stays()
    {
        using var database = new TestDatabase();
        var seeded = await SeedProcessingAsync(database);
        await using var ctx = database.Open();

        var error = await Assert.ThrowsAsync<DomainException>(() => NewExecutor(ctx, TimingOut.Always()).CompleteAsync(Answer(seeded), Ct));

        Assert.Equal((503, "validation_timed_out", true), (error.StatusCode, error.Code, error.Retryable));
        Assert.Equal("/code", Assert.Single(error.Details!).Field);
        await using var check = database.Open();
        Assert.Equal(WorkItemStatus.Processing, (await check.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == seeded.ItemId, Ct)).Status);
    }

    [Fact]
    public async Task An_answer_whose_shape_times_out_once_is_measured_warm_and_completes()
    {
        using var database = new TestDatabase();
        var seeded = await SeedProcessingAsync(database);
        await using var ctx = database.Open();

        var item = await NewExecutor(ctx, new TimingOut(1)).CompleteAsync(Answer(seeded), Ct);

        Assert.Equal(WorkItemStatus.Succeeded, item.Status);
    }

    private static WorkItemCompleteRequest Answer((string ItemId, string AttemptId) seeded) =>
        new(seeded.ItemId, seeded.AttemptId, CompletionStatus.Succeeded, Result: new JsonObject { ["code"] = "AB-12" }, Error: null, Reason: null);

    private static WorkItemService NewService(JasonDbContext db, PatternTiming timing)
    {
        var clock = new FixedClock(Noon);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings(), timing);
    }

    private static ExecutorService NewExecutor(JasonDbContext db, PatternTiming timing)
    {
        var time = new FixedClock(Noon);
        var settings = TestOptions.Settings(o => o.RetryDelaySeconds = 0);
        return new ExecutorService(db, time, new AttemptOutcomes(new JournalWriter(time), time, settings), settings, timing);
    }

    private static WorkItemCreateRequest Create(string campaignId, JsonObject arguments) =>
        new(campaignId, WorkItemKind.ProviderOp, null, "list_membership.add", null, null, null, null, null, null, null, null, new JsonObject { ["input"] = arguments }, null, null, null);

    private static Campaign Seed(JasonDbContext db)
    {
        var campaign = WorkItemFactory.NewCampaign();
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign;
    }

    /// <summary>An agent's item that asked for an answer with a patterned code, and the attempt running it.</summary>
    private static async Task<(string ItemId, string AttemptId)> SeedProcessingAsync(TestDatabase database)
    {
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon.UtcDateTime);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon.UtcDateTime, configure: w =>
        {
            w.Status = WorkItemStatus.Processing;
            w.ResultFormat = JsonNode.Parse("""{"type":"object","properties":{"code":{"type":"string","pattern":"^[A-Z]{2}-[0-9]{2}$"}},"required":["code"]}""");
        });
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon.UtcDateTime);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (item.PublicId, attempt.PublicId);
    }
}
