using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.WorkItems;

/// <summary>
/// <c>workitem.create</c> with <c>wait_on</c>: the item is written already waiting on the items it names, each an item of
/// the same campaign named once, so it is never claimed before they have ended. A refusal writes nothing.
/// </summary>
public class WorkItemWaitOnTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_item_created_waiting_is_waiting_names_its_items_in_order_and_is_not_eligible()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var first = Seed(db, WorkItemFactory.NewAiRole(campaign));
        var second = Seed(db, WorkItemFactory.NewAiRole(campaign));

        var made = await Service(db).CreateAsync(Request(campaign.PublicId) with { WaitOn = [second.PublicId, $" {first.PublicId} "] }, Ct);

        Assert.Equal(WorkItemStatus.Waiting, made.Status);
        Assert.False(made.Eligible);
        Assert.Equal([second.PublicId, first.PublicId], made.Wait!.On.Select(on => on.WorkItemId));
        Assert.Equal(WaitMode.All, made.Wait.Mode);
        var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItem!.PublicId == made.Id, Ct);
        Assert.Equal((WaitStatus.Open, (int?)null), (wait.Status, wait.AttemptId));
    }

    public static TheoryData<string, string, string> Refusals() => new()
    {
        { "an empty list", "wait_on", "invalid" },
        { "a blank id", "wait_on[0]", "invalid" },
        { "the same item twice", "wait_on[1]", "duplicate" },
        { "more than a wait may name", "wait_on", "too_many" },
        { "an item nobody made", "wait_on[0]", "unknown" },
        { "an item of another campaign", "wait_on[0]", "unknown" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Each_wait_a_create_may_not_name_is_refused_at_its_field_and_writes_nothing(string why, string field, string code)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var mine = Seed(db, WorkItemFactory.NewAiRole(campaign));
        var theirs = Seed(db, WorkItemFactory.NewAiRole(Seed(db, WorkItemFactory.NewCampaign("Another"))));
        IReadOnlyList<string> waitOn = why switch
        {
            "an empty list" => [],
            "a blank id" => [" "],
            "the same item twice" => [mine.PublicId, mine.PublicId],
            "more than a wait may name" => [.. Enumerable.Range(0, WorkItemWaitRequest.MaxWaitItems + 1).Select(n => $"wi_{n}")],
            "an item nobody made" => ["wi_nobody"],
            "an item of another campaign" => [theirs.PublicId],
            _ => throw new ArgumentOutOfRangeException(nameof(why), why, null),
        };
        var before = await db.WorkItems.CountAsync(Ct);

        var refused = await Assert.ThrowsAsync<ValidationException>(() => Service(db).CreateAsync(Request(campaign.PublicId) with { WaitOn = waitOn }, Ct));

        var detail = Assert.Single(refused.Details!);
        Assert.Equal((field, code), (detail.Field, detail.Code));
        db.ChangeTracker.Clear();
        Assert.Equal(before, await db.WorkItems.CountAsync(Ct));
        Assert.Empty(await db.WorkItemWaits.ToListAsync(Ct));
    }

    /// <summary>The items waited on are part of what a keyed create asks for: the same key with another wait is a different request.</summary>
    [Fact]
    public async Task A_key_used_with_another_wait_is_refused_as_reused()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = Seed(db, WorkItemFactory.NewCampaign());
        var first = Seed(db, WorkItemFactory.NewAiRole(campaign));
        var second = Seed(db, WorkItemFactory.NewAiRole(campaign));
        var request = Request(campaign.PublicId) with { IdempotencyKey = "compose-1", WaitOn = [first.PublicId] };

        var made = await Service(db).CreateAsync(request, Ct);
        var again = await Service(db).CreateAsync(request, Ct);
        var refused = await Assert.ThrowsAsync<ConflictException>(() => Service(db).CreateAsync(request with { WaitOn = [second.PublicId] }, Ct));

        Assert.Equal(made.Id, again.Id);
        Assert.Equal("idempotency_key_reused", refused.Code);
        Assert.Equal(1, await db.WorkItemWaits.CountAsync(Ct));
    }

    /// <summary>A key stored before a create could wait on anything still matches the same request without the field.</summary>
    [Fact]
    public void A_request_that_names_no_wait_digests_as_it_always_did()
    {
        var request = Request("cmp_1") with { IdempotencyKey = "k" };

        Assert.Equal(CreationKey.Digest(request), CreationKey.Digest(request with { WaitOn = null, AskPersonNote = null }));
        Assert.NotEqual(CreationKey.Digest(request), CreationKey.Digest(request with { WaitOn = ["wi_1"] }));
    }

    private static WorkItemService Service(JasonDbContext db)
    {
        var clock = new FixedClock(Noon);
        var journal = new JournalWriter(clock);
        return new WorkItemService(db, journal, clock, TestCanceller.New(clock), TestOptions.PluginSettings(), outcomes: new AttemptOutcomes(journal, clock, TestOptions.Settings()));
    }

    private static WorkItemCreateRequest Request(string campaignId) =>
        new(campaignId, WorkItemKind.AiRole, "researcher", null, null, null, null, null, null, null, null, null, null, null, null, null);

    private static T Seed<T>(JasonDbContext db, T entity)
        where T : class
    {
        db.Add(entity);
        db.SaveChanges();
        return entity;
    }
}
