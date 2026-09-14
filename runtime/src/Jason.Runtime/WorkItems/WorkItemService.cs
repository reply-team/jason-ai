using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Json;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.WorkItems;

/// <summary>
/// Work items as callers create, read and list them. The service owns what a caller may ask for; whether an
/// item may run, and what happens when it does, belongs to the dispatcher and to
/// <see cref="WorkItemTransitions"/>.
/// </summary>
public sealed class WorkItemService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    public const int MaxRoleLength = 64;

    public const int MaxOperationLength = 200;

    public const int MaxExecutionProfileLength = 100;

    public const int MaxResultFormatBytes = 16 * 1024;

    public async Task<WorkItemDto> CreateAsync(WorkItemCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        WorkItemValidation.ValidateCreate(request, await RoleExistsAsync(request, cancellationToken).ConfigureAwait(false), errors);
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
        return WorkItemMapper.ToDto(item, now, [], includeSnapshots: false);
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

        return WorkItemMapper.ToDto(item, clock.GetUtcNow().UtcDateTime, item.Attempts, request.IncludeSnapshots == true);
    }

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

    private static JsonNode? Moment(DateTime? value) =>
        value is { } moment ? JsonSerializer.SerializeToNode(WorkItemMapper.Utc(moment), JasonJson.Options) : null;

    private static string RequireId(string? publicId) =>
        string.IsNullOrWhiteSpace(publicId) ? throw DomainErrors.Required("work_item_id") : publicId.Trim();

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
}
