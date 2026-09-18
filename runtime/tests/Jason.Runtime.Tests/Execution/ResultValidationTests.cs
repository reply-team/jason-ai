using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// A work item may declare the shape of the answer it wants. Until now that declaration was decoration: anything
/// an executor sent back was stored, so an agent could answer a request for findings with a paragraph explaining
/// how thoroughly it had looked, and the work would be recorded as a success. Persuasive prose is not a result
/// when a structure was asked for, and this is where that is decided.
/// <para>
/// The refusal is a 400 and the item stays <c>processing</c> on purpose: the executor still holds its attempt and
/// can answer again properly. One that gives up instead loses the attempt to <c>executor_exited</c>, which is the
/// accountable ending it has always had.
/// </para>
/// </summary>
public class ResultValidationTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Two fields, one of them required: the smallest shape that can tell an answer from an essay.</summary>
    private static JsonObject Shape() => new()
    {
        ["type"] = "object",
        ["properties"] = new JsonObject
        {
            ["findings"] = new JsonObject { ["type"] = "array" },
            ["confidence"] = new JsonObject { ["type"] = "number" },
        },
        ["required"] = new JsonArray("findings"),
    };

    [Fact]
    public async Task Persuasive_prose_is_refused_where_a_shape_was_asked_for()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, Shape());
        await using var ctx = database.Open();

        var refused = await Assert.ThrowsAsync<DomainException>(() => NewService(ctx).CompleteAsync(
            new WorkItemCompleteRequest(
                seeded.ItemId,
                seeded.AttemptId,
                CompletionStatus.Succeeded,
                Result: JsonValue.Create("I looked into it thoroughly and I am confident in the direction."),
                Error: null,
                Reason: null),
            Ct));

        Assert.Equal("result_invalid", refused.Code);
        Assert.False(refused.Retryable);
        Assert.NotNull(refused.Details);

        // The attempt is still the executor's: it was told, and it can answer again.
        await using var check = database.Open();
        var item = await check.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == seeded.ItemId, Ct);
        Assert.Equal(WorkItemStatus.Processing, item.Status);
        Assert.Null(item.Result);
    }

    [Fact]
    public async Task An_answer_of_the_declared_shape_completes()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, Shape());
        await using var ctx = database.Open();

        var answer = new JsonObject { ["findings"] = new JsonArray("one", "two"), ["confidence"] = 0.7 };
        var item = await NewService(ctx).CompleteAsync(
            new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, CompletionStatus.Succeeded, Result: answer, Error: null, Reason: null),
            Ct);

        Assert.Equal(WorkItemStatus.Succeeded, item.Status);
    }

    /// <summary>
    /// Progress is not an answer. Half of a result cannot be expected to have the shape of a whole one, and the
    /// whole point of reporting progress is that an executor killed halfway leaves behind what it had rather than
    /// nothing — a rule that refused checkpoints would simply stop executors from making them.
    /// </summary>
    [Fact]
    public async Task Progress_reported_halfway_is_not_held_to_the_shape_of_a_finished_answer()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, Shape());
        await using var ctx = database.Open();

        var item = await NewService(ctx).SetResultAsync(
            new WorkItemSetResultRequest(seeded.ItemId, seeded.AttemptId, new JsonObject { ["confidence"] = 0.4 }),
            Ct);

        Assert.Equal(WorkItemStatus.Processing, item.Status);
    }

    /// <summary>
    /// The hole that rule would otherwise leave: an executor could check in with a paragraph and then complete
    /// without a result, and the paragraph would stand as the answer. What is judged is therefore what will
    /// stand, whether this call carries it or an earlier one left it.
    /// </summary>
    [Fact]
    public async Task Completing_on_top_of_a_checkpoint_that_is_not_the_shape_is_still_refused()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, Shape());

        await using (var progress = database.Open())
        {
            await NewService(progress).SetResultAsync(
                new WorkItemSetResultRequest(
                    seeded.ItemId,
                    seeded.AttemptId,
                    JsonValue.Create("I am making excellent progress and will summarise shortly.")),
                Ct);
        }

        await using var ctx = database.Open();
        var refused = await Assert.ThrowsAsync<DomainException>(() => NewService(ctx).CompleteAsync(
            new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, CompletionStatus.Succeeded, Result: null, Error: null, Reason: null),
            Ct));

        Assert.Equal("result_invalid", refused.Code);

        await using var check = database.Open();
        Assert.Equal(WorkItemStatus.Processing, (await check.WorkItems.AsNoTracking().SingleAsync(w => w.PublicId == seeded.ItemId, Ct)).Status);
    }

    /// <summary>
    /// A failure is not the result. An executor that could not do the job has an error to report and no shape to
    /// satisfy, and holding it to one would leave it with nothing it could say at all.
    /// </summary>
    [Fact]
    public async Task A_failure_is_never_held_to_the_shape_of_a_result()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, Shape());
        await using var ctx = database.Open();

        var item = await NewService(ctx).CompleteAsync(
            new WorkItemCompleteRequest(
                seeded.ItemId,
                seeded.AttemptId,
                CompletionStatus.Failed,
                Result: null,
                Error: new CompletionErrorDto("source_unreachable", "The page would not load.", Details: null),
                Reason: null),
            Ct);

        Assert.NotEqual(WorkItemStatus.Succeeded, item.Status);
    }

    [Fact]
    public async Task An_item_that_declared_no_shape_takes_whatever_it_is_given()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, resultFormat: null);
        await using var ctx = database.Open();

        var item = await NewService(ctx).CompleteAsync(
            new WorkItemCompleteRequest(
                seeded.ItemId,
                seeded.AttemptId,
                CompletionStatus.Succeeded,
                Result: JsonValue.Create("whatever it liked"),
                Error: null,
                Reason: null),
            Ct);

        Assert.Equal(WorkItemStatus.Succeeded, item.Status);
    }

    /// <summary>
    /// Finishing with nothing at all, where a shape was asked for. There is no result to judge and therefore
    /// nothing that satisfies the schema, so it is refused for the same reason prose is: the work would be
    /// recorded as done with no answer to show for it.
    /// </summary>
    [Fact]
    public async Task A_success_with_no_result_at_all_is_refused_where_a_shape_was_asked_for()
    {
        using var database = new TestDatabase();
        var seeded = await SeedAsync(database, Shape());
        await using var ctx = database.Open();

        var refused = await Assert.ThrowsAsync<DomainException>(() => NewService(ctx).CompleteAsync(
            new WorkItemCompleteRequest(seeded.ItemId, seeded.AttemptId, CompletionStatus.Succeeded, Result: null, Error: null, Reason: null),
            Ct));

        Assert.Equal("result_invalid", refused.Code);
    }

    private static async Task<(string ItemId, string AttemptId)> SeedAsync(TestDatabase database, JsonNode? resultFormat)
    {
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var item = WorkItemFactory.NewAiRole(campaign, now: Noon, configure: w =>
        {
            w.Status = WorkItemStatus.Processing;
            w.ResultFormat = resultFormat;
        });
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(Ct);
        return (item.PublicId, attempt.PublicId);
    }

    private static ExecutorService NewService(JasonDbContext db)
    {
        var time = new FixedClock(Noon);
        var settings = TestOptions.Settings(o => o.RetryDelaySeconds = 0);
        return new ExecutorService(db, time, new AttemptOutcomes(new JournalWriter(time), time, settings), settings);
    }
}
