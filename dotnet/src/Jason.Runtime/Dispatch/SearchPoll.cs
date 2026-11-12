using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Audiences;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// What one poll did: the reads of searches it made, the searches it ended as stalled, and the stops it made for the
/// searches of campaigns archived while they ran.
/// </summary>
public sealed record SearchPollReport(int Read, int Stalled, int Stopped = 0);

/// <summary>
/// The step of a scan that follows each search a campaign's audience is filled by, after the inbound read and before the
/// respond step: a search it ends is summoned for in the same scan, and a read it makes is claimed there.
/// <para>
/// For each search still followed in an active campaign, with no read or stop of it under way — the dispatcher's or
/// anybody's — one decision from its row alone. Its reads have landed nobody for the stall bound: it ends <c>stalled</c>,
/// said once, and is never read again. Otherwise, where the last read said more people are readable now, or the cadence
/// has passed since the dispatcher's last read, or since the search began — doubled for each of the dispatcher's reads in
/// a row that ended without an answer, as the inbound read waits — one <c>audience.search_status</c> work item naming it, made by the dispatcher
/// through the routine every work item is made by, so it is routed, claimed, retried and recorded like any other, and
/// lands what it reads wherever it ends. The step keeps nothing; a runtime started again reads the same rows.
/// </para>
/// <para>
/// The stall is measured from the start of the quiet — the search's start, the last landing that made somebody a member,
/// or the last change between held and moving — to the moment the dispatcher made its latest read, once that read has
/// ended: so a campaign paused for hours is read once more before its search is called stalled. A read that failed lands
/// nobody too. A search the provider holds — paused, out of credits, at the plan's limit — is read on
/// <c>Dispatcher:SearchHeldCadenceSeconds</c> and called stalled only after <c>Dispatcher:SearchHeldStallSeconds</c>, its
/// state named in the line, since it spends nothing until a person acts at the provider and may be resumed there.
/// </para>
/// </summary>
public sealed class SearchPoll(
    WorkItemService items,
    TimeProvider clock,
    LiveSettings<DispatcherOptions> settings,
    SearchLanding landing,
    ILogger<SearchPoll> logger)
{
    /// <summary>The operation a read of a search is.</summary>
    public const string Operation = SearchLanding.Status;

    /// <summary>How many people one read asks for: the most the operation allows.</summary>
    public const int PageSize = 100;

    private static readonly Action<ILogger, string, Exception?> Failed = LoggerMessage.Define<string>(
        LogLevel.Warning,
        new EventId(1, nameof(Failed)),
        "The poll of a search of campaign {CampaignId} failed; the rest of the scan continues without it");

    private static readonly Action<ILogger, string, string, Exception?> FailedToParse = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(1, nameof(Failed)),
        "The poll of a search of campaign {CampaignId} failed; the rest of the scan continues without it: {Failure}");

    private static readonly Action<ILogger, string, Exception?> Moved = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(2, nameof(Moved)),
        "A search of campaign {CampaignId} was moved by another writer while the poll was reading it");

    public async Task<SearchPollReport> PollAsync(JasonDbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var options = settings.Current;
        var now = clock.GetUtcNow().UtcDateTime;
        var stopped = await StopArchivedAsync(db, ct).ConfigureAwait(false);
        var followed = await Following(db).ToListAsync(ct).ConfigureAwait(false);
        if (followed.Count == 0)
        {
            return new SearchPollReport(0, 0, stopped);
        }

        // A first look, outside any transaction, so a scan with nothing to do takes no writer: it is looked again inside the
        // transaction that acts.
        var busy = await UnderWayAsync(db, [.. followed.Select(search => search.CampaignId).Distinct()], ct).ConfigureAwait(false);
        var read = 0;
        var stalled = 0;
        foreach (var search in followed.Where(search => !busy.Contains(search.WorkItem)))
        {
            // Per search, because the steps after this one summon and hand out the work there is: one search's write that
            // will not land costs that search, not every one behind it.
            try
            {
                switch (Decide(search, options, now))
                {
                    case Decision.Stall:
                        stalled += await StallAsync(db, search, ct).ConfigureAwait(false) ? 1 : 0;
                        break;
                    case Decision.Read:
                        read += await StartAsync(db, search, now, ct).ConfigureAwait(false) ? 1 : 0;
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                PollFailed(search.CampaignPublicId, ex);
                db.ChangeTracker.Clear();
            }
        }

        return new SearchPollReport(read, stalled, stopped);
    }

    /// <summary>
    /// Archiving is how a person stops a campaign, and a search goes on spending at the provider until it is stopped: so
    /// each search of an archived campaign still in flight, with no stop of it open and none made since the archive, gets
    /// one stop, made by the dispatcher and claimed in the archived campaign. A stop that then fails is not made again: it
    /// is protective work, and its failure is a person's to read.
    /// </summary>
    private async Task<int> StopArchivedAsync(JasonDbContext db, CancellationToken ct)
    {
        var archived = await ArchivedInFlight(db).ToListAsync(ct).ConfigureAwait(false);
        var stopped = 0;
        foreach (var search in archived)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
                var made = await StopsSince(db, search.CampaignId, search.ArchivedAt ?? DateTime.MinValue).ToListAsync(ct).ConfigureAwait(false);
                if (made.Exists(context => context["input"]?["search"] is JsonValue named && named.TryGetValue(out string? stop) && stop == search.WorkItem))
                {
                    await transaction.RollbackAsync(ct).ConfigureAwait(false);
                    continue;
                }

                await items.CreateCoreAsync(StopRequest(search.Campaign, search.WorkItem), Actors.Dispatcher, LineageRecord.Root, ct).ConfigureAwait(false);
                await transaction.CommitAsync(ct).ConfigureAwait(false);
                stopped++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                PollFailed(search.Campaign, ex);
                db.ChangeTracker.Clear();
            }
        }

        return stopped;
    }

    /// <summary>
    /// What a search with no work of it under way needs now. Stalled: the dispatcher's latest read began the stall bound
    /// after the quiet did, and has ended. Due: the latest read said more is readable now and did not fail, or the wait has
    /// passed since the latest read was made, or since the search began — a cadence, doubled for each of the dispatcher's
    /// reads in a row that ended without an answer, never more than a day.
    /// </summary>
    internal static Decision Decide(Followed search, DispatcherOptions options, DateTime now)
    {
        // The dispatcher's latest read, as the step's first look saw it, still under way: it may have ended since, and how
        // it ended decides the next wait, so the next scan reads it again rather than guess.
        if (search.ReadStatus is { } status && AudienceSearches.Open.Contains(status))
        {
            return Decision.None;
        }

        // A search the provider holds spends nothing until a person acts there, so it is read on a cadence and bound of its
        // own, from the moment it was held; one that moves again is read on the ordinary pair from the moment it moved.
        var held = SearchLanding.IsHeld(search.State);
        var bound = held ? options.SearchHeldStallSeconds : options.SearchStallSeconds;
        var cadence = held ? options.SearchHeldCadenceSeconds : options.SearchCadenceSeconds;
        if (search.ReadAt is { } last && last - search.QuietSince >= TimeSpan.FromSeconds(bound))
        {
            return Decision.Stall;
        }

        var streak = Streak(search);
        if (search.More && streak == 0)
        {
            return Decision.Read;
        }

        var wait = Math.Min(cadence * Math.Pow(2, Math.Clamp(streak, 0, 32)), MaximumWaitSeconds);
        return now >= (search.ReadAt ?? search.CreatedAt).AddSeconds(wait) ? Decision.Read : Decision.None;
    }

    /// <summary>
    /// How many of the dispatcher's reads in a row ended without an answer, the latest among them: one that failed, was
    /// cancelled or expired. A read that answered, whoever made it, ended the streak (the landing set it back to none).
    /// </summary>
    private static int Streak(Followed search) =>
        search.ReadStatus is WorkItemStatus.Failed or WorkItemStatus.Cancelled or WorkItemStatus.Expired ? search.Failures + 1 : 0;

    /// <summary>The longest a search waits for its next read, however many in a row failed: a day.</summary>
    public const int MaximumWaitSeconds = 86_400;

    /// <summary>The searches still followed, of the campaigns that run, read off the index that holds only those.</summary>
    internal static IQueryable<Followed> Following(JasonDbContext db) =>
        AudienceSearches.InFlight(db)
            .AsNoTracking()

            // The index's own order — a campaign's searches, oldest first — so the read never sorts, and never scans the ended.
            .OrderBy(search => search.CampaignId)
            .ThenBy(search => search.Id)
            .Select(search => new Followed(
                search.Id,
                search.CampaignId,
                search.Campaign!.PublicId,
                search.SearchWorkItem!.PublicId,
                search.More,
                search.ReadAt,
                search.ReadWorkItem == null ? null : search.ReadWorkItem.Status,
                search.Failures,
                search.QuietSince,
                search.CreatedAt,
                search.State));

    /// <summary>
    /// The searches that have work under way in these campaigns, whoever made it — a read of their people or a stop: one
    /// read of the campaigns' open work of those operations, on the index of a campaign's work by status, and the search
    /// each names under <c>args.search</c>. A search being stopped is neither read nor called stalled meanwhile.
    /// </summary>
    internal static async Task<HashSet<string>> UnderWayAsync(JasonDbContext db, IReadOnlyCollection<int> campaigns, CancellationToken ct)
    {
        var contexts = await AudienceSearches.OpenWork(db, campaigns)
            .AsNoTracking()
            .Select(item => item.Context)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return new HashSet<string>(
            contexts.Select(context => context["input"]?["search"] is JsonValue named && named.TryGetValue(out string? search) ? search : null).OfType<string>(),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// A read of the search, and the row's note that it was made, which it was, and how the read before it ended: all, or
    /// nothing. Work of the search made since the step first looked keeps it from being made.
    /// </summary>
    private async Task<bool> StartAsync(JasonDbContext db, Followed search, DateTime now, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        if ((await UnderWayAsync(db, [search.CampaignId], ct).ConfigureAwait(false)).Contains(search.WorkItem))
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            return false;
        }

        var item = await items.CreateCoreAsync(Request(search), Actors.Dispatcher, LineageRecord.Root, ct).ConfigureAwait(false);

        // The note consumes "more": a read that then fails waits the cadence, rather than being made again every scan.
        var streak = Streak(search);
        var started = await Unmoved(db, search)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(row => row.ReadAt, now)
                    .SetProperty(row => row.ReadWorkItemId, item.Id)
                    .SetProperty(row => row.Failures, streak)
                    .SetProperty(row => row.More, false),
                ct)
            .ConfigureAwait(false);
        if (started == 0)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            Moved(logger, search.CampaignPublicId, null);
            return false;
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// The search ended as stalled, and its line: once, as the row stood when the step read it, and only while no work of
    /// it has begun since.
    /// </summary>
    private async Task<bool> StallAsync(JasonDbContext db, Followed search, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        var row = (await UnderWayAsync(db, [search.CampaignId], ct).ConfigureAwait(false)).Contains(search.WorkItem)
            ? null
            : await Unmoved(db, search)
                .Include(row => row.Audience)
                .Include(row => row.SearchWorkItem)
                .SingleOrDefaultAsync(ct)
                .ConfigureAwait(false);
        if (row is null)
        {
            await transaction.RollbackAsync(ct).ConfigureAwait(false);
            Moved(logger, search.CampaignPublicId, null);
            return false;
        }

        landing.Stalled(db, row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>The search's row, as long as it is still followed and nobody has read it or landed from it since the step looked.</summary>
    private static IQueryable<AudienceSearch> Unmoved(JasonDbContext db, Followed search) =>
        db.AudienceSearches.Where(row => row.Id == search.Id
            && row.EndedAs == null
            && row.ReadAt == search.ReadAt
            && row.QuietSince == search.QuietSince
            && row.More == search.More);

    /// <summary>The searches still in flight of the campaigns archived: each one's campaign, when it was archived, and its work item.</summary>
    internal static IQueryable<ArchivedSearch> ArchivedInFlight(JasonDbContext db) =>
        db.AudienceSearches
            .AsNoTracking()
            .Where(search => search.EndedAs == null && search.Campaign!.Status == CampaignStatus.Archived)
            .Select(search => new ArchivedSearch(search.CampaignId, search.Campaign!.PublicId, search.Campaign.ArchivedAt, search.SearchWorkItem!.PublicId));

    /// <summary>
    /// The stops of a campaign's searches still open, or finished since <paramref name="since"/>: what each was asked to
    /// stop. A stop made since then is one or the other, and each half is read off an index of the campaign's work, by
    /// status and by when it finished, never the campaign's whole history.
    /// </summary>
    internal static IQueryable<System.Text.Json.Nodes.JsonObject> StopsSince(JasonDbContext db, int campaignId, DateTime since) =>
        db.WorkItems
            .AsNoTracking()
            .Where(item => item.CampaignId == campaignId && AudienceSearches.Open.Contains(item.Status) && item.Operation == SearchLanding.Stop)
            .Select(item => item.Context)
            .Concat(db.WorkItems
                .AsNoTracking()
                .Where(item => item.CampaignId == campaignId && item.FinishedAt >= since && item.Operation == SearchLanding.Stop)
                .Select(item => item.Context));

    /// <summary>One search of an archived campaign still in flight.</summary>
    internal sealed record ArchivedSearch(int CampaignId, string Campaign, DateTime? ArchivedAt, string WorkItem);

    private static WorkItemCreateRequest StopRequest(string campaign, string search) =>
        new(
            campaign,
            WorkItemKind.ProviderOp,
            Role: null,
            SearchLanding.Stop,
            ContactId: null,
            ExecutionProfile: null,
            Priority: null,
            NotBefore: null,
            DueAt: null,
            TimeoutSeconds: null,
            HeartbeatSeconds: null,
            MaxAttempts: null,
            new JsonObject { ["input"] = new JsonObject { ["search"] = search } },
            ResultFormat: null,
            Actor: null,
            Reason: "campaign archived");

    private static WorkItemCreateRequest Request(Followed search) =>
        new(
            search.CampaignPublicId,
            WorkItemKind.ProviderOp,
            Role: null,
            Operation,
            ContactId: null,
            ExecutionProfile: null,
            Priority: null,
            NotBefore: null,
            DueAt: null,
            TimeoutSeconds: null,
            HeartbeatSeconds: null,
            MaxAttempts: null,
            new JsonObject { ["input"] = new JsonObject { ["search"] = search.WorkItem, ["limit"] = PageSize } },
            ResultFormat: null,
            Actor: null,
            Reason: "search poll");

    private void PollFailed(string campaignId, Exception exception)
    {
        if (ParseFailure.Is(exception))
        {
            FailedToParse(logger, campaignId, ParseFailure.Describe(exception), null);
        }
        else
        {
            Failed(logger, campaignId, exception);
        }
    }

    /// <summary>One search the step follows, as its row stood when the step read it.</summary>
    internal sealed record Followed(
        int Id,
        int CampaignId,
        string CampaignPublicId,
        string WorkItem,
        bool More,
        DateTime? ReadAt,
        WorkItemStatus? ReadStatus,
        int Failures,
        DateTime QuietSince,
        DateTime CreatedAt,
        string? State = null);

    /// <summary>What the step does with one search this scan.</summary>
    internal enum Decision
    {
        None,
        Read,
        Stall,
    }
}
