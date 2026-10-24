using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The step of a scan between the summon and the claim: waiting work goes back to the queue once what it waited for has
/// happened — the items it named have ended, all of them or any one, its deadline has come, or the provider block it is
/// parked on has cleared — and a provider block whose trial is due lets one of the items it holds through. After the lease enforcer,
/// so an item whose last attempt was lost for good in this scan releases its waiter in the same scan; before the claim,
/// so what is released can be claimed in that scan too.
/// <para>
/// The condition is read from rows, never from an event: a wait whose items had all ended before it was written, or whose
/// deadline passed while the runtime was down, is simply ready at the next scan. Nothing here reads a context or anything
/// anybody wrote — statuses, identifiers and the clock only — and no campaign: a released item is ordinary created work,
/// and whether its campaign lets it run is the claim's to decide, as for any other.
/// </para>
/// </summary>
public sealed class Resumer(JournalWriter journal, TimeProvider clock)
{
    /// <summary>
    /// How many waits that are over one scan releases at most, the oldest first; the rest are released by the scans after
    /// it. A block that clears with ten thousand items on it so costs each scan what this many releases cost.
    /// </summary>
    internal const int ReleasesPerScan = 500;

    public async Task<int> ResumeAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var now = clock.GetUtcNow().UtcDateTime;
        var ready = await ReadyAsync(db, now, ct).ConfigureAwait(false);

        // One short write a wait, so an item cancelled a moment ago costs the others nothing.
        var resumed = 0;
        foreach (var row in ready)
        {
            var wait = await db.WorkItemWaits
                .Include(w => w.Attempt)
                .FirstOrDefaultAsync(w => w.Id == row.Id && w.Status == WaitStatus.Open, ct)
                .ConfigureAwait(false);
            var item = wait is null
                ? null
                : await db.WorkItems.FirstOrDefaultAsync(w => w.Id == wait.WorkItemId && w.Status == WorkItemStatus.Waiting, ct).ConfigureAwait(false);
            if (wait is null || item is null)
            {
                db.ChangeTracker.Clear();
                continue;
            }

            var because = SnakeCaseEnumConverter<WaitReleaseReason>.Parse(row.Reason);
            OpenWaits.Release(db, journal, wait, item, because, Actors.Dispatcher, reason: null, now);

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                resumed++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // The item was cancelled or expired between the read and this write; that stands.
            }

