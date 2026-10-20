using System.Globalization;
using System.Linq.Expressions;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.WorkItems;

/// <summary>What a listing and a count of work items both filter by: one record, so the two can never disagree.</summary>
internal sealed record WorkItemFilter(
    string? CampaignId,
    string? ContactId,
    IReadOnlyList<WorkItemStatus>? Status,
    WorkItemKind? Kind,
    string? Role,
    bool? Eligible,
    DateTimeOffset? Since,
    DateTimeOffset? Until,
    WorkItemListOrder Order)
{
    public static WorkItemFilter From(WorkItemListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(request.CampaignId, request.ContactId, request.Status, request.Kind, request.Role, request.Eligible, request.Since, request.Until, request.Order ?? WorkItemListOrder.Created);
    }

    public static WorkItemFilter From(WorkItemCountRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new(request.CampaignId, request.ContactId, request.Status, request.Kind, request.Role, request.Eligible, request.Since, request.Until, request.Order ?? WorkItemListOrder.Created);
    }
}

/// <summary>
/// The rows a work item filter selects, in the order it asks for, and the keyset cursor that continues them. The
/// default order pages by public id alone, as listings always have, so a cursor a caller already holds stays good;
/// the two time orders page by the time and then the id, because a time is not unique.
/// </summary>
internal static class WorkItemListing
{
    private const string DuePrefix = "due|";
    private const string FinishedPrefix = "finished|";

    public static async Task<IQueryable<WorkItem>> FilterAsync(JasonDbContext db, WorkItemFilter filter, DateTime now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Since is { } from && filter.Until is { } to && to < from)
        {
            var errors = new ValidationErrors();
            errors.Add("until", "invalid", "until must not be before since.");
            errors.ThrowIfAny();
        }

        IQueryable<WorkItem> query = db.WorkItems;

        if (!string.IsNullOrWhiteSpace(filter.CampaignId))
        {
            var campaign = await CampaignService.LoadAsync(db, filter.CampaignId, cancellationToken).ConfigureAwait(false);
            query = query.Where(w => w.CampaignId == campaign.Id);
        }

        if (!string.IsNullOrWhiteSpace(filter.ContactId))
        {
            var contact = await ContactService.LoadAsync(db, filter.ContactId, cancellationToken).ConfigureAwait(false);
            query = query.Where(w => w.ContactId == contact.Id);
        }

        if (filter.Status is { Count: > 0 } wanted)
        {
            var statuses = wanted.Distinct().ToArray();
            query = query.Where(w => statuses.Contains(w.Status));
        }

        if (filter.Kind is { } kind)
        {
            query = query.Where(w => w.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(filter.Role))
        {
            var role = filter.Role.Trim();
            query = query.Where(w => w.Role == role);
        }

        if (filter.Eligible is { } eligible)
        {
            var predicate = WorkItemQueries.Eligible(now);
            query = query.Where(eligible
                ? predicate
                : Expression.Lambda<Func<WorkItem, bool>>(Expression.Not(predicate.Body), predicate.Parameters[0]));
        }

        if (filter.Order == WorkItemListOrder.Finished)
        {
            query = query.Where(w => w.FinishedAt != null);
        }

        var since = filter.Since?.UtcDateTime;
        var until = filter.Until?.UtcDateTime;
        return filter.Order switch
        {
            WorkItemListOrder.Due => query
                .Where(w => since == null || (w.DueAt != null && w.DueAt >= since))
                .Where(w => until == null || (w.DueAt != null && w.DueAt < until)),
            WorkItemListOrder.Finished => query
                .Where(w => since == null || w.FinishedAt >= since)
                .Where(w => until == null || w.FinishedAt < until),
            _ => query
                .Where(w => since == null || w.CreatedAt >= since)
                .Where(w => until == null || w.CreatedAt < until),
        };
    }

    /// <summary>The rows after the one a cursor names, in the cursor's own order; a cursor made under another order is refused.</summary>
    public static IQueryable<WorkItem> After(IQueryable<WorkItem> query, WorkItemListOrder order, string? cursor)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (cursor is null)
        {
            return query;
        }

        switch (order)
        {
            case WorkItemListOrder.Due:
            {
                var (at, id) = Parse(cursor, DuePrefix, allowNoTime: true);
                return at is { } due
                    ? query.Where(w => w.DueAt > due || (w.DueAt == due && string.Compare(w.PublicId, id) > 0) || w.DueAt == null)
                    : query.Where(w => w.DueAt == null && string.Compare(w.PublicId, id) > 0);
            }

            case WorkItemListOrder.Finished:
            {
                var (at, id) = Parse(cursor, FinishedPrefix, allowNoTime: false);
                var finished = at!.Value;
                return query.Where(w => w.FinishedAt < finished || (w.FinishedAt == finished && string.Compare(w.PublicId, id) > 0));
            }

            default:
                if (cursor.StartsWith(DuePrefix, StringComparison.Ordinal) || cursor.StartsWith(FinishedPrefix, StringComparison.Ordinal))
                {
                    throw InvalidCursor();
                }

                return query.Where(w => string.Compare(w.PublicId, cursor) > 0);
        }
    }

    public static IOrderedQueryable<WorkItem> Order(IQueryable<WorkItem> query, WorkItemListOrder order)
    {
        ArgumentNullException.ThrowIfNull(query);
        return order switch
        {
            WorkItemListOrder.Due => query.OrderBy(w => w.DueAt == null).ThenBy(w => w.DueAt).ThenBy(w => w.PublicId),
            WorkItemListOrder.Finished => query.OrderByDescending(w => w.FinishedAt).ThenBy(w => w.PublicId),
            _ => query.OrderBy(w => w.PublicId),
        };
    }

    /// <summary>What a page's cursor names: the last row it holds, in the terms its order continues from.</summary>
    public static string CursorKey(WorkItemListOrder order, WorkItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return order switch
        {
            WorkItemListOrder.Due => DuePrefix + Format(item.DueAt) + "|" + item.PublicId,
            WorkItemListOrder.Finished => FinishedPrefix + Format(item.FinishedAt) + "|" + item.PublicId,
            _ => item.PublicId,
        };
    }

    private static string Format(DateTime? at) =>
        at is { } value ? DateTime.SpecifyKind(value, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) : string.Empty;

    private static (DateTime? At, string Id) Parse(string cursor, string prefix, bool allowNoTime)
    {
        if (!cursor.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw InvalidCursor();
        }

        var rest = cursor[prefix.Length..];
        var bar = rest.IndexOf('|', StringComparison.Ordinal);
        if (bar < 0 || bar == rest.Length - 1)
        {
            throw InvalidCursor();
        }

        var time = rest[..bar];
        var id = rest[(bar + 1)..];
        if (time.Length == 0)
        {
            return allowNoTime ? (null, id) : throw InvalidCursor();
        }

        return DateTime.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            ? (at, id)
            : throw InvalidCursor();
    }

    private static ValidationException InvalidCursor() =>
        new([new ErrorDetail("cursor", "invalid", "cursor must be the next_cursor returned by an earlier page.")]);
}
