using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// What an attempt of work a provider block holds does to the provider's blocks, and how its item parks: a blocked
/// answer opens the block it names or sees it again, and the item waits on it uncounted; a call that gets past a block
/// clears it; a trial that says nothing puts the next one a cadence off. Made-up plugins, bindings and senders.
/// </summary>
public class ProviderBlockOutcomesTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// An attempt that answered blocked ends <c>blocked</c>, keeping its error and its block, and is never counted; its
    /// item waits on the block with no mode, moment or deadline, and the line that says so names what blocks it.
    /// </summary>
    [Fact]
    public async Task A_blocked_attempt_ends_blocked_uncounted_and_its_item_waits_on_the_block()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var outcomes = NewOutcomes();
        var (item, attempt) = await RunningAsync(db, WorkItemFactory.NewCampaign(now: Noon));
        var error = new AttemptErrorDto(AttemptErrors.ProviderBlocked, "The account is out of credit.", true, null, null, FailureClass.Blocked, Said("account"));

        await outcomes.AnsweredAsync(db, item, attempt, BlockRows.Plugin, Input("snd_marta"), BlockAnswer.Blocked, Said("account"), error, Ct);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(AttemptStatus.Blocked, attempt.Status);
        Assert.Equal(Noon, attempt.FinishedAt);
        Assert.Equal(error, attempt.Error);
        Assert.True(item.AttemptCount == 0, $"A blocked attempt was counted: the item has {item.AttemptCount} attempt(s).");
        Assert.Equal(WorkItemStatus.Waiting, item.Status);

        var block = await db.ProviderBlocks.AsNoTracking().SingleAsync(Ct);
        var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(Ct);
        Assert.True(
            wait is { Status: WaitStatus.Open, Mode: null, Until: null, Deadline: null } && wait.ProviderBlockId == block.Id && wait.AttemptId == attempt.Id,
            $"The wait is {wait.Status}, mode {wait.Mode}, until {wait.Until}, deadline {wait.Deadline}, block {wait.ProviderBlockId}, attempt {wait.AttemptId}.");

        var waiting = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.WorkItemWaiting, Ct);
        Assert.Equal(attempt.PublicId, waiting.Key);
        Assert.Equal(
            $$$"""{"on":[],"mode":null,"until":null,"deadline":null,"blocked_by":{"provider_block_id":"{{{block.PublicId}}}","scope":"account","reason":"billing"}}""",
            waiting.New!.ToJsonString());
    }

    /// <summary>
    /// The item an attempt parked carries the block it met, its link included, on its last error, where a person reads
    /// it; the trace, the provider's own detail, stays on the attempt, as it does for a failure.
    /// </summary>
    [Fact]
    public async Task An_item_an_attempt_parked_carries_the_block_and_its_link_on_its_last_error()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var (item, attempt) = await RunningAsync(db, WorkItemFactory.NewCampaign(now: Noon));
        var said = Said("account", link: "https://billing.example.test/pay");
        var error = new AttemptErrorDto(
            AttemptErrors.ProviderBlocked, "The account is out of credit.", false, """{"detail":"the plan has lapsed"}""", null, FailureClass.Blocked, said);

        await NewOutcomes().AnsweredAsync(db, item, attempt, BlockRows.Plugin, Input(null), BlockAnswer.Blocked, said, error, Ct);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(error, attempt.Error);
        Assert.Equal(error with { Trace = null }, item.LastError);
        Assert.Equal("https://billing.example.test/pay", (string?)item.LastError?.Block?["link"]);
    }

    /// <summary>
    /// A sender's block is answered by its own trial: a trial it let through that gets past clears it, whether or not the
    /// trial's input names the sender.
    /// </summary>
    [Fact]
    public async Task A_senders_trial_that_names_no_sender_and_gets_past_clears_that_block()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var outcomes = NewOutcomes();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var opener = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        var block = BlockRows.Block(opener, Noon.AddHours(-1), Noon, ProviderBlockScope.Sender, "snd_marta");
        db.AddRange(opener, block);
        await db.SaveChangesAsync(Ct);
        var (trial, attempt) = await RunningAsync(db, campaign);
        block.TrialWorkItemId = trial.Id;
        await db.SaveChangesAsync(Ct);

        outcomes.Succeed(db, trial, attempt, null, Actors.Dispatcher);
        await outcomes.AnsweredAsync(db, trial, attempt, BlockRows.Plugin, Input(null), BlockAnswer.GotPast, null, null, Ct);
        await db.SaveChangesAsync(Ct);

        var row = await db.ProviderBlocks.AsNoTracking().SingleAsync(Ct);
        Assert.True(
            row is { Status: ProviderBlockStatus.Cleared, TrialWorkItemId: null } && row.ClearedByAttemptId == attempt.Id,
            $"The sender's block is {row.Status}, trial {row.TrialWorkItemId}, cleared by {row.ClearedByAttemptId}.");
    }

    /// <summary>The claim's park: no attempt exists, so none is ended, and the line is keyed by nothing.</summary>
    [Fact]
    public async Task Work_parked_at_the_claim_waits_on_the_block_with_no_attempt()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var opener = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        var block = BlockRows.Block(opener, Noon, Noon.AddMinutes(15));
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        db.AddRange(opener, item, block);
        await db.SaveChangesAsync(Ct);

        NewOutcomes().Park(db, item, attempt: null, block, error: null, Actors.Dispatcher);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(WorkItemStatus.Waiting, item.Status);
        Assert.Equal(0, item.AttemptCount);
        var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == item.Id, Ct);
        Assert.True(wait.AttemptId is null && wait.ProviderBlockId == block.Id, $"The wait holds attempt {wait.AttemptId} and block {wait.ProviderBlockId}.");
        var waiting = await db.Journal.AsNoTracking().SingleAsync(e => e.Kind == JournalKinds.WorkItemWaiting, Ct);
        Assert.Null(waiting.Key);
        Assert.Equal(item.PublicId, waiting.WorkItemId);
    }

    /// <summary>
    /// The first blocked answer opens a block; every later one of the same scope and sender sees it again — counted, with
    /// the latest reason, link and moment — rather than opening a second. The campaign is told once per block, whichever
    /// of its items met it, and another campaign is told once of its own; the line never carries the link.
    /// </summary>
    [Fact]
    public async Task A_block_is_opened_once_seen_again_by_later_answers_and_said_once_per_campaign()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var outcomes = NewOutcomes();
        var first = WorkItemFactory.NewCampaign(now: Noon);
        var second = WorkItemFactory.NewCampaign(now: Noon);

        var (a, attemptA) = await RunningAsync(db, first);
        await outcomes.AnsweredAsync(db, a, attemptA, BlockRows.Plugin, Input(null), BlockAnswer.Blocked, Said("account", link: "https://example.com/billing"), null, Ct);
        await db.SaveChangesAsync(Ct);

        var (b, attemptB) = await RunningAsync(db, first);
        var (c, attemptC) = await RunningAsync(db, second);
        await outcomes.AnsweredAsync(db, b, attemptB, BlockRows.Plugin, Input(null), BlockAnswer.Blocked, Said("account", reason: "limit"), null, Ct);
        await outcomes.AnsweredAsync(db, c, attemptC, BlockRows.Plugin, Input(null), BlockAnswer.Blocked, Said("account", reason: "limit", clearsAt: "2026-09-14T14:00:00Z"), null, Ct);
        await db.SaveChangesAsync(Ct);

        var block = await db.ProviderBlocks.AsNoTracking().SingleAsync(Ct);
        Assert.True(
            block is { SeenCount: 3, Reason: "limit", Link: null } && block.ClearsAt == Noon.AddHours(2) && block.OpenedByAttemptId == attemptA.Id,
            $"The block was seen {block.SeenCount} time(s), reason {block.Reason}, link {block.Link}, clears {block.ClearsAt:O}, opened by {block.OpenedByAttemptId}.");

        var said = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlocked).OrderBy(e => e.Id).ToListAsync(Ct);
        Assert.True(
            said.Count == 2 && said[0].CampaignId == first.Id && said[1].CampaignId == second.Id && said.All(e => e.Key == block.PublicId),
            $"provider_blocked was said {said.Count} time(s): {string.Join(", ", said.Select(e => $"{e.CampaignId}/{e.Key}"))}.");
        Assert.True(said[0].WorkItemId is null, $"The line names work item {said[0].WorkItemId} as its subject; a block is about the account.");
        Assert.Equal(a.PublicId, (string?)said[0].New!["work_item_id"]);
        Assert.DoesNotContain("example.com", string.Join(" ", said.Select(e => e.New!.ToJsonString())), StringComparison.Ordinal);
        Assert.True(new[] { a, b, c }.All(item => item.AttemptCount == 0), "A blocked attempt was counted.");
    }

    /// <summary>
    /// The next trial is due when the provider said the block clears, where that is ahead and within a day; a day from now
    /// where it is further; and the cadence from now where it said nothing or a moment already past.
    /// </summary>
    [Theory]
    [InlineData(2.0, 2.0)]
    [InlineData(30.0, 24.0)]
    [InlineData(-1.0, 0.25)]
    [InlineData(null, 0.25)]
    public void The_next_trial_is_the_providers_moment_capped_at_a_day_or_else_the_cadence(double? clearsInHours, double dueInHours)
    {
        var clearsAt = clearsInHours is { } hours ? Noon.AddHours(hours) : (DateTime?)null;

        var due = ProviderBlocks.NextTrial(clearsAt, Noon, trialSeconds: 900);

        Assert.True(due == Noon.AddHours(dueInHours), $"A block that clears at {clearsAt:O} is next tried at {due:O}, not {Noon.AddHours(dueInHours):O}.");
        Assert.Equal(86_400, ProviderBlocks.ClearsAtCapSeconds);
    }

    /// <summary>
    /// A call that gets past clears the account's block and the block of the sender it names, never another sender's,
    /// and every campaign that was told of a cleared block is told it cleared — none that was not.
    /// </summary>
    [Fact]
    public async Task A_call_that_gets_past_clears_the_account_and_its_own_sender_and_says_so_where_the_block_was_said()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var outcomes = NewOutcomes();
        var told = WorkItemFactory.NewCampaign(now: Noon);
        var untold = WorkItemFactory.NewCampaign(now: Noon);

        var (a, attemptA) = await RunningAsync(db, told);
        var (b, attemptB) = await RunningAsync(db, told);
        await outcomes.AnsweredAsync(db, a, attemptA, BlockRows.Plugin, Input("snd_marta"), BlockAnswer.Blocked, Said("sender", sender: "snd_marta"), null, Ct);
        await outcomes.AnsweredAsync(db, b, attemptB, BlockRows.Plugin, Input("snd_lena"), BlockAnswer.Blocked, Said("sender", sender: "snd_lena"), null, Ct);
        await db.SaveChangesAsync(Ct);

        var (c, attemptC) = await RunningAsync(db, untold);
        outcomes.Succeed(db, c, attemptC, null, Actors.Dispatcher);
        await outcomes.AnsweredAsync(db, c, attemptC, BlockRows.Plugin, Input("snd_marta"), BlockAnswer.GotPast, null, null, Ct);
        await db.SaveChangesAsync(Ct);

        var blocks = await db.ProviderBlocks.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Ct);
        Assert.True(
            blocks[0] is { Status: ProviderBlockStatus.Cleared, SenderExternalId: "snd_marta" } && blocks[0].ClearedByAttemptId == attemptC.Id && blocks[0].ClearedAt == Noon,
            $"Marta's block is {blocks[0].Status}, cleared by {blocks[0].ClearedByAttemptId} at {blocks[0].ClearedAt:O}.");
        Assert.True(blocks[1] is { Status: ProviderBlockStatus.Open, SenderExternalId: "snd_lena" }, $"Lena's block is {blocks[1].Status}.");

        var cleared = await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlockCleared).ToListAsync(Ct);
        Assert.True(
            cleared.Count == 1 && cleared[0].CampaignId == told.Id && cleared[0].Key == blocks[0].PublicId,
            $"provider_block_cleared was said {cleared.Count} time(s): {string.Join(", ", cleared.Select(e => $"{e.CampaignId}/{e.Key}"))}.");
    }

    /// <summary>
    /// A trial of the account's block that meets a sender's has got past the account: the account clears, the item parks
    /// on the sender's block, and that block holds no trial yet.
    /// </summary>
    [Fact]
    public async Task An_account_trial_that_meets_a_senders_block_clears_the_account_and_parks_on_the_sender()
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var outcomes = NewOutcomes();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var (opener, attemptOpener) = await RunningAsync(db, campaign);
        await outcomes.AnsweredAsync(db, opener, attemptOpener, BlockRows.Plugin, Input(null), BlockAnswer.Blocked, Said("account"), null, Ct);
        await db.SaveChangesAsync(Ct);
        var account = await db.ProviderBlocks.SingleAsync(Ct);

        var (trial, attempt) = await RunningAsync(db, campaign);
        account.TrialWorkItemId = trial.Id;
        await db.SaveChangesAsync(Ct);

        await outcomes.AnsweredAsync(db, trial, attempt, BlockRows.Plugin, Input("snd_marta"), BlockAnswer.Blocked, Said("sender", sender: "snd_marta"), null, Ct);
        await db.SaveChangesAsync(Ct);

        var blocks = await db.ProviderBlocks.AsNoTracking().OrderBy(x => x.Id).ToListAsync(Ct);
        Assert.True(
            blocks.Count == 2 && blocks[0] is { Status: ProviderBlockStatus.Cleared, TrialWorkItemId: null } && blocks[1] is { Status: ProviderBlockStatus.Open, Scope: ProviderBlockScope.Sender, TrialWorkItemId: null },
            $"The blocks are {string.Join(", ", blocks.Select(x => $"{x.Scope}/{x.Status}/trial {x.TrialWorkItemId}"))}.");
        var wait = await db.WorkItemWaits.AsNoTracking().SingleAsync(w => w.WorkItemId == trial.Id, Ct);
        Assert.Equal(blocks[1].Id, wait.ProviderBlockId);
        Assert.Single(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlockCleared).ToListAsync(Ct));
    }

    /// <summary>
    /// A trial that blocks again on its own block sees it again and holds no trial; one that says nothing — a transient
    /// failure, an answer outside the contract, no answer — leaves the block open with its next trial a cadence off.
    /// </summary>
    [Theory]
    [InlineData(BlockAnswer.Blocked)]
    [InlineData(BlockAnswer.Undecided)]
    public async Task A_trial_that_does_not_get_past_leaves_the_block_open_and_puts_the_next_trial_off(BlockAnswer answer)
    {
        using var database = new TestDatabase();
        await using var db = database.Open();
        var outcomes = NewOutcomes();
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var opener = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        var block = BlockRows.Block(opener, Noon.AddHours(-1), Noon);
        db.AddRange(opener, block);
        await db.SaveChangesAsync(Ct);
        var (trial, attempt) = await RunningAsync(db, campaign);
        block.TrialWorkItemId = trial.Id;
        await db.SaveChangesAsync(Ct);

        if (answer == BlockAnswer.Undecided)
        {
            outcomes.Fail(db, trial, attempt, "provider_unavailable", "The provider did not answer.", null, null, Actors.Dispatcher, failureClass: FailureClass.Transient, retriable: true);
        }

        await outcomes.AnsweredAsync(db, trial, attempt, BlockRows.Plugin, Input(null), answer, answer == BlockAnswer.Blocked ? Said("account", reason: "limit") : null, null, Ct);
        await db.SaveChangesAsync(Ct);

        var row = await db.ProviderBlocks.AsNoTracking().SingleAsync(Ct);
        Assert.True(
            row is { Status: ProviderBlockStatus.Open, TrialWorkItemId: null } && row.NextTrialAt == Noon.AddSeconds(900),
            $"After a trial that answered {answer}, the block is {row.Status}, trial {row.TrialWorkItemId}, next at {row.NextTrialAt:O}.");
        Assert.Equal(answer == BlockAnswer.Blocked ? 2 : 1, row.SeenCount);
        Assert.Equal(answer == BlockAnswer.Blocked ? "limit" : "billing", row.Reason);
        Assert.Empty(await db.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlockCleared).ToListAsync(Ct));
    }

    /// <summary>
    /// Which open block holds work: the account's covers every sender; a sender's holds only work that names that sender,
    /// so work that names none is held by no sender's block.
    /// </summary>
    [Fact]
    public void The_accounts_block_covers_every_sender_and_a_senders_block_only_its_own()
    {
        var campaign = WorkItemFactory.NewCampaign(now: Noon);
        var opener = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        var marta = BlockRows.Block(opener, Noon, Noon, ProviderBlockScope.Sender, "snd_marta");
        var account = BlockRows.Block(opener, Noon, Noon);

        Assert.Same(marta, ProviderBlocks.Covering([marta], Input("snd_marta")));
        Assert.Null(ProviderBlocks.Covering([marta], Input("snd_lena")));
        Assert.Null(ProviderBlocks.Covering([marta], Input(null)));
        Assert.Same(account, ProviderBlocks.Covering([marta, account], Input("snd_marta")));
        Assert.Same(account, ProviderBlocks.Covering([account], Input(null)));
        Assert.Equal("snd_marta", ProviderBlocks.Sender(Input("snd_marta")));
        Assert.Null(ProviderBlocks.Sender(new JsonObject { ["args"] = new JsonObject { ["sender"] = "snd_marta" } }));
    }

    private static AttemptOutcomes NewOutcomes()
    {
        var clock = new FixedClock(Noon);
        var options = TestOptions.Dispatcher(o => o.Blocks.TrialSeconds = 900);
        return new AttemptOutcomes(new JournalWriter(clock), clock, TestOptions.Settings(options));
    }

    /// <summary>A provider item of <c>message.send</c> in <paramref name="campaign"/>, processing, with a running attempt under the made-up binding.</summary>
    private static async Task<(WorkItem Item, Attempt Attempt)> RunningAsync(JasonDbContext db, Campaign campaign)
    {
        var item = WorkItemFactory.NewProviderOp(campaign, "message.send", Noon);
        item.Status = WorkItemStatus.Processing;
        var attempt = WorkItemFactory.NewAttempt(item, 1, AttemptStatus.Running, Noon);
        attempt.Provenance = new AttemptProvenanceDto(BlockRows.Plugin, null, null, null, null, "message.send", null, null, null, null, BlockRows.Binding);
        db.WorkItems.Add(item);
        await db.SaveChangesAsync(Ct);
        return (item, attempt);
    }

    private static JsonObject Input(string? sender)
    {
        var args = new JsonObject { ["body"] = "Hello Marta" };
        if (sender is not null)
        {
            args["sender"] = new JsonObject { ["external_id"] = sender };
        }

        return new JsonObject { ["args"] = args };
    }

    private static JsonObject Said(string scope, string reason = "billing", string? sender = null, string? link = null, string? clearsAt = null)
    {
        var said = new JsonObject { ["scope"] = scope, ["reason"] = reason };
        if (sender is not null)
        {
            said["sender_external_id"] = sender;
        }

        if (link is not null)
        {
            said["link"] = link;
        }

        if (clearsAt is not null)
        {
            said["clears_at"] = clearsAt;
        }

        return said;
    }
}
