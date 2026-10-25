using System.Data.Common;
using System.Text.RegularExpressions;
using Jason.Contracts.Api;
using Jason.Runtime.Blocks;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Dispatch;
using Jason.Runtime.Tests.Plugins;
using Jason.Runtime.WorkItems;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Blocks;

/// <summary>
/// The three operations a person sees the provider's blocks through and ends one with: what is blocked now, one block
/// with the link where it is cleared, and a clear on a person's word — said in every campaign that was told of the block,
/// and released by the next scan rather than in place.
/// </summary>
public partial class BlockServiceTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ActorRef Marta = new(ActorType.Human, "marta");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// What a person asks first is what is blocked now: open blocks only, newest first. The cleared ones are the history,
    /// listed when asked for, alone or beside the open ones in the same order.
    /// </summary>
    [Fact]
    public async Task A_listing_answers_the_open_blocks_newest_first_unless_asked_for_the_cleared_ones()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var older = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-2));
        var newer = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-1), scope: ProviderBlockScope.Sender, sender: "snd_marta");
        var cleared = await BlockAsync(harness, campaign, openedAt: Noon.AddMinutes(-30), scope: ProviderBlockScope.Sender, sender: "snd_ada", cleared: true);

        await using var db = harness.Open();
        var service = NewService(db, Noon);
        Assert.Equal([newer, older], Ids(await service.ListAsync(new BlockListRequest(null, null, null, null), Ct)));
        Assert.Equal([cleared], Ids(await service.ListAsync(new BlockListRequest([ProviderBlockStatus.Cleared], null, null, null), Ct)));
        Assert.Equal(
            [cleared, newer, older],
            Ids(await service.ListAsync(new BlockListRequest([ProviderBlockStatus.Open, ProviderBlockStatus.Cleared], null, null, null), Ct)));
    }

    [Fact]
    public async Task A_listing_narrows_to_one_plugin()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var fake = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-2));
        await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-1), plugin: "other-provider");

        await using var db = harness.Open();
        var page = await NewService(db, Noon).ListAsync(new BlockListRequest(null, BlockRows.Plugin, null, null), Ct);

        Assert.Equal([fake], Ids(page));
    }

    /// <summary>A page is cut by its cursor in the listing's own order: the opening moment, then the id, both newest first.</summary>
    [Fact]
    public async Task A_listing_pages_by_its_cursor_through_blocks_opened_at_one_moment()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var first = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-1));
        var second = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-1), binding: "sha256:" + new string('b', 64));
        var third = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-3), binding: "sha256:" + new string('c', 64));
        string[] expected = [.. new[] { first, second }.Order(StringComparer.Ordinal).Reverse(), third];

        await using var db = harness.Open();
        var service = NewService(db, Noon);
        var one = await service.ListAsync(new BlockListRequest(null, null, 2, null), Ct);
        Assert.True(one.NextCursor is not null, "The first page of two out of three gave no cursor.");
        var two = await service.ListAsync(new BlockListRequest(null, null, 2, one.NextCursor), Ct);

        string[] actual = [.. Ids(one), .. Ids(two)];
        Assert.Equal(expected, actual);
        Assert.True(two.NextCursor is null, $"The last page still points on, at '{two.NextCursor}'.");
    }

    /// <summary>
    /// A block's held count is the open waits parked on that block and no other: a wait already released is not held, and
    /// an account block does not count what its senders' blocks hold.
    /// </summary>
    [Fact]
    public async Task Held_count_is_the_open_waits_on_the_block_itself()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var account = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-2), parked: 2, released: 1);
        var sender = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-1), scope: ProviderBlockScope.Sender, sender: "snd_marta", parked: 1);

        await using var db = harness.Open();
        var service = NewService(db, Noon);
        var listed = (await service.ListAsync(new BlockListRequest(null, null, null, null), Ct)).Items.ToDictionary(b => b.Id, b => b.HeldCount);
        var got = await service.GetAsync(new BlockGetRequest(account), Ct);

        Assert.True(listed[account] == 2 && listed[sender] == 1, $"The listing holds {string.Join(", ", listed.Select(p => $"{p.Key}={p.Value}"))}.");
        Assert.Equal(2, got.HeldCount);
    }

    /// <summary>One block, as the UI reads it: what holds, why, the link where a person clears it, and the attempt that met it.</summary>
    [Fact]
    public async Task Get_answers_the_block_with_its_link_and_the_attempt_that_opened_it()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var id = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-1), scope: ProviderBlockScope.Sender, sender: "snd_marta", parked: 1);
        string opener;
        await using (var db = harness.Open())
        {
            opener = (await db.ProviderBlocks.AsNoTracking().Include(b => b.OpenedByAttempt).SingleAsync(b => b.PublicId == id, Ct)).OpenedByAttempt!.PublicId;
        }

        await using var read = harness.Open();
        var block = await NewService(read, Noon).GetAsync(new BlockGetRequest(id), Ct);

        Assert.Equal((id, BlockRows.Plugin, BlockRows.Binding), (block.Id, block.PluginId, block.BindingIdentity));
        Assert.Equal((ProviderBlockScope.Sender, "snd_marta", "billing"), (block.Scope, block.SenderExternalId, block.Reason));
        Assert.Equal("https://provider.example.com/billing", block.Link);
        Assert.Equal((ProviderBlockStatus.Open, opener, 1), (block.Status, block.OpenedByAttemptId, block.SeenCount));
        Assert.Equal(WorkItemMapper.Utc(Noon.AddHours(-1)), block.OpenedAt);
        Assert.True(
            block is { ClearedAt: null, ClearedByAttemptId: null, ClearedByActor: null, TrialWorkItemId: null },
            $"An open block reads cleared at {block.ClearedAt} by {block.ClearedByAttemptId}/{block.ClearedByActor}, trial {block.TrialWorkItemId}.");
    }

    [Fact]
    public async Task An_unknown_block_is_not_found_to_a_read_and_to_a_clear()
    {
        using var harness = new DispatchHarness(Noon);
        await using var db = harness.Open();
        var service = NewService(db, Noon);

        var read = await Assert.ThrowsAsync<NotFoundException>(() => service.GetAsync(new BlockGetRequest("blk_01J00000000000000000000000"), Ct));
        var clear = await Assert.ThrowsAsync<NotFoundException>(() => service.ClearAsync(new BlockClearRequest("blk_01J00000000000000000000000", Marta, null), Ct));

        Assert.Equal(("block_not_found", "block_not_found"), (read.Code, clear.Code));
    }

    /// <summary>
    /// A block is cleared on a person's word, and the line says whose: a clear naming nobody — no actor, or a person with
    /// no id — is refused as a decision naming nobody is, and changes nothing.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_clear_names_the_person_who_asks_for_it(bool unnamed)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var id = await BlockAsync(harness, campaign, openedAt: Noon, parked: 1);

        await using var db = harness.Open();
        var actor = unnamed ? new ActorRef(ActorType.Human, "  ") : null;
        var error = await Assert.ThrowsAsync<InvalidRequestException>(() => NewService(db, Noon).ClearAsync(new BlockClearRequest(id, actor, "paid"), Ct));

        Assert.Equal("actor_required", error.Code);
        await using var check = harness.Open();
        Assert.Equal(ProviderBlockStatus.Open, (await check.ProviderBlocks.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Empty(await check.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlockCleared).ToListAsync(Ct));
    }

    /// <summary>
    /// A clear is a person's word that the cause is gone: a role or an attempt asking to clear a block is asking to be that
    /// person, and is refused before the block is read; only an attempt that gets past the block clears it for the runtime.
    /// </summary>
    [Theory]
    [InlineData(ActorType.Role, "planner")]
    [InlineData(ActorType.Attempt, "att_01K52JR0000000000000000001")]
    public async Task Only_a_person_clears_a_block(ActorType type, string id)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var block = await BlockAsync(harness, campaign, openedAt: Noon, parked: 1);

        await using var db = harness.Open();
        var error = await Assert.ThrowsAsync<InvalidRequestException>(() => NewService(db, Noon).ClearAsync(new BlockClearRequest(block, new ActorRef(type, id), "paid"), Ct));

        Assert.Equal("block_not_human", error.Code);
        await using var check = harness.Open();
        Assert.Equal(ProviderBlockStatus.Open, (await check.ProviderBlocks.AsNoTracking().SingleAsync(Ct)).Status);
        Assert.Empty(await check.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlockCleared).ToListAsync(Ct));
    }

    /// <summary>
    /// A person's clear: the block is cleared now, by them and by no attempt, and every campaign that was told the block
    /// held its work is told once that it cleared, under their name and with their reason — a campaign never told is not.
    /// </summary>
    [Fact]
    public async Task A_person_clears_an_open_block_and_each_told_campaign_is_told_once_under_their_name()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await CampaignAsync(harness, "First");
        var second = await CampaignAsync(harness, "Second");
        var untold = await CampaignAsync(harness, "Untold");
        var id = await BlockAsync(harness, first, openedAt: Noon, parked: 1, alsoParkedIn: second);

        await using (var db = harness.Open())
        {
            var cleared = await NewService(db, Noon.AddHours(1)).ClearAsync(new BlockClearRequest(id, Marta, "  the invoice is paid  "), Ct);
            Assert.True(
                cleared is { Status: ProviderBlockStatus.Cleared, ClearedByAttemptId: null, TrialWorkItemId: null } && cleared.ClearedByActor == Marta,
                $"The clear answered {cleared.Status}, by attempt {cleared.ClearedByAttemptId} and {cleared.ClearedByActor}, trial {cleared.TrialWorkItemId}.");
            Assert.Equal(WorkItemMapper.Utc(Noon.AddHours(1)), cleared.ClearedAt);
        }

        await using var check = harness.Open();
        var row = await check.ProviderBlocks.AsNoTracking().SingleAsync(Ct);
        Assert.True(
            row is { Status: ProviderBlockStatus.Cleared, ClearedByAttemptId: null, ClearedByType: ActorType.Human, ClearedById: "marta" },
            $"The block is {row.Status}, cleared by attempt {row.ClearedByAttemptId} and {row.ClearedByType}:{row.ClearedById}.");
        var lines = await check.Journal.AsNoTracking().Include(e => e.Campaign).Where(e => e.Kind == JournalKinds.ProviderBlockCleared).ToListAsync(Ct);
        Assert.True(
            lines.Select(e => e.Campaign!.PublicId).Order(StringComparer.Ordinal).SequenceEqual(new[] { first, second }.Order(StringComparer.Ordinal)),
            $"provider_block_cleared was said in {string.Join(", ", lines.Select(e => e.Campaign!.PublicId))}, not exactly {first} and {second}; {untold} was never told.");
        Assert.All(lines, line =>
        {
            Assert.Equal((ActorType.Human, "marta", (string?)null, "the invoice is paid", id), (line.ActorType, line.ActorId, line.AttemptId, line.Reason, line.Key));
            Assert.Equal($$"""{"provider_block_id":"{{id}}"}""", line.New!.ToJsonString());
        });
    }

    [Fact]
    public async Task A_cleared_block_is_not_cleared_again()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var id = await BlockAsync(harness, campaign, openedAt: Noon, parked: 1);
        await using (var db = harness.Open())
        {
            await NewService(db, Noon).ClearAsync(new BlockClearRequest(id, Marta, null), Ct);
        }

        await using var again = harness.Open();
        var error = await Assert.ThrowsAsync<ConflictException>(() => NewService(again, Noon.AddHours(1)).ClearAsync(new BlockClearRequest(id, Marta, null), Ct));

        Assert.Equal("block_not_open", error.Code);
        await using var check = harness.Open();
        Assert.Single(await check.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlockCleared).ToListAsync(Ct));
        Assert.Equal(WorkItemMapper.Utc(Noon), (await NewService(check, Noon).GetAsync(new BlockGetRequest(id), Ct)).ClearedAt);
    }

    /// <summary>
    /// A clear releases nothing in place: the items it held are still waiting when it answers, and the next scan's resume
    /// step releases every wait on it as <c>block_cleared</c>, as it does for a block an attempt got past.
    /// </summary>
    [Fact]
    public async Task A_clear_releases_nothing_and_the_next_resume_step_releases_every_wait_as_block_cleared()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var id = await BlockAsync(harness, campaign, openedAt: Noon, parked: 2);
        await using (var db = harness.Open())
        {
            await NewService(db, Noon).ClearAsync(new BlockClearRequest(id, Marta, null), Ct);
        }

        await using (var db = harness.Open())
        {
            var statuses = await db.WorkItems.AsNoTracking().Where(w => w.Kind == WorkItemKind.ProviderOp && w.Status != WorkItemStatus.Failed).Select(w => w.Status).ToListAsync(Ct);
            Assert.True(statuses.Count == 2 && statuses.All(s => s == WorkItemStatus.Waiting), $"Right after the clear the held items are {string.Join(", ", statuses)}.");
        }

        Assert.Equal(2, await harness.ResumeAsync(Ct));

        await using var check = harness.Open();
        var waits = await check.WorkItemWaits.AsNoTracking().Where(w => w.ProviderBlock!.PublicId == id).ToListAsync(Ct);
        Assert.True(
            waits.Count == 2 && waits.All(w => w is { Status: WaitStatus.Released, ReleaseReason: WaitReleaseReason.BlockCleared }),
            $"The waits closed as {string.Join(", ", waits.Select(w => $"{w.Status}/{w.ReleaseReason}"))}.");
    }

    /// <summary>
    /// The clear summons no manager: <c>provider_block_cleared</c> is not a trigger, whoever writes it, and the manager was
    /// told when the block began.
    /// </summary>
    [Fact]
    public async Task A_clear_summons_no_manager()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness, live: true);
        var id = await BlockAsync(harness, campaign, openedAt: Noon, parked: 1);
        await ConsumeAsync(harness);

        await using (var db = harness.Open())
        {
            await NewService(db, Noon).ClearAsync(new BlockClearRequest(id, Marta, null), Ct);
        }

        Assert.Equal(0, await harness.SummonAsync(Ct));
        await using var check = harness.Open();
        Assert.Empty(await check.WorkItems.AsNoTracking().Where(w => w.Role == ManagerCheckIn.Role).ToListAsync(Ct));
    }

    /// <summary>
    /// A person's clear of a block a call has just got past is refused, and the attempt's clear stands: the block's status
    /// is read inside the clear's own transaction, so a clear that committed first is what it reads.
    /// </summary>
    [Fact]
    public async Task A_clear_after_an_attempt_s_clear_is_refused_and_the_attempt_s_stands()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var id = await BlockAsync(harness, campaign, openedAt: Noon, parked: 1);
        string winner;
        await using (var other = harness.Open())
        {
            var (row, attempt) = await GettingPastAsync(other, id);
            ProviderBlocks.Clear(row, attempt, Noon);
            await other.SaveChangesAsync(Ct);
            winner = attempt.PublicId;
        }

        using var scope = harness.Scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
        var error = await Assert.ThrowsAsync<ConflictException>(() => NewService(db, Noon.AddMinutes(5)).ClearAsync(new BlockClearRequest(id, Marta, null), Ct));

        Assert.Equal("block_not_open", error.Code);
        await AttemptsClearStandsAsync(harness, winner);
    }

    /// <summary>
    /// The other order: a call that read the block open before a person's clear, and reaches its own write after the clear
    /// committed, writes nothing — the block's status is the token both writes carry — and the person's clear stands.
    /// </summary>
    [Fact]
    public async Task An_attempt_s_clear_in_flight_meets_a_person_s_clear_and_the_person_s_stands()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var id = await BlockAsync(harness, campaign, openedAt: Noon, parked: 1);
        await using var other = harness.Open();
        var (row, attempt) = await GettingPastAsync(other, id);

        await using (var db = harness.Open())
        {
            await NewService(db, Noon.AddMinutes(5)).ClearAsync(new BlockClearRequest(id, Marta, "paid"), Ct);
        }

        ProviderBlocks.Clear(row, attempt, Noon.AddMinutes(6));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => other.SaveChangesAsync(Ct));

        await using var check = harness.Open();
        var stands = await check.ProviderBlocks.AsNoTracking().SingleAsync(Ct);
        Assert.True(
            stands is { Status: ProviderBlockStatus.Cleared, ClearedByAttemptId: null, ClearedByType: ActorType.Human, ClearedById: "marta" },
            $"The block was cleared by attempt {stands.ClearedByAttemptId} and {stands.ClearedByType}:{stands.ClearedById}, not by marta alone.");
    }

    /// <summary>
    /// From the block's read to its commit a person's clear is the only writer: a campaign whose work is parked on the block
    /// in between would be told the block holds it and never told it cleared, so that park waits for the clear — here, a
    /// writer that does not wait is refused outright — and every campaign told of the block is told it cleared.
    /// </summary>
    [Fact]
    public async Task A_park_between_a_clear_s_read_and_its_write_waits_and_every_told_campaign_is_told_it_cleared()
    {
        using var harness = new DispatchHarness(Noon);
        var first = await CampaignAsync(harness, "First");
        var second = await CampaignAsync(harness, "Second");
        var late = await CampaignAsync(harness, "Late");
        var id = await BlockAsync(harness, first, openedAt: Noon, parked: 1, alsoParkedIn: second);
        Exception? refused = null;
        harness.InterfereOnceBeforeSaving(
            context => context.ChangeTracker.Entries<ProviderBlock>().Any(entry => entry.State == EntityState.Modified),
            () =>
            {
                using var other = Impatient(harness);
                var block = other.ProviderBlocks.Single(b => b.PublicId == id);
                var item = BlockRows.Parked(other, other.Campaigns.Single(c => c.PublicId == late), block, Noon);
                refused = Record.Exception(() =>
                {
                    ProviderBlocks.SayBlockedAsync(other, new JournalWriter(harness.Clock), item, block, attempt: null, CancellationToken.None).GetAwaiter().GetResult();
                    other.SaveChanges();
                });
            });

        using (var scope = harness.Scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            await NewService(db, Noon.AddHours(1)).ClearAsync(new BlockClearRequest(id, Marta, "paid"), Ct);
        }

        Assert.True(Busy(refused), $"The park in between was not held off by the clear's lock: {refused?.GetType().Name ?? "it was written"}.");
        await using var check = harness.Open();
        var told = await CampaignsSaidAsync(check, JournalKinds.ProviderBlocked);
        var cleared = await CampaignsSaidAsync(check, JournalKinds.ProviderBlockCleared);
        string[] expected = [.. new[] { first, second }.Order(StringComparer.Ordinal)];
        Assert.Equal(expected, cleared);
        Assert.Equal(told, cleared);
    }

    /// <summary>
    /// Nor does a trial get set on the block between the clear's read and its write: the clear leaves the block with no
    /// trial, and a resume step that would pick one meanwhile waits for the clear — here, refused outright.
    /// </summary>
    [Fact]
    public async Task A_trial_set_between_a_clear_s_read_and_its_write_waits_and_the_cleared_block_has_none()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var id = await BlockAsync(harness, campaign, openedAt: Noon, parked: 1);
        Exception? refused = null;
        harness.InterfereOnceBeforeSaving(
            context => context.ChangeTracker.Entries<ProviderBlock>().Any(entry => entry.State == EntityState.Modified),
            () =>
            {
                using var other = Impatient(harness);
                var block = other.ProviderBlocks.Single(b => b.PublicId == id);
                block.TrialWorkItemId = other.WorkItemWaits.Where(w => w.ProviderBlockId == block.Id).Select(w => w.WorkItemId).First();
                refused = Record.Exception(() => other.SaveChanges());
            });

        using (var scope = harness.Scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<JasonDbContext>();
            await NewService(db, Noon.AddHours(1)).ClearAsync(new BlockClearRequest(id, Marta, "paid"), Ct);
        }

        Assert.True(Busy(refused), $"The trial in between was not held off by the clear's lock: {refused?.GetType().Name ?? "it was written"}.");
        await using var check = harness.Open();
        var row = await check.ProviderBlocks.AsNoTracking().SingleAsync(Ct);
        Assert.True(row is { Status: ProviderBlockStatus.Cleared, TrialWorkItemId: null }, $"The block is {row.Status} with trial {row.TrialWorkItemId}.");
    }

    /// <summary>
    /// A person who clears an account's block while a sender's block under it stays open: the sender's block is tried on
    /// its own from then on, in the very next resume step, while the account's waits are released as cleared.
    /// </summary>
    [Fact]
    public async Task An_account_block_cleared_with_a_sender_block_still_open_lets_the_sender_block_be_tried_on_its_own()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var account = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-2), nextTrialAt: Noon.AddHours(1), parked: 1);
        var sender = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-1), scope: ProviderBlockScope.Sender, sender: "snd_marta", nextTrialAt: Noon, parked: 1);

        // While the account's block is open its sender's block holds no trial of its own, due or not.
        Assert.Equal(0, await harness.ResumeAsync(Ct));

        await using (var db = harness.Open())
        {
            await NewService(db, Noon).ClearAsync(new BlockClearRequest(account, Marta, null), Ct);
        }

        await harness.ResumeAsync(Ct);

        await using var check = harness.Open();
        var reasons = await check.WorkItemWaits.AsNoTracking().Include(w => w.ProviderBlock)
            .Select(w => new { Block = w.ProviderBlock!.PublicId, w.Status, w.ReleaseReason })
            .ToListAsync(Ct);
        Assert.True(
            reasons.Single(r => r.Block == account) is { Status: WaitStatus.Released, ReleaseReason: WaitReleaseReason.BlockCleared }
                && reasons.Single(r => r.Block == sender) is { Status: WaitStatus.Released, ReleaseReason: WaitReleaseReason.BlockTrial },
            $"The waits closed as {string.Join(", ", reasons.Select(r => $"{r.Block}:{r.Status}/{r.ReleaseReason}"))}.");
        Assert.Equal(ProviderBlockStatus.Open, (await check.ProviderBlocks.AsNoTracking().SingleAsync(b => b.PublicId == sender, Ct)).Status);
    }

    /// <summary>
    /// A clear is a person's statement that the cause is gone, not a mute: where the provider still enforces it, the next
    /// held call answers <c>blocked</c>, a new block opens under a new id beside the cleared one, the campaign is told again
    /// under the new key, and the manager is summoned again.
    /// </summary>
    [Fact]
    public async Task After_a_person_s_clear_a_blocked_answer_opens_a_new_block_and_the_campaign_is_told_again()
    {
        var command = new FakeCommand(WorkItemKind.ProviderOp, _ => Task.FromResult<CommandOutcome>(new CommandOutcome.Provider(ProviderBlockRaceTests.Blocked(), null)));
        using var harness = new DispatchHarness(Noon, commands: command);
        var (work, _) = await ProviderBlockRaceTests.SeedTwoClaimedAsync(harness, oneCampaign: true);
        string old;
        await using (var db = harness.Open())
        {
            var campaign = await db.Campaigns.SingleAsync(Ct);
            campaign.ManagerReviewAnchor = Noon;
            var opener = WorkItemFactory.NewProviderOp(campaign, "campaign.pause", Noon);
            opener.Status = WorkItemStatus.Failed;
            var row = BlockRows.Block(opener, Noon, Noon.AddHours(1));
            row.PluginId = TestPlugins.FakeProviderId;
            row.BindingIdentity = ProviderBlocks.Identity(null);
            db.AddRange(opener, row);
            var item = await db.WorkItems.Include(w => w.Campaign).SingleAsync(w => w.Id == work.WorkItemId, Ct);
            await ProviderBlocks.SayBlockedAsync(db, new JournalWriter(harness.Clock), item, row, attempt: null, Ct);
            await db.SaveChangesAsync(Ct);
            old = row.PublicId;
        }

        await using (var db = harness.Open())
        {
            await NewService(db, Noon).ClearAsync(new BlockClearRequest(old, Marta, "the invoice is paid"), Ct);
        }

        await ConsumeAsync(harness);
        await ProviderBlockRaceTests.RunAsync(harness, work);

        await using var check = harness.Open();
        var blocks = await check.ProviderBlocks.AsNoTracking().OrderBy(b => b.Id).ToListAsync(Ct);
        Assert.True(
            blocks.Count == 2 && blocks[0] is { Status: ProviderBlockStatus.Cleared, ClearedById: "marta" } && blocks[1].Status == ProviderBlockStatus.Open
                && blocks[1].PublicId != old && blocks[1].OpenedByAttemptId == work.AttemptId,
            $"The blocks are {string.Join(", ", blocks.Select(b => $"{b.PublicId}:{b.Status}/{b.ClearedById}/opened by {b.OpenedByAttemptId}"))}.");
        var told = await check.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlocked).Select(e => e.Key).ToListAsync(Ct);
        Assert.Equal([old, blocks[1].PublicId], told);
        Assert.Equal(1, await harness.SummonAsync(Ct));
    }

    /// <summary>
    /// The items a block holds, listed and counted by the block: exactly those whose open wait names it — not an item it
    /// held and has released, not one parked on another block, not a role's wait.
    /// </summary>
    [Fact]
    public async Task Work_items_are_listed_and_counted_by_the_block_they_are_parked_on()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var id = await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-2), parked: 2, released: 1);
        await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-1), scope: ProviderBlockScope.Sender, sender: "snd_marta", parked: 1);
        string[] parked;
        await using (var db = harness.Open())
        {
            parked = [.. await db.WorkItemWaits.AsNoTracking()
                .Where(w => w.ProviderBlock!.PublicId == id && w.Status == WaitStatus.Open)
                .Select(w => w.WorkItem!.PublicId)
                .OrderBy(p => p)
                .ToListAsync(Ct)];
        }

        await using var read = harness.Open();
        var service = NewWorkItems(read, Noon);
        var listed = await service.ListAsync(new WorkItemListRequest(null, null, null, null, null, null, null, null, ProviderBlockId: id), Ct);
        var counted = await service.CountAsync(new WorkItemCountRequest(null, null, null, null, null, null, null, null, null, ProviderBlockId: id), Ct);
        var unknown = await Assert.ThrowsAsync<NotFoundException>(
            () => service.ListAsync(new WorkItemListRequest(null, null, null, null, null, null, null, null, ProviderBlockId: "blk_01J00000000000000000000000"), Ct));

        Assert.Equal(parked, listed.Items.Select(w => w.Id));
        Assert.Equal(2, counted.Total);
        Assert.Equal("block_not_found", unknown.Code);
    }

    /// <summary>
    /// The listing by block reads the waits through the index of the waits by block and status, and each item it finds
    /// by its key: never a walk of the waits nor of the items. The held count a block listing carries reads the same index.
    /// </summary>
    [Fact]
    public async Task The_reads_by_block_go_through_the_index_of_the_waits_by_block_and_status()
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        var id = await BlockAsync(harness, campaign, openedAt: Noon, parked: 3);
        var recorder = new Recorder();
        var builder = new DbContextOptionsBuilder<JasonDbContext>();
        JasonDbContext.Configure(builder, harness.DatabaseFile).AddInterceptors(recorder);
        await using (var db = new JasonDbContext(builder.Options))
        {
            await NewWorkItems(db, Noon).ListAsync(new WorkItemListRequest(null, null, null, null, null, null, null, null, ProviderBlockId: id), Ct);
            await NewService(db, Noon).ListAsync(new BlockListRequest(null, null, null, null), Ct);
        }

        // A read that filters by block says so in a WHERE; the waits a listed item carries join their block in an ON instead.
        var reads = recorder.Commands.Where(c => FiltersByBlock().IsMatch(c.Text) && c.Text.Contains("FROM \"work_item_waits\"", StringComparison.Ordinal)).ToList();
        Assert.True(reads.Count == 2, $"Expected the item listing and the block listing, read {reads.Count}:{Environment.NewLine}{string.Join(Environment.NewLine, reads.Select(r => r.Text))}");
        foreach (var read in reads)
        {
            var plan = PlanOf(harness, read);
            var described = Described(plan, read);
            // SQLite says USING INDEX, or USING COVERING INDEX where the index alone answers.
            Assert.True(
                plan.Any(step => step.Contains("INDEX ix_work_item_waits_provider_block_id_status (provider_block_id=? AND status=?)", StringComparison.Ordinal)),
                $"The waits are not read through their block index:{Environment.NewLine}{described}");
            Assert.True(
                !plan.Any(step => step.StartsWith("SCAN ", StringComparison.Ordinal) && step.Contains("work_item_waits", StringComparison.Ordinal)),
                $"The read walks the waits:{Environment.NewLine}{described}");
        }

        // The items the waits name are each read by their key: no walk of the items, by the table or by any index of it.
        var items = Assert.Single(reads, read => read.Text.Contains("FROM \"work_items\" AS \"w\"", StringComparison.Ordinal));
        var itemPlan = PlanOf(harness, items);
        Assert.True(
            itemPlan.Contains("SEARCH w USING INTEGER PRIMARY KEY (rowid=?)") && !Scans(itemPlan, "w"),
            $"The items are not read by their key:{Environment.NewLine}{Described(itemPlan, items)}");
    }

    /// <summary>
    /// A block listing is bounded by the blocks of the statuses asked for: it reads them through the index that leads with
    /// the status and sorts what that finds, never walking every block the installation has had.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_block_listing_reads_the_blocks_of_the_asked_statuses_through_their_index(bool history)
    {
        using var harness = new DispatchHarness(Noon);
        var campaign = await CampaignAsync(harness);
        await BlockAsync(harness, campaign, openedAt: Noon, parked: 1);
        await BlockAsync(harness, campaign, openedAt: Noon.AddHours(-1), cleared: true);
        var recorder = new Recorder();
        var builder = new DbContextOptionsBuilder<JasonDbContext>();
        JasonDbContext.Configure(builder, harness.DatabaseFile).AddInterceptors(recorder);
        await using (var db = new JasonDbContext(builder.Options))
        {
            var request = history
                ? new BlockListRequest([ProviderBlockStatus.Open, ProviderBlockStatus.Cleared], BlockRows.Plugin, null, null)
                : new BlockListRequest(null, null, null, null);
            await NewService(db, Noon).ListAsync(request, Ct);
        }

        var read = Assert.Single(recorder.Commands, c => c.Text.Contains("FROM \"provider_blocks\" AS \"p\"", StringComparison.Ordinal));
        var plan = PlanOf(harness, read);
        Assert.True(
            plan.Contains("SEARCH p USING INDEX ix_provider_blocks_status_next_trial_at (status=?)") && plan.Contains("USE TEMP B-TREE FOR ORDER BY") && !Scans(plan, "p"),
            $"The listing does not read the blocks of its statuses through their index:{Environment.NewLine}{Described(plan, read)}");
    }

    /// <summary>Whether a step of the plan walks <paramref name="alias"/> itself; a co-routine over it has a name of its own.</summary>
    private static bool Scans(List<string> plan, string alias) =>
        plan.Any(step => step == $"SCAN {alias}" || step.StartsWith($"SCAN {alias} ", StringComparison.Ordinal));

    private static string Described(List<string> plan, Command read) => $"{string.Join(Environment.NewLine, plan)}{Environment.NewLine}{read.Text}";

    private static BlockService NewService(JasonDbContext db, DateTime now)
    {
        var clock = new FixedClock(now);
        return new BlockService(db, new JournalWriter(clock), clock);
    }

    private static WorkItemService NewWorkItems(JasonDbContext db, DateTime now)
    {
        var clock = new FixedClock(now);
        return new WorkItemService(db, new JournalWriter(clock), clock, TestCanceller.New(clock), TestOptions.PluginSettings());
    }

    private static IEnumerable<string> Ids(Page<ProviderBlockDto> page) => page.Items.Select(b => b.Id);

    [GeneratedRegex(@"WHERE ""\w+""\.""provider_block_id"" = ")]
    private static partial Regex FiltersByBlock();

    /// <summary>One active campaign; a live one has a review anchor, so a trigger summons its manager.</summary>
    private static async Task<string> CampaignAsync(DispatchHarness harness, string name = "Work", bool live = false)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(name, now: Noon);
        if (live)
        {
            campaign.ManagerReviewAnchor = Noon;
        }

        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(Ct);
        return campaign.PublicId;
    }

    /// <summary>
    /// A block of <paramref name="plugin"/> opened at <paramref name="openedAt"/> by an attempt in <paramref name="campaign"/>,
    /// with a link, holding <paramref name="parked"/> items there and having released <paramref name="released"/>; each
    /// campaign an item parks in is told once, as the runtime tells it.
    /// </summary>
    private static async Task<string> BlockAsync(
        DispatchHarness harness,
        string campaign,
        DateTime openedAt,
        ProviderBlockScope scope = ProviderBlockScope.Account,
        string? sender = null,
        string plugin = BlockRows.Plugin,
        string binding = BlockRows.Binding,
        DateTime? nextTrialAt = null,
        int parked = 0,
        int released = 0,
        string? alsoParkedIn = null,
        bool cleared = false)
    {
        await using var db = harness.Open();
        var home = await db.Campaigns.SingleAsync(c => c.PublicId == campaign, Ct);
        var opener = WorkItemFactory.NewProviderOp(home, "message.send", openedAt);
        opener.Status = WorkItemStatus.Failed;
        db.WorkItems.Add(opener);
        var block = BlockRows.Block(opener, openedAt, nextTrialAt ?? openedAt.AddHours(1), scope, sender, binding);
        block.PluginId = plugin;
        block.Link = "https://provider.example.com/billing";
        if (cleared)
        {
            block.Status = ProviderBlockStatus.Cleared;
            block.ClearedAt = openedAt;
        }

        db.ProviderBlocks.Add(block);
        var items = new List<WorkItem>();
        for (var n = 0; n < parked; n++)
        {
            items.Add(BlockRows.Parked(db, home, block, openedAt));
        }

        if (alsoParkedIn is not null)
        {
            items.Add(BlockRows.Parked(db, await db.Campaigns.SingleAsync(c => c.PublicId == alsoParkedIn, Ct), block, openedAt));
        }

        for (var n = 0; n < released; n++)
        {
            var item = WorkItemFactory.NewProviderOp(home, "message.send", openedAt);
            db.WorkItems.Add(item);
            db.WorkItemWaits.Add(new WorkItemWait
            {
                WorkItem = item,
                ProviderBlock = block,
                OpenedAt = openedAt,
                Status = WaitStatus.Released,
                ReleaseReason = WaitReleaseReason.BlockTrial,
                ReleasedAt = openedAt,
            });
        }

        await db.SaveChangesAsync(Ct);
        var journal = new JournalWriter(harness.Clock);
        foreach (var item in items)
        {
            await ProviderBlocks.SayBlockedAsync(db, journal, item, block, attempt: null, Ct);
            await db.SaveChangesAsync(Ct);
        }

        return block.PublicId;
    }

    /// <summary>The block, tracked in <paramref name="db"/>, and the attempt after the one that met it, about to get past it.</summary>
    private static async Task<(ProviderBlock Block, Attempt Attempt)> GettingPastAsync(JasonDbContext db, string id)
    {
        var block = await db.ProviderBlocks.SingleAsync(b => b.PublicId == id, Ct);
        var opened = await db.Attempts.Include(a => a.WorkItem).SingleAsync(a => a.Id == block.OpenedByAttemptId, Ct);
        var attempt = WorkItemFactory.NewAttempt(opened.WorkItem!, opened.Number + 1, AttemptStatus.Succeeded, Noon);
        db.Attempts.Add(attempt);
        return (block, attempt);
    }

    private static async Task AttemptsClearStandsAsync(DispatchHarness harness, string winner)
    {
        await using var check = harness.Open();
        var stands = await check.ProviderBlocks.AsNoTracking().Include(b => b.ClearedByAttempt).SingleAsync(Ct);
        Assert.True(
            stands.ClearedByAttempt?.PublicId == winner && stands.ClearedByType is null && stands.ClearedById is null,
            $"The block was cleared by attempt {stands.ClearedByAttempt?.PublicId} and {stands.ClearedByType}:{stands.ClearedById}, not by {winner} alone.");
        Assert.Empty(await check.Journal.AsNoTracking().Where(e => e.Kind == JournalKinds.ProviderBlockCleared && e.ActorType == ActorType.Human).ToListAsync(Ct));
    }

    /// <summary>The campaigns a line of <paramref name="kind"/> was said in, in order.</summary>
    private static async Task<string[]> CampaignsSaidAsync(JasonDbContext db, string kind) =>
        [.. (await db.Journal.AsNoTracking().Where(e => e.Kind == kind).Select(e => e.Campaign!.PublicId).ToListAsync(Ct)).Distinct().Order(StringComparer.Ordinal)];

    /// <summary>
    /// A context on the harness's database that gives up on a lock after a second instead of the runtime's thirty, so a
    /// write the lock holds off is seen refused rather than waited for. The harness's interference never fires in it.
    /// </summary>
    private static JasonDbContext Impatient(DispatchHarness harness)
    {
        var builder = new DbContextOptionsBuilder<JasonDbContext>();
        JasonDbContext.Configure(builder, harness.DatabaseFile).UseSqlite($"Data Source={harness.DatabaseFile};Default Timeout=1");
        return new JasonDbContext(builder.Options);
    }

    /// <summary>Whether the write was refused for the lock another writer holds: 5 is SQLITE_BUSY, however EF Core wraps it.</summary>
    private static bool Busy(Exception? refused)
    {
        for (var exception = refused; exception is not null; exception = exception.InnerException)
        {
            if (exception is SqliteException { SqliteErrorCode: 5 })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every line written so far has been read by the manager's summon: the watermark of each campaign is past it.</summary>
    private static async Task ConsumeAsync(DispatchHarness harness)
    {
        await using var db = harness.Open();
        var last = await db.Journal.AsNoTracking().MaxAsync(e => (int?)e.Id, Ct) ?? 0;
        foreach (var campaign in await db.Campaigns.ToListAsync(Ct))
        {
            campaign.ManagerEventWatermark = last;
        }

        await db.SaveChangesAsync(Ct);
    }

    /// <summary>What SQLite plans for a recorded command, bound to the values it was sent with; one step a line.</summary>
    private static List<string> PlanOf(DispatchHarness harness, Command command)
    {
        using var connection = new SqliteConnection(JasonDbContext.ConnectionString(harness.DatabaseFile));
        connection.Open();
        using var explain = connection.CreateCommand();
        explain.CommandText = "EXPLAIN QUERY PLAN " + command.Text;
        foreach (var (name, value) in command.Parameters)
        {
            explain.Parameters.AddWithValue(name, value);
        }

        using var reader = explain.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
        {
            plan.Add(reader.GetString(3));
        }

        return plan;
    }

    private sealed record Command(string Text, IReadOnlyList<(string Name, object Value)> Parameters);

    /// <summary>Every query a context sends, with its parameters, as it is sent.</summary>
    private sealed class Recorder : DbCommandInterceptor
    {
        public List<Command> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new Command(command.CommandText, [.. command.Parameters.Cast<DbParameter>().Select(parameter => (parameter.ParameterName, parameter.Value ?? DBNull.Value))]));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
