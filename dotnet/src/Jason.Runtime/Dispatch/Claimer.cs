using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Approvals;
using Jason.Runtime.Audiences;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Logging;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The tenth step of a scan, and the only one that hands work out. Everything happens inside one immediate
/// transaction, so two runtimes — or two scans — cannot both claim the same item: the second waits, sees the
/// item already taken and moves on. One item per campaign per scan keeps a busy campaign from starving the
/// others, and never more than the free handler slots, so a queued item's lease cannot expire before it runs.
/// </summary>
public sealed class Claimer(
    JournalWriter journal,
    TimeProvider clock,
    AttemptOutcomes outcomes,
    EntryCommandResolver resolver,
    AgentLaunchPlanner agents,
    LiveSettings<RolesOptions> roles,
    PluginRegistry plugins,
    RouteRegistry routes,
    ExternalIdStore identifiers,
    ISuppressionCheck suppression,
    LiveSettings<DispatcherOptions> settings,
    LiveSettings<LaunchesOptions> launches,
    JasonPaths paths,
    ILogger<Claimer> logger,
    PatternTiming? timing = null)
{
    /// <summary>How the claim matches a schema's patterns: the runtime's one default, which a test replaces.</summary>
    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    /// <summary>The kind an operation names the send it made the provider take by.</summary>
    private const string SendKind = "send";

    /// <summary>The kind an operation names the search it had the provider start by.</summary>
    private const string SearchKind = "search";

    private static readonly Action<ILogger, string, Exception?> ClaimSkipped = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(1, nameof(ClaimSkipped)),
        "Claim skipped: another writer changed work item {WorkItemId} first");

    private static readonly Action<ILogger, string, string, string, Exception?> EndedAtClaim = LoggerMessage.Define<string, string, string>(
        LogLevel.Warning,
        new EventId(2, nameof(EndedAtClaim)),
        "Attempt {AttemptId} of work item {WorkItemId} ended at the claim: {Code}");

    private static readonly Action<ILogger, string, string, string, Exception?> MemoryFileUnreadable = LoggerMessage.Define<string, string, string>(
        LogLevel.Warning,
        new EventId(3, nameof(MemoryFileUnreadable)),
        "Attempt {AttemptId} of work item {WorkItemId}: the {Level} memory file's path cannot be written as a read rule");

    /// <summary>
    /// A memory file the claim named and the host could not write as a rule for one file, by the ids a person can type
    /// and the level, one of three closed words: never the path, which is built from names a person gave.
    /// </summary>
    private readonly record struct UnreadableFile(string CampaignId, string WorkItemId, string AttemptId, string Level);

    /// <summary>
    /// An attempt the claim itself ended, by the ids a person can type and the code the attempt recorded. The code is
    /// one of the closed words in <see cref="AttemptErrors"/>: never the attempt's message, which names what the work
    /// was about.
    /// </summary>
    private readonly record struct AttemptEnd(string CampaignId, string WorkItemId, string AttemptId, string Code);

    /// <summary>
    /// Claims up to <paramref name="freeSlots"/> items. Work that cannot be run at all is failed inside the
    /// same transaction rather than returned: it is finished, not dispatched.
    /// </summary>
    public async Task<IReadOnlyList<ClaimedWork>> ClaimAsync(JasonDbContext db, int freeSlots, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (freeSlots <= 0)
        {
            return [];
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var current = settings.Current;
        var claimed = new List<ClaimedWork>();
        var ended = new List<AttemptEnd>();
        var unreadable = new List<UnreadableFile>();

        // Both snapshots are read once, so every item this scan hands out was decided against one pair of them
        // and an activation that happens mid-scan belongs to the next one.
        var packages = plugins.Snapshot;
        var routing = routes.Snapshot;

        // And the one setting an agent claim reads, for the same reason and in the same place: a default edited
        // halfway through a scan belongs to the next scan, not to half of this one.
        var globalDefault = roles.Current.DefaultExecutionProfile;

        // And the daily limits, for the same reason: a limit raised halfway through a scan applies from the next one. The
        // day is UTC, since a campaign has no time zone of its own.
        var limits = launches.Current.MaxPerDay;
        var today = now.Date;

        await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
        foreach (var id in await CandidatesAsync(db, now, freeSlots, ct).ConfigureAwait(false))
        {
            var item = await db.WorkItems.WithNavigation().Include(w => w.Attempts)
                .FirstOrDefaultAsync(w => w.Id == id, ct)
                .ConfigureAwait(false);
            if (item is null || item.Status != WorkItemStatus.Created)
            {
                continue;
            }

            // The decision before the record of it. Everything that can refuse a provider item is decided here,
            // and one of the answers is not a refusal at all: work whose operation needs a person's approval is
            // parked, and parking makes no attempt, because an attempt is a run and nothing has run.
            var facts = item.Kind == WorkItemKind.ProviderOp ? await FactsAsync(db, item, routing, ct).ConfigureAwait(false) : null;
            var verdict = facts is null ? null : ProviderOpPreflight.Check(facts, packages, routing, _timing);

            // A search the claim lets through, to run now or to wait for a person, runs under one of the campaign's audiences
            // from here on: its definition passed the routed plugin's schema. One the claim refuses becomes no audience.
            if (verdict is { Refused: false, Plan: { } searching } && CampaignAudiences.Recorded(searching.Contract.Id))
            {
                await AudienceAsync(db, item, searching, now, ct).ConfigureAwait(false);
            }

            // A role its campaign has started enough of today is held, not refused: a failure would wake the manager, and a
            // responder's reply would reach it by a second path. It waits, with no attempt, for the next 00:00Z.
            if (item.Kind == WorkItemKind.AiRole
                && item.Role is { } role && limits.TryGetValue(role, out var limit)
                && await SpentAsync(db, item, limit, today, ct).ConfigureAwait(false))
            {
                var tomorrow = today.AddDays(1);
                outcomes.Hold(db, item, tomorrow, LaunchesSpent(role, limit, today, tomorrow), limit, Actors.Dispatcher);
                await SaveAsync(db, item, ct).ConfigureAwait(false);
                continue;
            }

            // The same question for agent work, asked in the same place and with the same rule: everything that
            // can refuse it is decided before a child exists, and the answer is one reason the attempt keeps.
            var agent = item.Kind == WorkItemKind.AiRole
                ? await agents.PlanAsync(db, item, globalDefault, ct).ConfigureAwait(false)
                : null;

            // Behind another search of its campaign: the item waits, with no attempt and nobody asked, and is claimed again a
            // search cadence later, when the search in the way may have ended — the long cadence of a hold where the
            // provider holds it, since it moves only when a person acts there.
            if (verdict is { Parks: true, Code: AttemptErrors.SearchUnfinished } && facts?.SearchInTheWay is { } inTheWay)
            {
                var error = new AttemptErrorDto(AttemptErrors.SearchUnfinished, verdict.Message!, Retriable: false);
                var held = SearchLanding.IsHeld(await AudienceSearches.Named(db, item.CampaignId, inTheWay)
                    .Select(search => search.State)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false));
                var cadence = held ? current.SearchHeldCadenceSeconds : current.SearchCadenceSeconds;
                outcomes.Behind(db, item, now.AddSeconds(cadence), error, inTheWay, Actors.Dispatcher);
                await SaveAsync(db, item, ct).ConfigureAwait(false);
                continue;
            }

            var asks = verdict is { Parks: true, Code: AttemptErrors.ApprovalRequired };
            var released = asks
                ? await DecideAsync(db, item, verdict!, now, ct).ConfigureAwait(false)
                : null;
            if (asks && released is null)
            {
                // Parked: nothing is handed out and no attempt exists to hand out. The save is the same guarded
                // one the claim itself uses, so a caller who cancelled this item a moment ago still wins.
                await SaveAsync(db, item, ct).ConfigureAwait(false);
                continue;
            }

            // Held by a provider block, whether the twenty-sixth check said so or a person released work the block still
            // holds: the item waits on the block, with no attempt, unless it is the block's trial.
            if (verdict is { Block: { } block } && (released is not null || verdict.Code == AttemptErrors.ProviderBlocked)
                && !await TrialAsync(db, block, item, now, ct).ConfigureAwait(false))
            {
                outcomes.Park(db, item, attempt: null, block, ProviderBlocks.HeldError(item.Operation!, block), Actors.Dispatcher);
                await ProviderBlocks.SayBlockedAsync(db, journal, item, block, attempt: null, ct).ConfigureAwait(false);
                await SaveAsync(db, item, ct).ConfigureAwait(false);
                continue;
            }

            var command = agent switch
            {
                AgentLaunchDecision.Ready ready => ready.Plan.Command,
                AgentLaunchDecision.Legacy => await CommandForAsync(db, item, ct).ConfigureAwait(false),
                _ => null,
            };

            var attempt = Claim(item, current, now, command);
            db.Attempts.Add(attempt);
            journal.Append(
                db,
                Actors.Dispatcher,
                JournalKinds.WorkItemScheduled,
                campaign: null,
                key: "attempt",
                updated: JsonValue.Create(attempt.Number),
                workItem: item,
                attempt: attempt);
            var plan = FailClosed(db, item, attempt, verdict, agent, packages, routing, released);

            // A role's run that follows a wait is told what the wait ended with, and the wait is told which run it
            // resumed, in the same save as the claim, so neither is written for a claim that did not happen.
            var resumption = item.Status == WorkItemStatus.Scheduled
                ? await Resumptions.ForClaimAsync(db, item, attempt, ct).ConfigureAwait(false)
                : null;

            if (!await SaveAsync(db, item, ct).ConfigureAwait(false))
            {
                continue;
            }

            // Every refusal above ends the attempt through the one routine that ends attempts, so this is the one
            // place that can tell an attempt the claim ended from one it handed out. No handler will ever run for it,
            // so no line a handler writes can say what became of the work.
            if (attempt.Status == AttemptStatus.Failed)
            {
                ended.Add(new AttemptEnd(item.Campaign!.PublicId, item.PublicId, attempt.PublicId, attempt.Error!.Code));
            }

            if (item.Status == WorkItemStatus.Scheduled)
            {
                claimed.Add(new ClaimedWork(
                    item.Id,
                    attempt.Id,
                    item.PublicId,
                    attempt.PublicId,
                    item.Campaign!.PublicId,
                    plan,
                    agent is AgentLaunchDecision.Ready ready ? new AgentLaunch(ready.Plan.Deny, ready.Plan.Allow, ready.Plan.CliCommand, ready.Plan.Host) : null,
                    agent switch
                    {
                        AgentLaunchDecision.Ready launched => launched.Plan.MemoryFiles,
                        AgentLaunchDecision.Legacy legacy => legacy.MemoryFiles,
                        _ => null,
                    },
                    // The project comes from the decision that placed the memory files, not from a navigation the claim holds.
                    agent switch
                    {
                        AgentLaunchDecision.Ready launched => launched.Plan.ProjectPublicId,
                        AgentLaunchDecision.Legacy legacy => legacy.ProjectPublicId,
                        _ => null,
                    },
                    resumption));

                if (agent is AgentLaunchDecision.Ready { Plan.Unreadable: { Count: > 0 } levels })
                {
                    unreadable.AddRange(levels.Select(level => new UnreadableFile(item.Campaign!.PublicId, item.PublicId, attempt.PublicId, level)));
                }
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        // Said once the claim is committed, for the same reason as the ends below: a file the role was never going to
        // be given leave to read, about an attempt a failed commit took back, would describe a launch that never was.
        foreach (var file in unreadable)
        {
            using (AttemptLogScope.Begin(logger, file.CampaignId, file.WorkItemId, file.AttemptId))
            {
                MemoryFileUnreadable(logger, file.AttemptId, file.WorkItemId, file.Level, null);
            }
        }

        // Said once the ends are committed, as the scan says its own claims: a line about an attempt that a failed
        // commit then took back would describe something that never happened.
        foreach (var end in ended)
        {
            using (AttemptLogScope.Begin(logger, end.CampaignId, end.WorkItemId, end.AttemptId))
            {
                EndedAtClaim(logger, end.AttemptId, end.WorkItemId, end.Code, null);
            }
        }

        return claimed;
    }

    /// <summary>
    /// One item per campaign, the most important first, and never more than the free slots. Raw SQL because the
    /// per-campaign choice is a window function: doing it in memory would either idle a slot or read the whole
    /// backlog. Its campaign rule is the eligible predicate's, written a second time here and held to it by a test:
    /// an active campaign, or a paused or an archived one for an operation whose contract says its work runs there.
    /// </summary>
    internal static async Task<List<int>> CandidatesAsync(JasonDbContext db, DateTime now, int freeSlots, CancellationToken ct)
    {
        var created = SnakeCaseEnumConverter<WorkItemStatus>.Format(WorkItemStatus.Created);
        var scheduled = SnakeCaseEnumConverter<WorkItemStatus>.Format(WorkItemStatus.Scheduled);
        var processing = SnakeCaseEnumConverter<WorkItemStatus>.Format(WorkItemStatus.Processing);
        var active = SnakeCaseEnumConverter<CampaignStatus>.Format(CampaignStatus.Active);
        var paused = SnakeCaseEnumConverter<CampaignStatus>.Format(CampaignStatus.Paused);
        var archived = SnakeCaseEnumConverter<CampaignStatus>.Format(CampaignStatus.Archived);
        var runsInPaused = JsonSerializer.Serialize(OperationCatalog.RunsInPausedCampaign);
        var runsInArchived = JsonSerializer.Serialize(OperationCatalog.RunsInArchivedCampaign);

        return await db.WorkItems.FromSqlInterpolated($"""
            SELECT * FROM (
              SELECT w.*, ROW_NUMBER() OVER (PARTITION BY w.campaign_id ORDER BY w.priority DESC, w.id ASC) AS rn
              FROM work_items w JOIN campaigns c ON c.id = w.campaign_id
              WHERE w.status = {created}
                AND (c.status = {active}
                     OR (c.status = {paused} AND w.operation IN (SELECT value FROM json_each({runsInPaused})))
                     OR (c.status = {archived} AND w.operation IN (SELECT value FROM json_each({runsInArchived}))))
                AND (w.not_before IS NULL OR w.not_before <= {now})
                AND (w.due_at IS NULL OR w.due_at > {now})
                AND (w.retry_after IS NULL OR w.retry_after <= {now})
                AND NOT EXISTS (SELECT 1 FROM work_items o
                                WHERE o.campaign_id = w.campaign_id AND o.status IN ({scheduled}, {processing})))
            WHERE rn = 1 ORDER BY priority DESC, id ASC LIMIT {freeSlots}
            """)
            .AsNoTracking()
            .Select(w => w.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the work of <paramref name="item"/>'s role in its campaign has started <paramref name="limit"/> runs or more
    /// since <paramref name="today"/>, 00:00Z. A run counts when its attempt started, so an attempt the claim refused, which
    /// never started, spends nothing. One item is let go past the limit: a hold a person resumed. The hold alone writes its
    /// code as the item's last error, and its wait is the item's latest, released by a person, with no attempt and no
    /// block; a wait released any other way, midnight among them, is counted as usual. Columns and the clock only.
    /// </summary>
    private static async Task<bool> SpentAsync(JasonDbContext db, WorkItem item, int limit, DateTime today, CancellationToken ct)
    {
        if (AttemptErrors.IsDailyHold(item.LastError?.Code))
        {
            var latest = await db.WorkItemWaits.AsNoTracking()
                .Where(w => w.WorkItemId == item.Id)
                .OrderByDescending(w => w.Id)
                .Select(w => new { w.Status, w.ReleaseReason, w.AttemptId, w.ProviderBlockId })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);
            if (latest is { Status: WaitStatus.Released, ReleaseReason: WaitReleaseReason.ResumedByActor, AttemptId: null, ProviderBlockId: null })
            {
                return false;
            }
        }

        var started = await db.Attempts.AsNoTracking()
            .CountAsync(
                a => a.WorkItem!.CampaignId == item.CampaignId
                    && a.WorkItem.Kind == WorkItemKind.AiRole
                    && a.WorkItem.Role == item.Role
                    && a.StartedAt >= today,
                ct)
            .ConfigureAwait(false);
        return started >= limit;
    }

    /// <summary>
    /// The last error of an item held at its role's limit for the day: the limit and its moment, and the three ways out — the next
    /// 00:00Z, a person's resume, or a raised limit. Not retried: the moment releases it.
    /// </summary>
    internal static AttemptErrorDto LaunchesSpent(string role, int limit, DateTime today, DateTime tomorrow)
    {
        var runs = limit == 1 ? "1 run" : string.Create(CultureInfo.InvariantCulture, $"{limit} runs");
        return new AttemptErrorDto(
            AttemptErrors.LaunchesSpent,
            $"Work of the {role} role in this campaign has reached the limit Launches:MaxPerDay:{role} sets, {runs} started since {Zulu(today)}; "
            + $"this item waits until {Zulu(tomorrow)}. A person's workitem.resume lets it go sooner, past the limit, and a raised "
            + "limit applies from the next scan to work not yet held, while this item keeps its moment.",
            Retriable: false);

        static string Zulu(DateTime moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<string>?> CommandForAsync(JasonDbContext db, WorkItem item, CancellationToken ct) =>
        item.Kind == WorkItemKind.AiRole ? await resolver.ResolveAsync(db, item.Role!, ct).ConfigureAwait(false) : null;

    /// <summary>Schedules the item and records the attempt that will run it, snapshot and all.</summary>
    private Attempt Claim(WorkItem item, DispatcherOptions current, DateTime now, IReadOnlyList<string>? command)
    {
        WorkItemTransitions.Apply(item, WorkItemStatus.Scheduled, now);
        var limits = EffectiveLimits.For(item, current);
        var number = item.Attempts.Count + 1;
        var attemptId = PublicId.New("att");
        return new Attempt
        {
            PublicId = attemptId,
            WorkItem = item,
            Number = number,
            Command = item.Kind,
            Status = AttemptStatus.Scheduled,
            ExecutionProfile = item.ExecutionProfile,

            // The context as it stands now: an edit after the claim belongs to the next attempt, not to this one.
            ContextSnapshot = item.Context.DeepClone().AsObject(),
            ClaimedAt = now,
            LockUntil = now.AddSeconds(limits.TimeoutSeconds),
            Launch = command is null ? null : new AttemptLaunchDto(command, paths.AttemptWorkDirectory(item.PublicId, attemptId), null, null),
        };
    }

    /// <summary>
    /// When the earliest earlier attempt of the item that may have acted was claimed, or null where none did. An attempt
    /// may have acted when it ended ambiguous, which is how the plugin says so and how the runtime ends one that lost its
    /// answer: a timeout, a crash, a host failure, a lease or a restart. A provider's own answer of any other class settled
    /// its attempt, and an attempt that never started ran nothing, whatever it ended as: one a restart found never started,
    /// or one the claim refused while the item was unsettled, which keeps the earlier attempt's class.
    /// </summary>
    private static DateTimeOffset? UnsettledSince(WorkItem item, Attempt attempt) =>
        item.Attempts
            .Where(earlier => earlier.Number < attempt.Number && MayHaveActed(earlier))
            .OrderBy(earlier => earlier.Number)
            .Select(earlier => (DateTimeOffset?)new DateTimeOffset(DateTime.SpecifyKind(earlier.ClaimedAt, DateTimeKind.Utc)))
            .FirstOrDefault();

    /// <summary>
    /// The provider's reference for the work item's send that the latest earlier attempt of the item that may have acted
    /// returned, or null where none did: the attempts <see cref="UnsettledSince"/> reads, newest first, and the first of
    /// them whose provenance holds a <c>send</c> — a kind the operation declares for the work item itself. A read of a
    /// send never makes it twice, so this is what a repeat reads before anything else.
    /// </summary>
    internal static string? UnsettledReference(OperationContract contract, WorkItem item, Attempt attempt) =>
        contract.ExternalIds.TryGetValue(SendKind, out var kind) && kind.Entity == PinnedEntity.WorkItem
            ? item.Attempts
                .Where(earlier => earlier.Number < attempt.Number && MayHaveActed(earlier))
                .OrderByDescending(earlier => earlier.Number)
                .Select(earlier => earlier.Provenance?.ExternalIdsReturned is { } returned && returned.TryGetValue(SendKind, out var reference) ? reference : null)
                .FirstOrDefault(reference => reference is not null)
            : null;

    /// <summary>Whether an earlier attempt may have acted: it started, and it ended ambiguous.</summary>
    private static bool MayHaveActed(Attempt earlier) => MayHaveActed(earlier.StartedAt, earlier.Error);

    /// <summary>
    /// Whether an attempt that started at <paramref name="startedAt"/>, if it did, and ended with <paramref name="error"/>
    /// may have acted: the one reading of an unsettled attempt, which check 23's hold and check 24's search in the way share.
    /// </summary>
    internal static bool MayHaveActed(DateTime? startedAt, AttemptErrorDto? error) =>
        startedAt is not null && error?.Class == FailureClass.Ambiguous;

    /// <summary>
    /// Work the runtime cannot perform fails where it is noticed, with the attempt and its snapshot kept so the
    /// manager can see what would have run. A silent skip would leave the item looking claimable forever.
    /// </summary>
    /// <remarks>
    /// For a provider operation this is the whole pre-flight: everything that can refuse the work is decided
    /// here, before a child process exists, and what comes back is either the plan the run needs or the one
    /// reason the item cannot run. Nothing falls back to another plugin, and nothing is retried but a check a
    /// schema pattern could not finish, which is transient: nothing was found wrong and nothing has run.
    /// </remarks>
    private ProviderOpPlan? FailClosed(
        JasonDbContext db,
        WorkItem item,
        Attempt attempt,
        PreflightVerdict? verdict,
        AgentLaunchDecision? agent,
        PluginSnapshot packages,
        RouteSnapshot routing,
        Approval? released)
    {
        if (agent is not null)
        {
            // Written before anything acts on it and whichever way it went, exactly as a provider decision is:
            // an attempt that never ran still says which profile would have run it, and which level chose it.
            attempt.Provenance = agent switch
            {
                AgentLaunchDecision.Ready ready => AttemptProvenance.ForAgent(ready.Plan.Provenance, attempt.PublicId),
                AgentLaunchDecision.Legacy legacy => AttemptProvenance.ForAgent(legacy.Provenance, attempt.PublicId),
                AgentLaunchDecision.Refused { Decided: { } decided } => AttemptProvenance.ForAgent(decided, attempt.PublicId),

                // Nothing was chosen at all, which is what work with unreadable ancestry means: the record says
                // so by staying empty, and the attempt's error says why.
                _ => attempt.Provenance,
            };

            if (agent is AgentLaunchDecision.Refused refused)
            {
                outcomes.Fail(
                    db,
                    item,
                    attempt,
                    refused.Code,
                    refused.Message,
                    trace: null,
                    details: null,
                    Actors.Dispatcher,

                    // Nothing about the work changed between two scans, so an item put back would be refused for
                    // the same reason for as long as the queue existed.
                    retriable: false);
                return null;
            }
        }

        if (verdict is not null)
        {
            // What was decided, recorded before anything acts on it and whichever way the decision went: an item
            // that never ran still says what would have run it, which is the half of a refusal a manager can act on.
            // Where a person released this work, the attempt names the decision it runs under.
            attempt.Provenance = AttemptProvenance.AtClaim(item.Operation, verdict, packages, routing, attempt.PublicId, released?.PublicId);
            // Work a block holds reaches here only as the block's trial: it runs, and what it answers decides the block.
            if (verdict.Passed || released is not null || verdict.Code == AttemptErrors.ProviderBlocked)
            {
                return verdict.Plan is { } plan
                    ? plan with { UnsettledSince = UnsettledSince(item, attempt), UnsettledReference = UnsettledReference(plan.Contract, item, attempt) }
                    : null;
            }

            outcomes.Fail(
                db,
                item,
                attempt,
                verdict.Code!,
                verdict.Message!,
                trace: null,
                verdict.Details,
                Actors.Dispatcher,
                failureClass: verdict.Class,

                // Said here rather than inferred from a table somewhere else: nothing about the work changed
                // between two scans, so an item released back into the queue would be refused for the same
                // reason for as long as the queue existed. A pre-flight refusal is final because the pre-flight
                // says it is — and the one answer that is not a refusal says so by its class: a check a schema
                // pattern could not finish is transient, nothing has run, and the next scan may well answer it.
                retriable: verdict.Class == FailureClass.Transient);
            return null;
        }

        if (attempt.Launch is null)
        {
            outcomes.Fail(
                db,
                item,
                attempt,
                AttemptErrors.RoleNotLaunchable,
                $"Role '{item.Role}' has no entry command and Roles:DefaultEntryCommand is not configured.",
                trace: null,
                details: null,
                Actors.Dispatcher);
        }

        return null;
    }

    /// <summary>
    /// Whether this work may run now: the decision a person has already made about exactly this subject, or
    /// nothing — in which case the item has been parked for one by the time this returns.
    /// </summary>
    /// <remarks>
    /// The comparison is between two hashes and nothing else. An approval authorizes its subject until something
    /// supersedes it, so a retriable failure runs again under the same decision, unless what the plugin would
    /// receive changed meanwhile — the operator approved an effect, and a provider timing out did not change what
    /// that effect is. What they did not approve is a different document, and that is what the hash is for: a pin
    /// the failed attempt learned is among such changes, since the input is composed with it, so the person
    /// decides once more. That holds for <c>confirm_each</c> as for
    /// <c>confirm_once</c>: the decision is this item's alone and the item is one person, so a retry after a failure
    /// that sent nothing performs the one act the person approved, and an end that may have sent is never repeated
    /// by an operation that says so.
    /// </remarks>
    private async Task<Approval?> DecideAsync(JasonDbContext db, WorkItem item, PreflightVerdict verdict, DateTime now, CancellationToken ct)
    {
        var plan = verdict.Plan!;
        var subject = ApprovalSubject.Of(plan, item);
        var live = await ApprovalGate.LiveAsync(db, item.CampaignId, item.Id, ct).ConfigureAwait(false);

        if (live is { Status: ApprovalStatus.Approved }
            && string.Equals(live.SubjectHash, subject.Hash, StringComparison.Ordinal))
        {
            return live;
        }

        if (live is { Status: ApprovalStatus.Pending })
        {
            // A question nobody has answered yet, about this item. It is left exactly as it is: asking the same
            // person the same thing twice is not an improvement, and a second pending row is one the database
            // would refuse anyway. Whether its subject still matches is not asked here: a pending row sits on an
            // item that is awaiting_approval, which is never claimed, and an edit of that item's input withdraws
            // the question in the same save that hands the item back to the queue, so the claim meets such an
            // item with no live decision and asks about what it now says. Reaching here at all means something
            // let the item back into the queue with the decision still open, and the honest answer to that is to
            // put it back where it was waiting.
            ApprovalGate.Wait(db, journal, item, now);
            return null;
        }

        var preview = ApprovalPreview.Of(
            plan.Contract,
            subject,
            item.Campaign!,
            item.Contact,
            CanonicalInput.ConsumedChannel(plan.Contract, item));

        // Only about work nobody was ever asked about: a decision a person made, or a question already put to them, is
        // theirs, and a later standing approval never answers it — not even once the question is gone, cancelled by an
        // expiry the item was then reopened from. A standing approval decides once, here, and its row then authorises a
        // retry of the same subject as a person's would.
        if (live is null
            && !await StandingApprovals.Asked(db, item.CampaignId, item.Id).AnyAsync(ct).ConfigureAwait(false)
            && await StandingApprovals.CoverAsync(db, item, plan, now, ct).ConfigureAwait(false) is { } answer)
        {
            if (answer.Cover is { } cover)
            {
                return ApprovalGate.ReleaseUnderStanding(db, journal, item, plan, subject, preview, cover, now);
            }

            // Why the standing approval did not let it go is said where the person deciding reads: in the preview, which
            // is not part of what is approved.
            preview["standing_approval"] = answer.Uncovered!.ToJson();
        }

        ApprovalGate.Park(
            db,
            journal,
            item,
            plan,
            subject,
            preview,
            live,
            live is null ? ApprovalGate.ApprovalRequired : ApprovalGate.InputChanged,
            now);
        return null;
    }

    /// <summary>
    /// Whether this item is the trial of the block that holds it: the one the block already names, or — where the
    /// block's next trial is due and no trial of it is still under way, as the resume step reads it — this one, which the
    /// block then names. One trial is in flight per block; every other item the block holds parks. The resume step runs
    /// before the claim and names an act wherever a parked one could run, so a block still due here holds no parked act
    /// that could: the item named here may be a control, and its answer decides nothing but <c>blocked</c>.
    /// </summary>
    private static async Task<bool> TrialAsync(JasonDbContext db, ProviderBlock block, WorkItem item, DateTime now, CancellationToken ct)
    {
        if (block.TrialWorkItemId == item.Id)
        {
            return true;
        }

        if (block.NextTrialAt > now)
        {
            return false;
        }

        if (block.TrialWorkItemId is { } other
            && await db.Database.SqlQueryRaw<int>(Resumer.TrialUnderWaySql, other).AnyAsync(ct).ConfigureAwait(false))
        {
            return false;
        }

        block.TrialWorkItemId = item.Id;
        return true;
    }

    /// <summary>
    /// The one write of this loop, guarded the way every claim is: another writer who changed this item first
    /// keeps their change, and this scan moves on to the next item rather than failing the whole transaction.
    /// </summary>
    private async Task<bool> SaveAsync(JasonDbContext db, WorkItem item, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            ClaimSkipped(logger, item.PublicId, null);
            db.ChangeTracker.Clear();
            return false;
        }
    }

    /// <summary>
    /// The campaign's audience for the definition the search is composed with — found by its content, or made — and the
    /// search's record of it under its context's reserved key, written and said in the chronicle only where it changes: a
    /// search claimed again after a person approved it runs under the audience it was parked under and says nothing more.
    /// It is saved with the claim, so a claim that loses its save to another writer leaves no audience behind.
    /// </summary>
    private async Task AudienceAsync(JasonDbContext db, WorkItem item, ProviderOpPlan plan, DateTime now, CancellationToken ct)
    {
        // The twentieth check held the composed definition to the routed plugin's schema, and that schema is an object's.
        var definition = (JsonObject)plan.Input[CanonicalInput.DefinitionKey]!;
        var audience = await CampaignAudiences.FindOrMakeAsync(db, item.CampaignId, definition, now, ct).ConfigureAwait(false);
        var carried = CampaignAudiences.Carried(audience, definition);
        var recorded = item.Context[CampaignAudiences.ContextKey];
        if (JsonNode.DeepEquals(recorded, carried))
        {
            return;
        }

        var context = item.Context.DeepClone().AsObject();
        context[CampaignAudiences.ContextKey] = carried;
        item.Context = context;
        journal.Append(
            db,
            Actors.Dispatcher,
            JournalKinds.WorkItemContextUpdated,
            campaign: null,
            key: CampaignAudiences.ContextKey,
            old: recorded?.DeepClone(),
            updated: carried.DeepClone(),
            workItem: item);
    }

    /// <summary>
    /// What the pre-flight decides on, read inside the claim transaction and nowhere else. The route is resolved
    /// first — a pure lookup over the snapshot the decision itself will use again — because whose identifiers to
    /// read is the routed plugin's question, and an item nothing routes needs no reads at all. An audience the work names
    /// by its number is read by that number, in the work's own campaign.
    /// </summary>
    private async Task<PreflightFacts> FactsAsync(JasonDbContext db, WorkItem item, RouteSnapshot routing, CancellationToken ct)
    {
        var campaign = item.Campaign!;
        var contract = item.Operation is { } operation ? OperationCatalog.Find(operation) : null;

        // The latest attempt this claim would repeat that may have acted: the attempts loaded with the item are all
        // earlier ones, since the claim's own does not exist yet. It is read before the route is, so a refusal for a
        // route that is gone says that attempt may have reached the person, as every later refusal does.
        var unsettled = item.Attempts
            .Where(MayHaveActed)
            .MaxBy(earlier => earlier.Number);

        // The search the work names is read before the route, since a read or a stop of it goes where it ran.
        var search = contract is not null && CanonicalInput.NamesASearch(contract)
            ? await ReferenceAsync(
                db,
                item,
                CanonicalInput.NamedSearch(item),
                SearchKind,
                (made, reference, ran) => new NamedSearch(made.PublicId, made.Operation, made.Status, reference, ran?.PluginId, ran?.BindingIdentity),
                ct).ConfigureAwait(false)
            : null;
        var resolution = contract is null
            ? null
            : ProviderOpPreflight.Resolve(contract, new PreflightFacts(item, campaign, item.Contact, PreflightFacts.NoPins, PreflightFacts.NoPins, Suppressed: false, Search: search), routing);
        if (contract is null || resolution is null)
        {
            return new PreflightFacts(item, campaign, item.Contact, PreflightFacts.NoPins, PreflightFacts.NoPins, Suppressed: false, Unsettled: unsettled);
        }

        var pluginId = resolution.Route.PluginId;
        var campaignPins = await PinsAsync(db, campaign, pluginId, ct).ConfigureAwait(false);

        // The open blocks of the route's plugin and binding, by their index, and only for work a block holds.
        var blocks = OperationCatalog.HeldByProviderBlock.Contains(contract.Id)
            ? await ProviderBlocks.OpenAsync(db, pluginId, ProviderBlocks.Identity(resolution.Route.BindingIdentity), ct).ConfigureAwait(false)
            : null;
        var send = CanonicalInput.NamesASend(contract)
            ? await ReferenceAsync(
                db,
                item,
                CanonicalInput.NamedSend(item),
                SendKind,
                (made, reference, ran) => new NamedSend(made.PublicId, made.Operation, made.Status, reference, ran?.PluginId, ran?.BindingIdentity),
                ct).ConfigureAwait(false)
            : null;

        // Where a status read starts is the search's own row's: the cursor the last read answered, as the row keeps it, and
        // null before any read answered one. The row is read in the work's own campaign, as the search is.
        if (search is not null)
        {
            search = search with
            {
                Cursor = await AudienceSearches.Named(db, item.CampaignId, search.WorkItem)
                    .AsNoTracking()
                    .Select(row => row.Cursor)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false),
            };
        }

        var inTheWay = string.Equals(contract.Id, SearchLanding.Search, StringComparison.Ordinal)
            ? await AudienceSearches.InTheWayAsync(db, campaign.Id, item.Id, unsettled is not null, ct).ConfigureAwait(false)
            : null;

        var audience = CanonicalInput.CarriesADefinition(contract) && CanonicalInput.NamedAudience(item) is { } number
            && await CampaignAudiences.ByNumber(db, campaign.Id, number).AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false) is { } held
            ? new NamedAudience(held.Number, held.Definition)
            : null;

        if (item.Contact is not { } contact)
        {
            return new PreflightFacts(item, campaign, null, PreflightFacts.NoPins, campaignPins, Suppressed: false, blocks, unsettled, send, Learned([], campaign.ExternalIds, pluginId, item, unsettled), search, audience, inTheWay);
        }

        if (contract.Preflight.Channel == ChannelRequirement.FromArgs)
        {
            // How the person is reachable, read once: the composed input carries the one channel the operation
            // consumes, and the register is asked about that same value.
            await db.Entry(contact).Collection(person => person.Channels).LoadAsync(ct).ConfigureAwait(false);
        }

        return new PreflightFacts(
            item,
            campaign,
            contact,
            await PinsAsync(db, contact, pluginId, ct).ConfigureAwait(false),
            campaignPins,
            await SuppressedAsync(db, contract, item, contact, ct).ConfigureAwait(false),
            blocks,
            unsettled,
            send,
            Learned(contact.ExternalIds, campaign.ExternalIds, pluginId, item, unsettled),
            search,
            audience,
            inTheWay);
    }

    /// <summary>
    /// The kinds of the routed plugin's pins that the attempt this claim would repeat, or any later attempt of the item,
    /// recorded, taken from the pins already loaded, or null where no attempt is repeated: an ambiguous answer may name the
    /// person it reached, and what the attempt learned so was not in the input its claim composed. Every later attempt ran
    /// only because its claim found it the same request, so what it learned is the provider's answer to that request too.
    /// </summary>
    private static PinsLearned? Learned(IEnumerable<ExternalId> contactPins, IEnumerable<ExternalId> campaignPins, string pluginId, WorkItem item, Attempt? unsettled)
    {
        if (unsettled is null)
        {
            return null;
        }

        var since = item.Attempts
            .Where(attempt => attempt.Number >= unsettled.Number)
            .Select(attempt => attempt.PublicId)
            .ToHashSet(StringComparer.Ordinal);
        return new PinsLearned(By(contactPins), By(campaignPins));

        IReadOnlySet<string> By(IEnumerable<ExternalId> pins) => pins
            .Where(pin => string.Equals(pin.PluginId, pluginId, StringComparison.Ordinal)
                && since.Contains(pin.RecordedByAttemptId))
            .Select(pin => pin.Kind)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The work the arguments name by its id — a send under <c>args.send</c>, a search under <c>args.search</c> — read only in
    /// the work's own campaign: another campaign's work is not this one's to name. What it kept of where it went is its
    /// succeeded attempt's, the one that returned the reference under <paramref name="kind"/>.
    /// </summary>
    private static async Task<T?> ReferenceAsync<T>(
        JasonDbContext db,
        WorkItem item,
        string? named,
        string kind,
        Func<WorkItem, string?, AttemptProvenanceDto?, T> found,
        CancellationToken ct)
        where T : NamedReference
    {
        if (named is null)
        {
            return null;
        }

        var made = await db.WorkItems
            .AsNoTracking()
            .Include(candidate => candidate.Attempts.Where(attempt => attempt.Status == AttemptStatus.Succeeded))
            .SingleOrDefaultAsync(candidate => candidate.PublicId == named && candidate.CampaignId == item.CampaignId, ct)
            .ConfigureAwait(false);
        if (made is null)
        {
            return null;
        }

        var ran = made.Attempts.MaxBy(attempt => attempt.Number)?.Provenance;
        return found(
            made,
            ran?.ExternalIdsReturned is { } returned && returned.TryGetValue(kind, out var reference) ? reference : null,
            ran);
    }

    /// <summary>
    /// One indexed read, and the projection is then taken from the loaded navigation: the store does no I/O of
    /// its own, so an entity whose pins were never brought in would look like an entity nobody has pinned. Only
    /// the routed plugin's rows are read — the index is on exactly that pair, and what another provider calls
    /// somebody is never this one's business.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> PinsAsync(JasonDbContext db, Campaign campaign, string pluginId, CancellationToken ct)
    {
        await db.Entry(campaign).Collection(entity => entity.ExternalIds).Query()
            .Where(pin => pin.PluginId == pluginId)
            .LoadAsync(ct)
            .ConfigureAwait(false);

        return identifiers.PinsFor(campaign, pluginId);
    }

    /// <summary>The same for the person the item is about.</summary>
    private async Task<IReadOnlyDictionary<string, string>> PinsAsync(JasonDbContext db, Contact contact, string pluginId, CancellationToken ct)
    {
        await db.Entry(contact).Collection(person => person.ExternalIds).Query()
            .Where(pin => pin.PluginId == pluginId)
            .LoadAsync(ct)
            .ConfigureAwait(false);

        return identifiers.PinsFor(contact, pluginId);
    }

    /// <summary>
    /// The one question the register is asked, and only where the operation names a channel to ask about and
    /// declares that the register applies to it — a read whose answer nothing uses is not made. Both a stored
    /// channel value and a suppression are already normalized, so they are compared as they stand.
    /// </summary>
    private async Task<bool> SuppressedAsync(JasonDbContext db, OperationContract contract, WorkItem item, Contact contact, CancellationToken ct) =>
        contract.Preflight.Suppression == SuppressionCheck.Checked
        && CanonicalInput.ConsumedChannel(contract, item) is { } channel
        && CanonicalInput.Reachable(contact, channel) is { } reachable
        && await suppression.IsSuppressedAsync(db, channel, reachable.Value, ct).ConfigureAwait(false);
}
