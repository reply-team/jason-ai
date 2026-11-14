using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Contracts.Operations;
using Jason.Runtime.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Approvals;
using Jason.Runtime.Audiences;
using Jason.Runtime.Configuration;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// Work items as callers create, read and list them. The service owns what a caller may ask for; whether an
/// item may run, and what happens when it does, belongs to the dispatcher and to
/// <see cref="WorkItemTransitions"/>.
/// </summary>
/// <param name="plugins">
/// Read for one number: the grace a child is given to stop, which together with an operation's own budget is the
/// shortest lease a provider item may be run under. It is read at each request rather than captured, so an
/// edited settings file governs the next item written — through the seam, so that an edit the validator refuses
/// costs the edit rather than every creation until somebody notices.
/// </param>
/// <param name="timing">
/// How a provider operation's arguments are matched against its patterns: the runtime's one default, which a test
/// replaces to make a match time out without making anything slow.
/// </param>
/// <param name="outcomes">
/// Writes the wait of an item created waiting on others (<c>wait_on</c>). Optional only for the tests that build this
/// service by hand and create nothing waiting; the runtime always supplies it.
/// </param>
/// <param name="run">
/// The attempt the request says it comes from: inside a run, a send a review covers is created under that attempt's own
/// name. Null outside a request and for a request that names none.
/// </param>
public sealed class WorkItemService(
    JasonDbContext db,
    JournalWriter journal,
    TimeProvider clock,
    WorkItemCanceller canceller,
    LiveSettings<PluginsOptions> plugins,
    PatternTiming? timing = null,
    AttemptOutcomes? outcomes = null,
    RequestRun? run = null)
{
    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    /// <summary>
    /// The one context key a provider operation's arguments live under. Everything else in a work item's context
    /// is the planner's own and never reaches a plugin: what an operation takes is a typed input, not a note.
    /// </summary>
    public const string InputKey = "input";

    public const int MaxRoleLength = 64;

    public const int MaxOperationLength = 200;

    public const int MaxExecutionProfileLength = 100;

    public const int MaxResultFormatBytes = 16 * 1024;

    /// <summary>
    /// The one number this service reads out of the plugins section. A file edited into something the validator
    /// refuses leaves the last value that validated in force; a runtime that has never read one has nothing to
    /// answer with, and says so rather than failing as though the request were at fault.
    /// </summary>
    private int KillGrace() =>
        plugins.TryCurrent(out var options)
            ? options.Invoker.KillGraceMs
            : throw DomainErrors.SettingsUnreadable(PluginsOptions.Section);

    public async Task<WorkItemDto> CreateAsync(WorkItemCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        // Inside a run, a send a review covers is the run's own: made under another name, a run could pass its own text
        // as a reviewer who did not write it. Asked before anything else, a keyed repeat included.
        if (run?.AttemptId is { } header
            && request.Kind == WorkItemKind.ProviderOp
            && ReviewSubject.IsReviewable(Trimmed(request.Operation))
            && !(actor.Type == ActorType.Attempt && string.Equals(actor.Id?.Trim(), header, StringComparison.Ordinal)))
        {
            throw DomainErrors.CreatedAsAnotherInsideARun(header);
        }

        if (request.IdempotencyKey is null)
        {
            var item = await CreateCoreAsync(request, actor, lineage: null, cancellationToken).ConfigureAwait(false);
            return await CreatedAsync(item, cancellationToken).ConfigureAwait(false);
        }

        // A key is looked for after the request's own shape is checked — everything it says that no state can change —
        // and before anything that reads today's state: a repeat of a create that succeeded answers with what it made,
        // whatever has happened to the campaign, its people or the settings since. The checks are the ordinary ones,
        // handed answers that refuse nothing that depends on state.
        var errors = new ValidationErrors();
        WorkItemValidation.ValidateCreate(request, roleExists: true, profileExists: true, killGraceMs: 0, _timing, errors);
        var key = CreationKey.Read(request.IdempotencyKey, errors);
        errors.ThrowIfAny();

        // The context's size is part of the request's shape too, and it is measured before the request is named: a
        // document past the cap is refused for what it is, and is never read whole to be hashed.
        ContextRules.EnsureWithinLimits(request.Context ?? []);

        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        var digest = CreationKey.Digest(request);
        if (await MadeUnderAsync(campaign.Id, key!, digest, cancellationToken).ConfigureAwait(false) is { } made)
        {
            return made;
        }

        try
        {
            var item = await CreateCoreAsync(request with { IdempotencyKey = key }, actor, lineage: null, cancellationToken).ConfigureAwait(false);
            return await CreatedAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Somebody else's create under the same key landed between the look above and this insert, and the
            // database decided between them. Theirs is answered as a repeat would be; a refusal over anything else
            // was never a race, and is what it was.
            db.ChangeTracker.Clear();
            if (await MadeUnderAsync(campaign.Id, key!, digest, cancellationToken).ConfigureAwait(false) is { } won)
            {
                return won;
            }

            throw;
        }
    }

    /// <summary>The create's answer: the item as written, with what it waits on where it was created waiting.</summary>
    private async Task<WorkItemDto> CreatedAsync(WorkItem item, CancellationToken cancellationToken)
    {
        var dto = WorkItemMapper.ToDto(item, item.CreatedAt, [], includeSnapshots: false);
        if (item.Status != WorkItemStatus.Waiting)
        {
            return dto;
        }

        var waits = await WaitBlocks.ForAsync(db, [item.Id], cancellationToken).ConfigureAwait(false);
        return dto with { Wait = waits.GetValueOrDefault(item.Id) };
    }

    /// <summary>
    /// The item a key already names in this campaign, as it is now, when <paramref name="digest"/> is the request that
    /// made it; a refusal naming it when a different request did; null when the key names nothing yet.
    /// </summary>
    private async Task<WorkItemDto?> MadeUnderAsync(int campaignId, string key, string digest, CancellationToken cancellationToken)
    {
        var made = await db.WorkItems
            .AsNoTracking()
            .WithNavigation()
            .Include(w => w.Attempts)
            .FirstOrDefaultAsync(w => w.CampaignId == campaignId && w.IdempotencyKey == key, cancellationToken)
            .ConfigureAwait(false);

        if (made is null)
        {
            return null;
        }

        if (!string.Equals(made.IdempotencyDigest, digest, StringComparison.Ordinal))
        {
            throw DomainErrors.IdempotencyKeyReused(made.PublicId);
        }

        // The create's own answer, about the item as it is now — its attempts included, and whether it is eligible read
        // at this moment rather than at the one it was created in.
        return WorkItemMapper.ToDto(made, clock.GetUtcNow().UtcDateTime, made.Attempts, includeSnapshots: false);
    }

    /// <summary>
    /// How a work item comes into existence, for every caller there is. The runtime creates work of its own —
    /// a manager's check-in — and it goes through this routine rather than beside it, so that validation, the
    /// chronicle line, what the row is computed from and everything added here later reach that work without
    /// anybody remembering to copy them.
    /// </summary>
    /// <param name="lineage">
    /// Supplied when the causing run is not the creating actor. The dispatcher creates a check-in as itself,
    /// but the chain that check-in belongs to is the chain of the thing it is about — so lineage is read from
    /// the cause and handed in, and the actor stays the truth about who made the row.
    /// </param>
    internal async Task<WorkItem> CreateCoreAsync(
        WorkItemCreateRequest request,
        ActorRef actor,
        LineageRecord? lineage,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        WorkItemValidation.ValidateCreate(
            request,
            await RoleExistsAsync(request, cancellationToken).ConfigureAwait(false),
            await ProfileExistsAsync(request.ExecutionProfile, cancellationToken).ConfigureAwait(false),
            KillGrace(),
            _timing,
            errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign.Status == CampaignStatus.Archived && !OperationCatalog.RunsInArchivedCampaign.Contains(Trimmed(request.Operation) ?? string.Empty, StringComparer.Ordinal))
        {
            throw DomainErrors.CampaignArchived(campaign.PublicId);
        }

        var contact = await LoadMemberAsync(campaign, request.ContactId, cancellationToken).ConfigureAwait(false);
        var awaited = await AwaitedAsync(campaign, request.WaitOn, cancellationToken).ConfigureAwait(false);
        var context = request.Context?.DeepClone().AsObject() ?? new JsonObject();
        ContextRules.EnsureWithinLimits(context);
        await StandingApprovals.ValidateReferenceAsync(
            db, request.Kind!.Value, Trimmed(request.Operation), campaign.Id, contact?.PublicId, context, errors, cancellationToken).ConfigureAwait(false);
        errors.ThrowIfAny();

        // Read once, here, for work of every kind: what the run that asked for this hands down. After this line
        // it is a fact about the row, and nothing recomputes it.
        var record = lineage ?? await Lineage.ForCreationAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var now = clock.GetUtcNow().UtcDateTime;
        var item = Compose(request, actor, record, campaign, contact, context, now);

        // Written waiting, in the create's own save: the item is never claimable before what it waits on has ended. The
        // wait only orders — once it is over the item is ordinary queued work, and the claim decides about it as ever.
        if (awaited.Count > 0)
        {
            var writer = outcomes ?? throw new InvalidOperationException("This service was built without the outcomes that write a wait, and a create names work to wait on.");
            writer.WaitFromCreation(db, item, awaited, actor);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return item;
    }

    /// <summary>
    /// Builds one new item and its <c>workitem_created</c> line, and adds both without saving, so one create can write the
    /// work it was asked for and the work the runtime adds to it in a single save: both or neither.
    /// </summary>
    private WorkItem Compose(
        WorkItemCreateRequest request,
        ActorRef actor,
        LineageRecord record,
        Campaign campaign,
        Contact? contact,
        JsonObject context,
        DateTime now)
    {
        var item = new WorkItem
        {
            PublicId = PublicId.New("wi"),
            Campaign = campaign,
            Contact = contact,
            Kind = request.Kind!.Value,
            Role = Trimmed(request.Role),
            Operation = Trimmed(request.Operation),
            ExecutionProfile = Trimmed(request.ExecutionProfile),
            LineageState = record.State,
            LineageProfileName = record.ProfileName,
            LineageProfileRevision = record.ProfileRevision,
            LineageFromAttemptId = record.FromAttemptId,
            Status = WorkItemStatus.Created,
            Priority = request.Priority ?? 0,
            NotBefore = request.NotBefore?.UtcDateTime,
            DueAt = request.DueAt?.UtcDateTime,
            TimeoutSeconds = request.TimeoutSeconds,
            HeartbeatSeconds = request.HeartbeatSeconds,
            MaxAttempts = request.MaxAttempts,
            CreatedByType = actor.Type,
            CreatedById = actor.Id,
            Context = context,
            ResultFormat = request.ResultFormat?.DeepClone(),
            CreatedAt = now,
            UpdatedAt = now,

            // Only a caller's create carries a key, already read; the runtime's own work never does.
            IdempotencyKey = request.IdempotencyKey,
            IdempotencyDigest = request.IdempotencyKey is null ? null : CreationKey.Digest(request),

            // The creator's sentence beside its ask_person, already read as one line; outside the context, so no line and
            // no attempt's snapshot ever carries it.
            AskPersonNote = string.IsNullOrWhiteSpace(request.AskPersonNote) ? null : request.AskPersonNote.Trim(),
        };

        db.WorkItems.Add(item);
        journal.Append(
            db,
            actor,
            JournalKinds.WorkItemCreated,
            campaign,
            key: "kind",
            updated: CreationSummary(item, contact),
            reason: NormalizeReason(request.Reason),
            workItem: item);
        return item;
    }

    /// <summary>
    /// The items a create waits on, each an item of the same campaign, in the order named; none where it names none. The
    /// shape — how many, each once, none blank — was read with the request.
    /// </summary>
    private async Task<List<WorkItem>> AwaitedAsync(Campaign campaign, IReadOnlyList<string>? waitOn, CancellationToken cancellationToken)
    {
        if (waitOn is not { Count: > 0 })
        {
            return [];
        }

        var ids = waitOn.Select(id => id.Trim()).ToList();
        var found = await db.WorkItems
            .Where(w => ids.Contains(w.PublicId) && w.CampaignId == campaign.Id)
            .ToDictionaryAsync(w => w.PublicId, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var errors = new ValidationErrors();
        for (var index = 0; index < ids.Count; index++)
        {
            if (!found.ContainsKey(ids[index]))
            {
                errors.Add(
                    string.Create(CultureInfo.InvariantCulture, $"wait_on[{index}]"),
                    "unknown",
                    "wait_on names a work item of the same campaign by its id, as wi_....");
            }
        }

        errors.ThrowIfAny();
        return [.. ids.Select(id => found[id])];
    }

    public async Task<WorkItemDto> GetAsync(WorkItemGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = RequireId(request.WorkItemId);
        var item = await db.WorkItems
            .AsNoTracking()
            .WithNavigation()
            .Include(w => w.Attempts)
            .FirstOrDefaultAsync(w => w.PublicId == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw DomainErrors.WorkItemNotFound(id);

        var dto = WorkItemMapper.ToDto(
            item,
            clock.GetUtcNow().UtcDateTime,
            item.Attempts,
            request.IncludeSnapshots == true,
            await ExternalReportsAsync(item.Id, cancellationToken).ConfigureAwait(false));
        if (item.Status != WorkItemStatus.Waiting)
        {
            return dto;
        }

        var waits = await WaitBlocks.ForAsync(db, [item.Id], cancellationToken).ConfigureAwait(false);
        return dto with { Wait = waits.GetValueOrDefault(item.Id) };
    }

    /// <summary>
    /// What somebody says they did about this item outside Jason. Read here and nowhere else in this service: a
    /// listing that carried twenty assertions per row would be paying for the one view that actually asks the
    /// question. Past the cap the report listing has the rest, which is why the cap is a constant rather than a
    /// page: a work item's view is not a place to paginate from.
    /// </summary>
    private Task<List<Report>> ExternalReportsAsync(int workItemId, CancellationToken cancellationToken) =>
        db.Reports
            .AsNoTracking()
            .Include(r => r.Campaign)
            .Include(r => r.Contact)
            .Include(r => r.WorkItem)
            .Where(r => r.WorkItemId == workItemId)

            // Two reports admitted inside one tick of the clock fall back on the order they were written in, so
            // newest-first stays true at whatever resolution the clock happens to have.
            .OrderByDescending(r => r.ReceivedAt)
            .ThenByDescending(r => r.Id)
            .Take(ReportService.MaxReportsOnWorkItem)
            .ToListAsync(cancellationToken);

    public async Task<WorkItemCountResponse> CountAsync(WorkItemCountRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.GetUtcNow().UtcDateTime;
        var query = await WorkItemListing.FilterAsync(db, WorkItemFilter.From(request), now, cancellationToken).ConfigureAwait(false);
        return new WorkItemCountResponse(await query.CountAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<Page<WorkItemSummaryDto>> ListAsync(WorkItemListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);
        var now = clock.GetUtcNow().UtcDateTime;
        var filter = WorkItemFilter.From(request);

        var query = await WorkItemListing.FilterAsync(db, filter, now, cancellationToken).ConfigureAwait(false);
        query = WorkItemListing.After(query, filter.Order, after);

        // Only the live attempt is fetched: a summary carries the current attempt id and nothing else about the
        // history, and a listing must not drag ten contexts per row along with it.
        var fetched = await WorkItemListing.Order(query, filter.Order)
            .AsNoTracking()
            .WithNavigation()
            .Include(w => w.Attempts.Where(a => a.Status == AttemptStatus.Scheduled || a.Status == AttemptStatus.Running))
            .Take(limit + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // What a waiting row waits for is read for the whole page at once, one query, and without the items it names.
        var waits = await WaitBlocks.SummariesForAsync(
            db,
            [.. fetched.Where(w => w.Status == WorkItemStatus.Waiting).Select(w => w.Id)],
            cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(
            fetched,
            limit,
            w => WorkItemListing.CursorKey(filter.Order, w),
            w => WorkItemMapper.ToSummary(w, now) with { Wait = waits.GetValueOrDefault(w.Id) });
    }

    /// <summary>
    /// A partial patch of what a caller owns: the window, the priority, the per-item limits, the execution
    /// profile, the result format and the context — every one of them a statement about how the work runs. What
    /// the item is — its campaign, contact, kind, role or operation — is not patchable; work that should be
    /// something else is new work.
    /// </summary>
    public Task<WorkItemDto> UpdateAsync(WorkItemUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return UpdateCoreAsync(request, Actors.Resolve(request.Actor), cancellationToken);
    }

    /// <summary>
    /// The patch, by an actor the runtime supplies rather than one the request names: the respond step records a further
    /// move of a thread on its own open responder as the dispatcher, through the path every patch takes.
    /// </summary>
    internal async Task<WorkItemDto> UpdateCoreAsync(WorkItemUpdateRequest request, ActorRef actor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        WorkItemValidation.ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var item = await LoadAsync(db, request.WorkItemId, cancellationToken).ConfigureAwait(false);

        // The same door as at create: inside a run, whoever writes a send's text is the run, since a review is told apart
        // from every author of the text by the name each wrote it under.
        if (run?.AttemptId is { } header
            && ReviewSubject.IsReviewable(item)
            && (request.Set?.ContainsKey(InputKey) == true || request.Unset?.Contains(InputKey, StringComparer.Ordinal) == true)
            && !(actor.Type == ActorType.Attempt && string.Equals(actor.Id?.Trim(), header, StringComparison.Ordinal)))
        {
            throw DomainErrors.UpdatedAsAnotherInsideARun(header);
        }

        if (WorkItemTransitions.Final.Contains(item.Status))
        {
            throw DomainErrors.WorkItemTerminal(item.PublicId, item.Status);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var notBefore = request.NotBefore.IsSet ? request.NotBefore.Value?.UtcDateTime : item.NotBefore;
        var dueAt = request.DueAt.IsSet ? request.DueAt.Value?.UtcDateTime : item.DueAt;
        var timeoutSeconds = request.TimeoutSeconds.IsSet ? request.TimeoutSeconds.Value : item.TimeoutSeconds;
        var heartbeatSeconds = request.HeartbeatSeconds.IsSet ? request.HeartbeatSeconds.Value : item.HeartbeatSeconds;
        var maxAttempts = request.MaxAttempts.IsSet ? request.MaxAttempts.Value : item.MaxAttempts;
        var executionProfile = request.ExecutionProfile.IsSet ? Trimmed(request.ExecutionProfile.Value) : item.ExecutionProfile;

        WorkItemValidation.ValidateOverrides(timeoutSeconds, heartbeatSeconds, maxAttempts, errors);
        WorkItemValidation.ValidateWindow(notBefore, dueAt, errors);

        // Read against the registry exactly as at create, and only where the patch names the field: an item
        // whose profile was removed from the registry afterwards stays repairable rather than having every
        // unrelated patch refused along with it.
        if (request.ExecutionProfile.IsSet)
        {
            WorkItemValidation.ValidateExecutionProfile(
                executionProfile,
                await ProfileExistsAsync(executionProfile, cancellationToken).ConfigureAwait(false),
                errors);
        }

        // The same mistake arriving later. Only a patch that names the lease is measured: an item written before
        // this rule existed is left repairable rather than having every unrelated patch refused along with it.
        if (request.TimeoutSeconds.IsSet && item.Kind == WorkItemKind.ProviderOp && item.Operation is { } operation)
        {
            WorkItemValidation.ValidateProviderTimeout(operation, timeoutSeconds, KillGrace(), errors);
        }

        if (request.ResultFormat.IsSet)
        {
            if (item.Kind == WorkItemKind.ProviderOp)
            {
                errors.Add("result_format", "not_allowed", "result_format belongs to ai_role work; a provider operation answers in its own shape.");
            }
            else
            {
                WorkItemValidation.ValidateResultFormat(request.ResultFormat.Value, _timing, errors);
            }
        }

        // Reopening is the only thing a patch can do to an expired item's status, and a deadline that has already
        // passed would expire it again on the next scan.
        if (item.Status == WorkItemStatus.Expired && request.DueAt.IsSet && (dueAt is null || dueAt <= now))
        {
            errors.Add("due_at", "in_the_past", "due_at must be a future moment to return an expired item to the queue.");
        }

        ValidateContextKeys(request.Set, request.Unset, errors);
        Doubts.RefuseInPatch(request.Set, request.Unset, errors);
        SendReview.RefuseInPatch(request.Set, request.Unset, errors);
        ValidateProviderInput(item, request.Set, request.Unset, _timing, errors);
        CampaignAudiences.RefuseWritten(
            item.Operation,
            request.Set?.ContainsKey(CampaignAudiences.ContextKey) == true || request.Unset?.Contains(CampaignAudiences.ContextKey, StringComparer.Ordinal) == true,
            errors);
        await ValidateVerdictReferenceAsync(item, request.Set, request.Unset, errors, cancellationToken).ConfigureAwait(false);
        errors.ThrowIfAny();

        var changes = new List<FieldChange>();
        if (request.NotBefore.IsSet && item.NotBefore != notBefore)
        {
            changes.Add(new FieldChange("not_before", Moment(item.NotBefore), Moment(notBefore)));
            item.NotBefore = notBefore;
        }

        if (request.DueAt.IsSet && item.DueAt != dueAt)
        {
            changes.Add(new FieldChange("due_at", Moment(item.DueAt), Moment(dueAt)));
            item.DueAt = dueAt;
        }

        if (request.Priority.IsSet && item.Priority != request.Priority.Value)
        {
            changes.Add(new FieldChange("priority", JsonValue.Create(item.Priority), JsonValue.Create(request.Priority.Value)));
            item.Priority = request.Priority.Value;
        }

        if (request.TimeoutSeconds.IsSet && item.TimeoutSeconds != timeoutSeconds)
        {
            changes.Add(new FieldChange("timeout_seconds", Number(item.TimeoutSeconds), Number(timeoutSeconds)));
            item.TimeoutSeconds = timeoutSeconds;
        }

        if (request.HeartbeatSeconds.IsSet && item.HeartbeatSeconds != heartbeatSeconds)
        {
            changes.Add(new FieldChange("heartbeat_seconds", Number(item.HeartbeatSeconds), Number(heartbeatSeconds)));
            item.HeartbeatSeconds = heartbeatSeconds;
        }

        if (request.MaxAttempts.IsSet && item.MaxAttempts != maxAttempts)
        {
            changes.Add(new FieldChange("max_attempts", Number(item.MaxAttempts), Number(maxAttempts)));
            item.MaxAttempts = maxAttempts;
        }

        if (request.ExecutionProfile.IsSet && !string.Equals(item.ExecutionProfile, executionProfile, StringComparison.Ordinal))
        {
            changes.Add(new FieldChange("execution_profile", Text(item.ExecutionProfile), Text(executionProfile)));
            item.ExecutionProfile = executionProfile;
        }

        if (request.ResultFormat.IsSet && !JsonNode.DeepEquals(item.ResultFormat, request.ResultFormat.Value))
        {
            changes.Add(new FieldChange("result_format", item.ResultFormat?.DeepClone(), request.ResultFormat.Value?.DeepClone()));
            item.ResultFormat = request.ResultFormat.Value?.DeepClone();
        }

        // The arguments as stored before the patch, against the arguments as it will store them: what a parked item's
        // question was about changed only where these two differ, however many keys the patch named on the way.
        var inputBefore = item.Context[InputKey]?.DeepClone();
        var contextChanges = ApplyContext(item, request.Set, request.Unset);
        var inputChanged = !JsonNode.DeepEquals(inputBefore, item.Context[InputKey]);

        // An edit of the input in the desktop's chat makes the send the chat's, as a create there does, and for good: marked
        // once, the first time, by one line of its own beside the edit's.
        if (inputChanged && ConfinedScope.Confined
            && !await AutomaticRelease.Confined(db, item.PublicId).AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            changes.Add(new FieldChange(ConfinedKey, JsonValue.Create(false), JsonValue.Create(true)));
        }

        if (changes.Count == 0 && contextChanges.Count == 0)
        {
            return WorkItemMapper.ToDto(item, now, item.Attempts, includeSnapshots: false);
        }

        var reason = NormalizeReason(request.Reason);
        item.UpdatedAt = now;
        foreach (var change in changes)
        {
            journal.Append(db, actor, JournalKinds.WorkItemUpdated, campaign: null, key: change.Key, old: change.Old, updated: change.New, reason: reason, workItem: item);
        }

        foreach (var change in contextChanges)
        {
            journal.Append(db, actor, JournalKinds.WorkItemContextUpdated, campaign: null, key: change.Key, old: change.Old, updated: change.New, reason: reason, workItem: item);
        }

        if (item.Status == WorkItemStatus.AwaitingApproval && inputChanged)
        {
            await WithdrawAskAsync(item, actor, now, cancellationToken).ConfigureAwait(false);
        }

        // A doubting review was about the text it read; once the input moves, that review no longer stands, nor its sentence.
        if (inputChanged)
        {
            item.ReviewNote = null;
        }

        if (item.Status == WorkItemStatus.Expired && request.DueAt.IsSet)
        {
            var previous = item.Status;
            WorkItemTransitions.Apply(item, WorkItemStatus.Created, now);
            journal.Append(
                db,
                actor,
                WorkItemTransitions.JournalKind(previous, WorkItemStatus.Created),
                campaign: null,
                key: "status",
                old: Status(previous),
                updated: Status(WorkItemStatus.Created),
                reason: reason,
                workItem: item);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return WorkItemMapper.ToDto(item, now, item.Attempts, includeSnapshots: false);
    }

    /// <summary>
    /// The question a parked item waits on was about arguments this patch has just replaced, so it is withdrawn, by the
    /// person who replaced them and saying why, and the item goes back to the queue. Nothing is asked here: the claim is
    /// the one place that decides whether a person is asked, and it decides about this item as about any other, under
    /// what the item now says. Written with the patch, in its one save, so an approval that read the question first and
    /// commits after this finds it superseded and approves nothing — its UPDATE carries the status it read, as this
    /// one does, which is also what makes this write nothing when that approval committed first.
    /// </summary>
    private async Task WithdrawAskAsync(WorkItem item, ActorRef actor, DateTime now, CancellationToken cancellationToken)
    {
        var live = await ApprovalGate.LiveAsync(db, item.CampaignId, item.Id, cancellationToken).ConfigureAwait(false);
        if (live is not { Status: ApprovalStatus.Pending })
        {
            return;
        }

        ApprovalGate.Resolve(db, journal, item, live, ApprovalStatus.Superseded, actor, ApprovalGate.InputChanged, now);
        var previous = item.Status;
        WorkItemTransitions.Apply(item, WorkItemStatus.Created, now);
        journal.Append(
            db,
            actor,
            WorkItemTransitions.JournalKind(previous, WorkItemStatus.Created),
            campaign: null,
            key: "status",
            old: Status(previous),
            updated: Status(WorkItemStatus.Created),
            reason: ApprovalGate.InputChanged,
            workItem: item);
    }

    /// <summary>Stops the work for good. Asking twice is how a retry after a lost response looks, so it is not a second event.</summary>
    public async Task<WorkItemDto> CancelAsync(WorkItemCancelRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        WorkItemValidation.ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var item = await LoadAsync(db, request.WorkItemId, cancellationToken).ConfigureAwait(false);
        var now = clock.GetUtcNow().UtcDateTime;
        if (item.Status == WorkItemStatus.Cancelled)
        {
            return WorkItemMapper.ToDto(item, now, item.Attempts, includeSnapshots: false);
        }

        if (WorkItemTransitions.Final.Contains(item.Status))
        {
            throw DomainErrors.WorkItemTerminal(item.PublicId, item.Status);
        }

        canceller.Cancel(
            db,
            item,
            actor,
            NormalizeReason(request.Reason),
            await ApprovalGate.LiveAsync(db, item.CampaignId, item.Id, cancellationToken).ConfigureAwait(false),
            await OpenWaits.OfAsync(db, item.Id, cancellationToken).ConfigureAwait(false));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        canceller.StopCancelled();
        return WorkItemMapper.ToDto(item, now, item.Attempts, includeSnapshots: false);
    }

    /// <summary>
    /// Ends a wait before what it waited for has happened, on somebody's word: the open wait is released as
    /// <c>resumed_by_actor</c> and the item goes back to <c>created</c> through the scan's own routine, so the next
    /// attempt is told it was resumed by the actor named rather than woken by its work. The actor is required — a wait
    /// is ended on somebody's word, and the line says whose — and the reason they give is journaled with it. Only a
    /// waiting item can be resumed; anything else is refused with its status, including an item the scan released a
    /// moment earlier.
    /// </summary>
    public async Task<WorkItemDto> ResumeAsync(WorkItemResumeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        if (request.Actor is null)
        {
            errors.Add("actor", "required", "actor is required: a wait is ended on somebody's word, and the line says whose.");
        }

        WorkItemValidation.ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var item = await LoadAsync(db, request.WorkItemId, cancellationToken).ConfigureAwait(false);
        var wait = item.Status == WorkItemStatus.Waiting
            ? await OpenWaits.OfAsync(db, item.Id, cancellationToken).ConfigureAwait(false)
            : null;
        if (wait is null)
        {
            throw DomainErrors.WorkItemNotWaiting(item.PublicId, item.Status);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        OpenWaits.Release(db, journal, wait, item, WaitReleaseReason.ResumedByActor, actor, NormalizeReason(request.Reason), now);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The scan released, or somebody cancelled, the item between the read and this write; that stands, and the
            // caller is told where the item is now.
            db.ChangeTracker.Clear();
            var current = await LoadAsync(db, item.PublicId, cancellationToken).ConfigureAwait(false);
            throw DomainErrors.WorkItemNotWaiting(current.PublicId, current.Status);
        }

        return WorkItemMapper.ToDto(item, now, item.Attempts, includeSnapshots: false);
    }

    /// <summary>The item with everything a change needs — campaign, contact and attempts — tracked, or the 404 the caller is owed.</summary>
    public static async Task<WorkItem> LoadAsync(JasonDbContext db, string? publicId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var id = RequireId(publicId);
        return await db.WorkItems
            .WithNavigation()
            .Include(w => w.Attempts)
            .FirstOrDefaultAsync(w => w.PublicId == id, cancellationToken)
            .ConfigureAwait(false)
            ?? throw DomainErrors.WorkItemNotFound(id);
    }

    private async Task<bool> RoleExistsAsync(WorkItemCreateRequest request, CancellationToken cancellationToken)
    {
        if (request.Kind != WorkItemKind.AiRole || string.IsNullOrWhiteSpace(request.Role))
        {
            return false;
        }

        var name = request.Role.Trim();
        return await db.Roles.AsNoTracking().AnyAsync(r => r.Name == name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Whether the registry holds the profile the request names. A disabled one counts: it exists, and whether
    /// it may run this work is the claim's question, not this one's.
    /// </summary>
    private async Task<bool> ProfileExistsAsync(string? executionProfile, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executionProfile))
        {
            return false;
        }

        var name = executionProfile.Trim();
        return await db.ExecutionProfiles.AsNoTracking().AnyAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Work about a person is only meaningful while the campaign is allowed to touch that person, so membership
    /// is checked here rather than left to the executor to discover.
    /// </summary>
    private async Task<Contact?> LoadMemberAsync(Campaign campaign, string? contactId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(contactId))
        {
            return null;
        }

        var contact = await ContactService.LoadAsync(db, contactId, cancellationToken).ConfigureAwait(false);
        if (contact.ArchivedAt is not null)
        {
            throw DomainErrors.ContactArchived(contact.PublicId);
        }

        var member = await db.CampaignContacts
            .AnyAsync(m => m.CampaignId == campaign.Id && m.ContactId == contact.Id && m.State != MembershipState.Excluded, cancellationToken)
            .ConfigureAwait(false);
        return member ? contact : throw DomainErrors.ContactNotMember(contact.PublicId, campaign.PublicId);
    }

    /// <summary>
    /// What the item was asked to be, so the chronicle answers "where did this work come from" on its own; and, only where it
    /// is so, that it was made in the desktop's chat (<see cref="ConfinedScope"/>), which a send keeps for good.
    /// </summary>
    private static JsonObject CreationSummary(WorkItem item, Contact? contact)
    {
        var summary = new JsonObject
        {
            ["kind"] = JsonSerializer.SerializeToNode(item.Kind, JasonJson.Options),
            ["role"] = item.Role is null ? null : JsonValue.Create(item.Role),
            ["operation"] = item.Operation is null ? null : JsonValue.Create(item.Operation),
            ["contact_id"] = contact is null ? null : JsonValue.Create(contact.PublicId),
            ["priority"] = JsonValue.Create(item.Priority),
            ["not_before"] = Moment(item.NotBefore),
            ["due_at"] = Moment(item.DueAt),
        };
        if (ConfinedScope.Confined)
        {
            summary[ConfinedKey] = true;
        }

        return summary;
    }

    /// <summary>The member, and the setting's key, that say an item was made or its input edited in the desktop's chat.</summary>
    internal const string ConfinedKey = "confined";

    /// <summary>
    /// Exactly the campaign-context algorithm: set then unset, keys that would not move skipped, and the whole
    /// result measured before it replaces what the item had. A claimed item's running attempt keeps its snapshot —
    /// an edit reaches the next attempt, never the one already told something else.
    /// </summary>
    private static List<FieldChange> ApplyContext(WorkItem item, JsonObject? set, IReadOnlyList<string>? unset)
    {
        var changes = new List<FieldChange>();
        if (set is null && unset is null)
        {
            return changes;
        }

        var context = item.Context.DeepClone().AsObject();
        if (set is not null)
        {
            foreach (var (key, value) in set)
            {
                var present = context.TryGetPropertyValue(key, out var current);
                if (present && JsonNode.DeepEquals(current, value))
                {
                    continue;
                }

                // A JSON node belongs to exactly one parent, and the entry must keep its value after the context
                // moves on, so every node is cloned into its own tree.
                changes.Add(new FieldChange(key, current?.DeepClone(), value?.DeepClone()));
                context[key] = value?.DeepClone();
            }
        }

        if (unset is not null)
        {
            foreach (var key in unset)
            {
                if (!context.TryGetPropertyValue(key, out var current))
                {
                    continue;
                }

                changes.Add(new FieldChange(key, current?.DeepClone(), null));
                context.Remove(key);
            }
        }

        if (changes.Count == 0)
        {
            return changes;
        }

        ContextRules.EnsureWithinLimits(context);
        item.Context = context;
        return changes;
    }

    /// <summary>
    /// A patch that touches the reserved input key is the operation's arguments being rewritten, so the contract
    /// reads them again exactly as it did at create. A patch that leaves the key alone is not re-measured: the
    /// arguments were accepted once and nothing in this request moved them.
    /// </summary>
    private static void ValidateProviderInput(WorkItem item, JsonObject? set, IReadOnlyList<string>? unset, PatternTiming timing, ValidationErrors errors)
    {
        if (item.Kind != WorkItemKind.ProviderOp || item.Operation is null)
        {
            return;
        }

        var removed = unset?.Any(key => string.Equals(key, InputKey, StringComparison.Ordinal)) == true;
        var written = set?.ContainsKey(InputKey) == true;
        if (!removed && !written)
        {
            return;
        }

        // The patch writes the set before it honours the unset, so a key named in both ends up gone; the contract
        // has to read the arguments the item will actually hold, not the ones the request mentions first.
        WorkItemValidation.ValidateProviderInput(item.Operation, removed ? null : set![InputKey], timing, errors);
    }

    /// <summary>
    /// A patch that names the verdict reference, or the arguments it is compared with, is read against the context the
    /// item will hold — the set written, then the unset honoured, as the patch applies them — by the same checks as at
    /// create. A patch that names neither leaves a reference that was true when it was written.
    /// </summary>
    private async Task ValidateVerdictReferenceAsync(
        WorkItem item, JsonObject? set, IReadOnlyList<string>? unset, ValidationErrors errors, CancellationToken cancellationToken)
    {
        string[] keys = [StandingApprovals.VerdictKey, InputKey];
        var named = keys.Any(key => set?.ContainsKey(key) == true || unset?.Contains(key, StringComparer.Ordinal) == true);

        // A patch already refused — its arguments not the operation's shape, say — is refused for that, as a create is
        // before it reads a reference; the reference is asked about the work the patch would leave, and there is none.
        if (!named || errors.Any)
        {
            return;
        }

        var context = item.Context.DeepClone().AsObject();
        foreach (var (key, value) in set ?? [])
        {
            context[key] = value?.DeepClone();
        }

        foreach (var key in unset ?? [])
        {
            context.Remove(key);
        }

        await StandingApprovals.ValidateReferenceAsync(
            db, item.Kind, item.Operation, item.CampaignId, item.Contact?.PublicId, context, errors, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateContextKeys(JsonObject? set, IReadOnlyList<string>? unset, ValidationErrors errors)
    {
        if (set is not null && set.Any(pair => string.IsNullOrWhiteSpace(pair.Key)))
        {
            errors.Add("set", "invalid", "context keys must not be blank.");
        }

        if (unset is null)
        {
            return;
        }

        for (var index = 0; index < unset.Count; index++)
        {
            if (string.IsNullOrWhiteSpace(unset[index]))
            {
                errors.Add(string.Create(CultureInfo.InvariantCulture, $"unset[{index}]"), "invalid", "context keys must not be blank.");
            }
        }
    }

    private static JsonNode? Moment(DateTime? value) =>
        value is { } moment ? JsonSerializer.SerializeToNode(WorkItemMapper.Utc(moment), JasonJson.Options) : null;

    private static JsonNode? Number(int? value) => value is { } number ? JsonValue.Create(number) : null;

    private static JsonNode? Text(string? value) => value is null ? null : JsonValue.Create(value);

    private static JsonNode? Status(WorkItemStatus status) => JsonSerializer.SerializeToNode(status, JasonJson.Options);

    private static string RequireId(string? publicId) =>
        string.IsNullOrWhiteSpace(publicId) ? throw DomainErrors.Required("work_item_id") : publicId.Trim();

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    /// <summary>One field that moved, in the shape the chronicle records it.</summary>
    private readonly record struct FieldChange(string Key, JsonNode? Old, JsonNode? New);
}
