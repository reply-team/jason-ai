using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Runtime.Approvals;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// The third step of a scan, and the only one that hands work out. Everything happens inside one immediate
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
    PluginRegistry plugins,
    RouteRegistry routes,
    ExternalIdStore identifiers,
    ISuppressionCheck suppression,
    LiveSettings<DispatcherOptions> settings,
    JasonPaths paths,
    ILogger<Claimer> logger)
{
    private static readonly Action<ILogger, string, Exception?> ClaimSkipped = LoggerMessage.Define<string>(
        LogLevel.Information,
        new EventId(1, nameof(ClaimSkipped)),
        "Claim skipped: another writer changed work item {WorkItemId} first");

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

        // Both snapshots are read once, so every item this scan hands out was decided against one pair of them
        // and an activation that happens mid-scan belongs to the next one.
        var packages = plugins.Snapshot;
        var routing = routes.Snapshot;

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
            var verdict = item.Kind == WorkItemKind.ProviderOp
                ? ProviderOpPreflight.Check(await FactsAsync(db, item, routing, ct).ConfigureAwait(false), packages, routing)
                : null;

            // The same question for agent work, asked in the same place and with the same rule: everything that
            // can refuse it is decided before a child exists, and the answer is one reason the attempt keeps.
            var agent = item.Kind == WorkItemKind.AiRole
                ? await agents.PlanAsync(db, item, ct).ConfigureAwait(false)
                : null;

            var released = verdict is { Parks: true }
                ? await DecideAsync(db, item, verdict, now, ct).ConfigureAwait(false)
                : null;
            if (verdict is { Parks: true } && released is null)
            {
                // Parked: nothing is handed out and no attempt exists to hand out. The save is the same guarded
                // one the claim itself uses, so a caller who cancelled this item a moment ago still wins.
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

            if (!await SaveAsync(db, item, ct).ConfigureAwait(false))
            {
                continue;
            }

            if (item.Status == WorkItemStatus.Scheduled)
            {
                claimed.Add(new ClaimedWork(
                    item.Id,
                    attempt.Id,
                    item.PublicId,
                    attempt.PublicId,
                    plan,
                    agent is AgentLaunchDecision.Ready ready ? new AgentLaunch(ready.Plan.Deny, ready.Plan.CliCommand) : null));
            }
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
        return claimed;
    }

    /// <summary>
    /// One item per campaign, the most important first, and never more than the free slots. Raw SQL because the
    /// per-campaign choice is a window function: doing it in memory would either idle a slot or read the whole
    /// backlog.
    /// </summary>
    private static async Task<List<int>> CandidatesAsync(JasonDbContext db, DateTime now, int freeSlots, CancellationToken ct)
    {
        var created = SnakeCaseEnumConverter<WorkItemStatus>.Format(WorkItemStatus.Created);
        var scheduled = SnakeCaseEnumConverter<WorkItemStatus>.Format(WorkItemStatus.Scheduled);
        var processing = SnakeCaseEnumConverter<WorkItemStatus>.Format(WorkItemStatus.Processing);
        var active = SnakeCaseEnumConverter<CampaignStatus>.Format(CampaignStatus.Active);

        return await db.WorkItems.FromSqlInterpolated($"""
            SELECT * FROM (
              SELECT w.*, ROW_NUMBER() OVER (PARTITION BY w.campaign_id ORDER BY w.priority DESC, w.id ASC) AS rn
              FROM work_items w JOIN campaigns c ON c.id = w.campaign_id
              WHERE w.status = {created} AND c.status = {active}
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
    /// Work the runtime cannot perform fails where it is noticed, with the attempt and its snapshot kept so the
    /// manager can see what would have run. A silent skip would leave the item looking claimable forever.
    /// </summary>
    /// <remarks>
    /// For a provider operation this is the whole pre-flight: everything that can refuse the work is decided
    /// here, before a child process exists, and what comes back is either the plan the run needs or the one
    /// reason the item cannot run. Nothing is retried and nothing falls back to another plugin.
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
            if (verdict.Passed || released is not null)
            {
                return verdict.Plan;
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
                // says it is.
                retriable: false);
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
    /// supersedes it, so a retriable failure runs again under the same decision — the operator approved an
    /// effect, and a provider timing out did not change what that effect is. What they did not approve is a
    /// different document, and that is what the hash is for.
    /// </remarks>
    private async Task<Approval?> DecideAsync(JasonDbContext db, WorkItem item, PreflightVerdict verdict, DateTime now, CancellationToken ct)
    {
        var plan = verdict.Plan!;
        var subject = ApprovalSubject.Of(plan, item);
        var live = await ApprovalGate.LiveAsync(db, item.Id, ct).ConfigureAwait(false);

        if (live is { Status: ApprovalStatus.Approved }
            && string.Equals(live.SubjectHash, subject.Hash, StringComparison.Ordinal))
        {
            return live;
        }

        if (live is { Status: ApprovalStatus.Pending })
        {
            // A question nobody has answered yet, about this item. It is left exactly as it is: asking the same
            // person the same thing twice is not an improvement, and a second pending row is one the database
            // would refuse anyway. Whether its subject still matches is not asked here, because it cannot have
            // stopped matching — a pending row sits on an item that is awaiting_approval, and such an item is
            // never claimed, so nothing could have edited the work since. Reaching here at all means something
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
    /// What the pre-flight decides on, read inside the claim transaction and nowhere else. The route is resolved
    /// first — a pure lookup over the snapshot the decision itself will use again — because whose identifiers to
    /// read is the routed plugin's question, and an item nothing routes needs no reads at all.
    /// </summary>
    private async Task<PreflightFacts> FactsAsync(JasonDbContext db, WorkItem item, RouteSnapshot routing, CancellationToken ct)
    {
        var campaign = item.Campaign!;
        var contract = item.Operation is { } operation ? OperationCatalog.Find(operation) : null;
        var resolution = contract is null ? null : RouteResolver.Resolve(routing, campaign.PublicId, contract.Id);
        if (contract is null || resolution is null)
        {
            return new PreflightFacts(item, campaign, item.Contact, PreflightFacts.NoPins, PreflightFacts.NoPins, Suppressed: false);
        }

        var pluginId = resolution.Route.PluginId;
        var campaignPins = await PinsAsync(db, campaign, pluginId, ct).ConfigureAwait(false);
        if (item.Contact is not { } contact)
        {
            return new PreflightFacts(item, campaign, null, PreflightFacts.NoPins, campaignPins, Suppressed: false);
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
            await SuppressedAsync(db, contract, item, contact, ct).ConfigureAwait(false));
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
    /// The one question the register is asked, and only where the operation names a channel to ask about: both
    /// a stored channel value and a suppression are already normalized, so they are compared as they stand.
    /// </summary>
    private async Task<bool> SuppressedAsync(JasonDbContext db, OperationContract contract, WorkItem item, Contact contact, CancellationToken ct) =>
        CanonicalInput.ConsumedChannel(contract, item) is { } channel
        && CanonicalInput.Reachable(contact, channel) is { } reachable
        && await suppression.IsSuppressedAsync(db, channel, reachable.Value, ct).ConfigureAwait(false);
}