            db.ChangeTracker.Clear();
        }

        return resumed + await TrialsAsync(db, now, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// One item of each block whose trial is due goes back to the queue as that block's trial, and the block records it, in
    /// one write: until the trial has ended no other item of the block is let through. A block whose trial is due but which
    /// holds nothing that could run now lets nothing through here; the next item the claim finds it holding is the trial.
    /// </summary>
    private async Task<int> TrialsAsync(JasonDbContext db, DateTime now, CancellationToken ct)
    {
        var due = await DueTrialsAsync(db, now, ct).ConfigureAwait(false);
        var released = 0;
        foreach (var row in due.Where(row => row.WaitId is not null))
        {
            var block = await db.ProviderBlocks.FirstOrDefaultAsync(b => b.Id == row.BlockId && b.Status == ProviderBlockStatus.Open, ct).ConfigureAwait(false);
            var wait = block is null
                ? null
                : await db.WorkItemWaits.Include(w => w.Attempt).Include(w => w.ProviderBlock)
                    .FirstOrDefaultAsync(w => w.Id == row.WaitId && w.Status == WaitStatus.Open, ct)
                    .ConfigureAwait(false);
            var item = wait is null
                ? null
                : await db.WorkItems.FirstOrDefaultAsync(w => w.Id == wait.WorkItemId && w.Status == WorkItemStatus.Waiting, ct).ConfigureAwait(false);
            if (block is null || wait is null || item is null || !ProviderBlocks.Holds(block, wait.ProviderBlock))
            {
                db.ChangeTracker.Clear();
                continue;
            }

            OpenWaits.Release(db, journal, wait, item, WaitReleaseReason.BlockTrial, Actors.Dispatcher, reason: null, now);
            block.TrialWorkItemId = item.Id;

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                released++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // The item was cancelled or expired, or the block cleared, between the read and this write; the next scan
                // picks another, or releases what the block held.
            }

            db.ChangeTracker.Clear();
        }

        return released;
    }

    /// <summary>
    /// The open waits whose item is waiting and whose condition holds, oldest first and no more than
    /// <see cref="ReleasesPerScan"/> of them, each with why it is over. The items
    /// come before the deadline: a wait whose items ended and whose deadline came by the same scan was over because of the
    /// items, which is what it waited for. Raw SQL because the condition is two correlated subqueries over the named items;
    /// its plan is pinned by a test — the waiting items by their status, each one's open wait by the index of the waits by
    /// item and status, each wait's items and each item by its key.
    /// </summary>
    internal static Task<List<ReadyWait>> ReadyAsync(JasonDbContext db, DateTime now, CancellationToken ct) =>
        db.Database.SqlQueryRaw<ReadyWait>(ReadySql, now).ToListAsync(ct);

    /// <summary>
    /// The open blocks whose trial is due and that have none under way, each with the one wait its trial is: protective
    /// work first, then the highest priority, then the oldest item, among the items the claim could run now — in a campaign
    /// that is active, or paused for work that runs in a paused campaign, and not yet past its due date. A trial is under
    /// way as <see cref="TrialUnderWay"/> says. A sender's block holds no trial of its own while its account's block is
    /// open: the account's trial answers first, and it is drawn from the items the account's senders' blocks hold as well
    /// as its own, since the account covers them. Raw SQL because the pick is a correlated subquery with an order, joined
    /// in the order it is written — the blocks a trial is drawn from, then their waits — so the waits are read by block
    /// rather than every waiting item by its status; its plan is pinned by a test — the due blocks by the index of the blocks by status and trial moment, an account's open sender
    /// blocks by the index of one open block a sender, each block's waits by the index of the waits by block and status,
    /// and every other row by a key.
    /// </summary>
    internal static Task<List<DueTrial>> DueTrialsAsync(JasonDbContext db, DateTime now, CancellationToken ct) =>
        db.Database.SqlQueryRaw<DueTrial>(DueTrialsSql, now).ToListAsync(ct);

    /// <summary>
    /// Whether the item <c>t</c> of the campaign <c>tc</c>, a block's trial, is still under way: claimed, running or
    /// awaiting a person's decision, or in the queue where the claim could take it — its campaign active, or paused and
    /// the work protective. A trial left in the queue of a campaign paused since holds nothing: the claim would never take
    /// it, and the block's next trial is another item's. One text, which the resume step and the claim both read.
    /// </summary>
    internal static readonly string TrialUnderWay = ComposeUnderWay();

    /// <summary>The claim's read of <see cref="TrialUnderWay"/> for one item, its id the one parameter.</summary>
    internal static readonly string TrialUnderWaySql =
        $"SELECT t.id AS \"Value\" FROM work_items t JOIN campaigns tc ON tc.id = t.campaign_id WHERE t.id = {{0}} AND {TrialUnderWay}";

    private static readonly string ReadySql = Compose();

    private static readonly string DueTrialsSql = ComposeTrials();

    private static string Compose()
    {
        static string Text<TEnum>(TEnum value) where TEnum : struct, Enum => $"'{SnakeCaseEnumConverter<TEnum>.Format(value)}'";
        var ended = string.Join(", ", WorkItemTransitions.Terminal.Order().Select(Text));

        // The constants are our own vocabulary, written into the text rather than bound, so the planner sees the statuses it
        // searches the indexes by; the bound is written in the same way. The clock is the one parameter.
        return $$"""
            SELECT r.id AS "Id", r.reason AS "Reason" FROM (
              SELECT x.id,
                     CASE
                       WHEN x.mode = {{Text(WaitMode.All)}} AND NOT EXISTS (
                              SELECT 1 FROM work_item_wait_items i JOIN work_items c ON c.id = i.child_work_item_id
                              WHERE i.wait_id = x.id AND c.status NOT IN ({{ended}}))
                         THEN {{Text(WaitReleaseReason.ChildrenEnded)}}
                       WHEN x.mode = {{Text(WaitMode.Any)}} AND EXISTS (
                              SELECT 1 FROM work_item_wait_items i JOIN work_items c ON c.id = i.child_work_item_id
                              WHERE i.wait_id = x.id AND c.status IN ({{ended}}))
                         THEN {{Text(WaitReleaseReason.ChildrenEnded)}}
                       WHEN x.provider_block_id IS NOT NULL AND EXISTS (
                              SELECT 1 FROM provider_blocks b WHERE b.id = x.provider_block_id AND b.status = {{Text(ProviderBlockStatus.Cleared)}})
                         THEN {{Text(WaitReleaseReason.BlockCleared)}}
                       WHEN x.deadline <= {0} AND x.until IS NOT NULL THEN {{Text(WaitReleaseReason.MomentReached)}}
                       WHEN x.deadline <= {0} THEN {{Text(WaitReleaseReason.WaitCeiling)}}
                     END AS reason
              FROM work_item_waits x JOIN work_items w ON w.id = x.work_item_id
              WHERE x.status = {{Text(WaitStatus.Open)}} AND w.status = {{Text(WorkItemStatus.Waiting)}}
            ) AS r
            WHERE r.reason IS NOT NULL
            ORDER BY r.id
            LIMIT {{ReleasesPerScan}}
            """;
    }

    private static string ComposeUnderWay()
    {
        static string Text<TEnum>(TEnum value) where TEnum : struct, Enum => $"'{SnakeCaseEnumConverter<TEnum>.Format(value)}'";
        WorkItemStatus[] taken = [WorkItemStatus.Scheduled, WorkItemStatus.Processing, WorkItemStatus.AwaitingApproval];
        var protective = Words(OperationCatalog.RunsInPausedCampaign);
        return $"(t.status IN ({string.Join(", ", taken.Select(Text))}) OR (t.status = {Text(WorkItemStatus.Created)} "
            + $"AND (tc.status = {Text(CampaignStatus.Active)} OR (tc.status = {Text(CampaignStatus.Paused)} AND t.operation IN ({protective})))))";
    }

    private static string ComposeTrials()
    {
        static string Text<TEnum>(TEnum value) where TEnum : struct, Enum => $"'{SnakeCaseEnumConverter<TEnum>.Format(value)}'";

        // Protective work is the work the catalogue runs in a paused campaign, as the claim and the notifications read it.
        var protective = Words(OperationCatalog.RunsInPausedCampaign);

        return $$"""
            SELECT b.id AS "BlockId", (
                     SELECT x.id FROM provider_blocks h
                     CROSS JOIN work_item_waits x
                     CROSS JOIN work_items w
                     CROSS JOIN campaigns c
                     WHERE (h.id = b.id
                            OR (b.scope = {{Text(ProviderBlockScope.Account)}} AND h.plugin_id = b.plugin_id AND h.binding_identity = b.binding_identity
                                AND h.status = {{Text(ProviderBlockStatus.Open)}} AND h.scope = {{Text(ProviderBlockScope.Sender)}}))
                       AND x.provider_block_id = h.id AND w.id = x.work_item_id AND c.id = w.campaign_id
                       AND x.status = {{Text(WaitStatus.Open)}} AND w.status = {{Text(WorkItemStatus.Waiting)}}
                       AND (c.status = {{Text(CampaignStatus.Active)}} OR (c.status = {{Text(CampaignStatus.Paused)}} AND w.operation IN ({{protective}})))
                       AND (w.due_at IS NULL OR w.due_at > {0})
                     ORDER BY CASE WHEN w.operation IN ({{protective}}) THEN 0 ELSE 1 END, w.priority DESC, w.id
                     LIMIT 1) AS "WaitId"
            FROM provider_blocks b
            WHERE b.status = {{Text(ProviderBlockStatus.Open)}} AND b.next_trial_at <= {0}
              AND NOT EXISTS (SELECT 1 FROM work_items t JOIN campaigns tc ON tc.id = t.campaign_id
                              WHERE t.id = b.trial_work_item_id AND {{TrialUnderWay}})
              AND NOT (b.scope = {{Text(ProviderBlockScope.Sender)}} AND EXISTS (
                       SELECT 1 FROM provider_blocks a
                       WHERE a.plugin_id = b.plugin_id AND a.binding_identity = b.binding_identity
                         AND a.status = {{Text(ProviderBlockStatus.Open)}} AND a.scope = {{Text(ProviderBlockScope.Account)}}))
            ORDER BY b.id
            """;
    }

    /// <summary>
    /// Operation names written into the text, as the statuses are: the catalogue's own identifiers, lowercase words and
    /// dots, and refused here if one ever is not.
    /// </summary>
    private static string Words(IEnumerable<string> operations) =>
        string.Join(", ", operations.Select(operation =>
            operation.Length > 0 && operation.All(static c => c is (>= 'a' and <= 'z') or '.' or '_')
                ? $"'{operation}'"
                : throw new InvalidOperationException($"The operation identifier '{operation}' cannot be written into a query.")));

    /// <summary>One wait that is over, and why, as the release reason's own word.</summary>
    internal sealed record ReadyWait(int Id, string Reason);

    /// <summary>One block whose trial is due, and the wait its trial is; null when it holds nothing the claim could run now.</summary>
    internal sealed record DueTrial(int BlockId, int? WaitId);
}
