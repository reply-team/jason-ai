using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Channels;
using Jason.Contracts.Ids;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Contacts;

/// <summary>
/// The do-not-contact list, global across campaigns and scoped to one channel: opting out of email is not
/// opting out of LinkedIn. Entries are keyed by the normalized value, so a suppression matches however the
/// address was typed the next time somebody imports it.
/// </summary>
public sealed class SuppressionService(JasonDbContext db, JournalWriter journal, TimeProvider clock) : ISuppressionCheck
{
    /// <summary>
    /// Whether this address is out of reach, asked of the caller's own context: the claim asks inside its
    /// transaction, and the entries are keyed by the normalized value, which is also how a channel is stored.
    /// </summary>
    public Task<bool> IsSuppressedAsync(JasonDbContext context, string channel, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Suppressions.AnyAsync(
            suppression => suppression.Channel == channel && suppression.Value == value,
            cancellationToken);
    }

    /// <summary>
    /// Idempotent: an existing pair answers with the entry that is already there and writes no second chronicle line. That
    /// holds of a pair another writer — an opt-out's stop, a bounce's — puts on the register between this one's read and its
    /// save: the register's index of a pair refuses the save, and the add answers the entry that writer saved, with nothing
    /// it staged left on the context.
    /// </summary>
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

        var suppression = Stage(db, journal, clock.GetUtcNow().UtcDateTime, actor, channel!, value!, request.Reason, out var line);
        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException failure) when (HeldFirstElsewhere(failure))
        {
            // The save was refused whole, so neither the entry nor its line is in the database; they are let go of here so
            // the context, which a request scope may ask again, saves nothing of this add later.
            db.Entry(suppression).State = EntityState.Detached;
            db.Entry(line).State = EntityState.Detached;
            var held = await FindAsync(channel!, value!, cancellationToken).ConfigureAwait(false);
            return ToDto(held!);
        }

        return ToDto(suppression);
    }

    /// <summary>
    /// Puts each pair on the register that is not on it yet, in the caller's change set and unsaved, so the entries commit
    /// with the act that made them or not at all: the same entry and the same <c>suppression_added</c> line as
    /// <see cref="AddAsync"/> writes, and none for a pair already there. The values are taken as they stand, so they are a
    /// stored channel's, which the register's rule already keys; each entry is dated <paramref name="at"/>, the act's moment.
    /// </summary>
    internal static async Task<int> StageAsync(
        JasonDbContext db,
        JournalWriter journal,
        DateTime at,
        ActorRef actor,
        IEnumerable<(string Channel, string Value)> pairs,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        var staged = 0;
        foreach (var (channel, value) in pairs.Distinct())
        {
            var held = await db.Suppressions.AnyAsync(s => s.Channel == channel && s.Value == value, cancellationToken).ConfigureAwait(false);
            if (!held)
            {
                Stage(db, journal, at, actor, channel, value, reason);
                staged++;
            }
        }

        return staged;
    }

    /// <summary>
    /// Whether a save was refused because a pair it puts on the register is already there — put there by another writer
    /// that read the register before this one saved and saved first. Only the register's one index of a pair answers yes;
    /// any other refusal, of this table or another, is not this one. A writer that stages an entry in an act's change set
    /// decides that act once more, and the pair it finds held is "already on the register".
    /// </summary>
    public static bool HeldFirstElsewhere(DbUpdateException failure) =>
        failure?.InnerException is SqliteException { SqliteErrorCode: 19 } refused
        && refused.Message.Contains(OnePairRefused, StringComparison.Ordinal);

    /// <summary>What SQLite says when the register's index of a pair refuses an entry.</summary>
    private const string OnePairRefused = "UNIQUE constraint failed: suppressions.channel, suppressions.value";

    private static Suppression Stage(JasonDbContext db, JournalWriter journal, DateTime at, ActorRef actor, string channel, string value, string? reason) =>
        Stage(db, journal, at, actor, channel, value, reason, out _);

    private static Suppression Stage(JasonDbContext db, JournalWriter journal, DateTime at, ActorRef actor, string channel, string value, string? reason, out JournalEntry line)
    {
        var suppression = new Suppression
        {
            PublicId = PublicId.New("sup"),
            Channel = channel,
            Value = value,
            Reason = reason,
            CreatedAt = at,
        };

        db.Suppressions.Add(suppression);

        // The chronicle is authoritative domain history a role reads, not a log line: what was suppressed is
        // the whole point of the entry, so the value belongs in it.
        line = journal.Append(db, actor, JournalKinds.SuppressionAdded, campaign: null, key: channel, updated: Pair(channel, value), reason: reason);
        return suppression;
    }

    /// <summary>Lifting a suppression is a deliberate act, so the reason is mandatory; removing nothing is still a 200.</summary>
    public async Task<SuppressionRemovedDto> RemoveAsync(SuppressionRemoveRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var actor = Actors.Resolve(request.Actor);
        var errors = new ValidationErrors();
        var named = ChannelRules.NormalizeChannelName(request.Channel, out _);
        var left = named is null ? null : await LeftAsItStoodAsync(named, request.Value, cancellationToken).ConfigureAwait(false);
        var (channel, value) = left is not null ? (named, left.Value) : Normalize(request.Channel, request.Value, errors);
        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            errors.Add("reason", "required", "reason is required: putting somebody back in reach has to be explainable later.");
        }

        errors.ThrowIfAny();

        var existing = left ?? await FindAsync(channel!, value!, cancellationToken).ConfigureAwait(false);
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
            channel = ChannelValidation.Name(request.Channel, "channel", errors);
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
                value = await LeftAsItStoodAsync(channel, request.Value, cancellationToken).ConfigureAwait(false) is { } left
                    ? left.Value
                    : ChannelValidation.Value(channel, request.Value, "value", errors);
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
        var channel = ChannelValidation.Name(rawChannel, "channel", errors);

        // Without a usable channel there is no rule to normalize the value with, but a missing value is still
        // reported: one round trip has to name every problem the payload has.
        var value = channel is null
            ? Missing(rawValue, errors)
            : ChannelValidation.Value(channel, rawValue, "value", errors);

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

    /// <summary>
    /// The entry kept under exactly the text given, trimmed, looked up before the channel's rule is asked anything: the text
    /// is keyed, and the key looked up, only where no entry holds the text itself. So an entry the start that re-keyed the
    /// register left as it stood — beside the entry holding its key, or under a text the rule now refuses, whatever it
    /// refuses it for — is listed and lifted by its own text, and lifting it leaves the entry holding the key, and the stop
    /// it makes, where they are. A text that is its own key finds the entry holding it on this step. Null where nothing was
    /// given or no entry holds the text.
    /// </summary>
    private async Task<Suppression?> LeftAsItStoodAsync(string channel, string? raw, CancellationToken cancellationToken) =>
        string.IsNullOrWhiteSpace(raw) ? null : await FindAsync(channel, raw.Trim(), cancellationToken).ConfigureAwait(false);

    private Task<Suppression?> FindAsync(string channel, string value, CancellationToken cancellationToken) =>
        db.Suppressions.FirstOrDefaultAsync(suppression => suppression.Channel == channel && suppression.Value == value, cancellationToken);

    private static JsonObject Pair(string channel, string value) => new() { ["channel"] = channel, ["value"] = value };

    private static SuppressionDto ToDto(Suppression suppression) =>
        new(suppression.PublicId, suppression.Channel, suppression.Value, suppression.Reason, ContactMapper.ToOffset(suppression.CreatedAt));
}
