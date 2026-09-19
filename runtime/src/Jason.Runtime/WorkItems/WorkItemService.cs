using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Approvals;
using Jason.Runtime.Configuration;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
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
public sealed class WorkItemService(
    JasonDbContext db,
    JournalWriter journal,
    TimeProvider clock,
    WorkItemCanceller canceller,
    LiveSettings<PluginsOptions> plugins)
{
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
        var item = await CreateCoreAsync(request, Actors.Resolve(request.Actor), lineage: null, cancellationToken).ConfigureAwait(false);
        return WorkItemMapper.ToDto(item, item.CreatedAt, [], includeSnapshots: false);
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
            errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign.Status == CampaignStatus.Archived)
        {
            throw DomainErrors.CampaignArchived(campaign.PublicId);
        }

        var contact = await LoadMemberAsync(campaign, request.ContactId, cancellationToken).ConfigureAwait(false);
        var context = request.Context?.DeepClone().AsObject() ?? new JsonObject();
        ContextRules.EnsureWithinLimits(context);

        // Read once, here, for work of every kind: what the run that asked for this hands down. After this line
        // it is a fact about the row, and nothing recomputes it.
        var record = lineage ?? await Lineage.ForCreationAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var now = clock.GetUtcNow().UtcDateTime;
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
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return item;
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

        return WorkItemMapper.ToDto(
            item,
            clock.GetUtcNow().UtcDateTime,
            item.Attempts,
            request.IncludeSnapshots == true,
            await ExternalReportsAsync(item.Id, cancellationToken).ConfigureAwait(false));
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

    public async Task<Page<WorkItemSummaryDto>> ListAsync(WorkItemListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);
        var now = clock.GetUtcNow().UtcDateTime;

        // Only the live attempt is fetched: a summary carries the current attempt id and nothing else about the
        // history, and a listing must not drag ten contexts per row along with it.
        IQueryable<WorkItem> query = db.WorkItems
            .AsNoTracking()
            .WithNavigation()
            .Include(w => w.Attempts.Where(a => a.Status == AttemptStatus.Scheduled || a.Status == AttemptStatus.Running));

        if (!string.IsNullOrWhiteSpace(request.CampaignId))
        {
            var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
            query = query.Where(w => w.CampaignId == campaign.Id);
        }

        if (!string.IsNullOrWhiteSpace(request.ContactId))
        {
            var contact = await ContactService.LoadAsync(db, request.ContactId, cancellationToken).ConfigureAwait(false);
            query = query.Where(w => w.ContactId == contact.Id);
        }

        if (request.Status is { Count: > 0 } wanted)
        {
            var statuses = wanted.Distinct().ToArray();
            query = query.Where(w => statuses.Contains(w.Status));
        }

        if (request.Kind is { } kind)
        {
            query = query.Where(w => w.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var role = request.Role.Trim();
            query = query.Where(w => w.Role == role);
        }

        if (request.Eligible is { } eligible)
        {
            var predicate = WorkItemQueries.Eligible(now);
            query = query.Where(eligible
                ? predicate
                : Expression.Lambda<Func<WorkItem, bool>>(Expression.Not(predicate.Body), predicate.Parameters[0]));
        }

        if (after is not null)
        {
            query = query.Where(w => string.Compare(w.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(w => w.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, w => w.PublicId, w => WorkItemMapper.ToSummary(w, now));
    }

    /// <summary>
    /// A partial patch of what a caller owns: the window, the priority, the per-item limits, the execution
    /// profile, the result format and the context — every one of them a statement about how the work runs. What
    /// the item is — its campaign, contact, kind, role or operation — is not patchable; work that should be
    /// something else is new work.
    /// </summary>
    public async Task<WorkItemDto> UpdateAsync(WorkItemUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        WorkItemValidation.ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var item = await LoadAsync(db, request.WorkItemId, cancellationToken).ConfigureAwait(false);
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
                WorkItemValidation.ValidateResultFormat(request.ResultFormat.Value, errors);
            }
        }

        // Reopening is the only thing a patch can do to an expired item's status, and a deadline that has already
        // passed would expire it again on the next scan.
        if (item.Status == WorkItemStatus.Expired && request.DueAt.IsSet && (dueAt is null || dueAt <= now))
        {
            errors.Add("due_at", "in_the_past", "due_at must be a future moment to return an expired item to the queue.");
        }

        ValidateContextKeys(request.Set, request.Unset, errors);
        ValidateProviderInput(item, request.Set, request.Unset, errors);
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

        var contextChanges = ApplyContext(item, request.Set, request.Unset);
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
            await ApprovalGate.LiveAsync(db, item.Id, cancellationToken).ConfigureAwait(false));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
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

    /// <summary>What the item was asked to be, so the chronicle answers "where did this work come from" on its own.</summary>
    private static JsonObject CreationSummary(WorkItem item, Contact? contact) => new()
    {
        ["kind"] = JsonSerializer.SerializeToNode(item.Kind, JasonJson.Options),
        ["role"] = item.Role is null ? null : JsonValue.Create(item.Role),
        ["operation"] = item.Operation is null ? null : JsonValue.Create(item.Operation),
        ["contact_id"] = contact is null ? null : JsonValue.Create(contact.PublicId),
        ["priority"] = JsonValue.Create(item.Priority),
        ["not_before"] = Moment(item.NotBefore),
        ["due_at"] = Moment(item.DueAt),
    };

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
    private static void ValidateProviderInput(WorkItem item, JsonObject? set, IReadOnlyList<string>? unset, ValidationErrors errors)
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
        WorkItemValidation.ValidateProviderInput(item.Operation, removed ? null : set![InputKey], errors);
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
