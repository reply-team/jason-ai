using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Contacts;

/// <summary>
/// The do-not-contact list, global across campaigns and scoped to one channel: opting out of email is not
/// opting out of LinkedIn. Entries are keyed by the normalized value, so a suppression matches however the
/// address was typed the next time somebody imports it.
/// </summary>
public sealed class SuppressionService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    /// <summary>Idempotent: an existing pair answers with the entry that is already there and writes no second chronicle line.</summary>
    public async Task<SuppressionDto> AddAsync(SuppressionAddRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = Actors.Resolve(request.Actor);
        var errors = new ValidationErrors();
        var (channel, value) = Normalize(request.Channel, request.Value, errors);
        errors.ThrowIfAny();

        var existing = await FindAsync(channel!, value!, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return ToDto(existing);
        }

        var suppression = new Suppression
        {
            PublicId = PublicId.New("sup"),
            Channel = channel!,
            Value = value!,
            Reason = request.Reason,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        db.Suppressions.Add(suppression);

        // The chronicle is authoritative domain history a role reads, not a log line: what was suppressed is
        // the whole point of the entry, so the value belongs in it.
        journal.Append(db, actor, JournalKinds.SuppressionAdded, campaign: null, key: channel, updated: Pair(channel!, value!), reason: request.Reason);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToDto(suppression);
    }

    /// <summary>Lifting a suppression is a deliberate act, so the reason is mandatory; removing nothing is still a 200.</summary>
    public async Task<SuppressionRemovedDto> RemoveAsync(SuppressionRemoveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = Actors.Resolve(request.Actor);
        var errors = new ValidationErrors();
        var (channel, value) = Normalize(request.Channel, request.Value, errors);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors.Add("reason", "required", "reason is required: putting somebody back in reach has to be explainable later.");
        }

        errors.ThrowIfAny();

        var existing = await FindAsync(channel!, value!, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return new SuppressionRemovedDto(channel!, value!, false);
        }

        db.Suppressions.Remove(existing);
        journal.Append(db, actor, JournalKinds.SuppressionRemoved, campaign: null, key: channel, updated: Pair(channel!, value!), reason: request.Reason);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new SuppressionRemovedDto(channel!, value!, true);
    }

    public async Task<Page<SuppressionDto>> ListAsync(SuppressionListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        string? channel = null;
        string? value = null;
        if (!string.IsNullOrWhiteSpace(request.Channel))
        {
            channel = ChannelRules.NormalizeChannelName(request.Channel, "channel", errors);
        }

        if (!string.IsNullOrWhiteSpace(request.Value))
        {
            if (string.IsNullOrWhiteSpace(request.Channel))
            {
                // A value has no canonical form until its channel says which rule normalizes it.
                errors.Add("value", "invalid", "value narrows a channel; send channel as well.");
            }
            else if (channel is not null)
            {
                value = ChannelRules.NormalizeValue(channel, request.Value, "value", errors);
            }
        }

        errors.ThrowIfAny();
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.Suppressions.AsQueryable();
        if (channel is not null)
        {
            query = query.Where(suppression => suppression.Channel == channel);
        }

        if (value is not null)
        {
            query = query.Where(suppression => suppression.Value == value);
        }

        if (after is not null)
        {
            query = query.Where(suppression => string.Compare(suppression.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(suppression => suppression.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, suppression => suppression.PublicId, ToDto);
    }

    /// <summary>Both parts are required; the channel decides the rule the value is normalized with.</summary>
    private static (string? Channel, string? Value) Normalize(string? rawChannel, string? rawValue, ValidationErrors errors)
    {
        var channel = ChannelRules.NormalizeChannelName(rawChannel, "channel", errors);

        // Without a usable channel there is no rule to normalize the value with, but a missing value is still
        // reported: one round trip has to name every problem the payload has.
        var value = channel is null
            ? Missing(rawValue, errors)
            : ChannelRules.NormalizeValue(channel, rawValue, "value", errors);

        return (channel, value);
    }

    private static string? Missing(string? rawValue, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            errors.Add("value", "required", "value is required.");
        }

        return null;
    }

    private Task<Suppression?> FindAsync(string channel, string value, CancellationToken cancellationToken) =>
        db.Suppressions.FirstOrDefaultAsync(suppression => suppression.Channel == channel && suppression.Value == value, cancellationToken);

    private static JsonObject Pair(string channel, string value) => new() { ["channel"] = channel, ["value"] = value };

    private static SuppressionDto ToDto(Suppression suppression) =>
        new(suppression.PublicId, suppression.Channel, suppression.Value, suppression.Reason, ContactMapper.ToOffset(suppression.CreatedAt));
}
