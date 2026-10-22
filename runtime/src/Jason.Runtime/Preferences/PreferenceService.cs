using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Operations;
using Jason.Contracts.Preferences;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Preferences;

/// <summary>
/// The named preferences: structured facts about the client a person states — who messages are written as, how the
/// company sounds — one value per key for the whole installation, each checked against its key's document in the catalog.
/// <para>
/// A value is the person's word, so only a person sets or unsets one, and the request names them; a role reads it and
/// never writes it. Every change is written to the chronicle in the same transaction as the change, as a line of the
/// installation's, keyed by the preference's key — and by the value's size and hash alone: a value can be personal data
/// a person must be able to take back, which unsetting does by deleting the row, and the append-only chronicle could
/// never forget it.
/// </para>
/// </summary>
public sealed class PreferenceService(JasonDbContext db, JournalWriter journal, TimeProvider clock, PatternTiming? timing = null)
{
    public const int MaxReasonLength = CampaignService.MaxReasonLength;

    private readonly PatternTiming _timing = timing ?? PatternTiming.Default;

    /// <summary>
    /// Sets a key's value whole. The value a key already has — the same canonical form, whoever sends it — changes
    /// nothing and writes no line, so a set sent again after its answer was lost is never a second line.
    /// </summary>
    public async Task<PreferenceDto> SetAsync(PreferenceSetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Person(request.Actor);

        var errors = new ValidationErrors();
        var contract = Contract(request.Key, errors);
        ValidateReason(request.Reason, errors);
        var value = Value(contract, request.Value, errors);
        errors.ThrowIfAny();

        var document = value!.Document;
        var measured = value.Measured;
        var row = await db.Preferences.FirstOrDefaultAsync(p => p.Key == contract!.Key, cancellationToken).ConfigureAwait(false);
        if (row is not null && string.Equals(row.ValueHash, measured.Hash, StringComparison.Ordinal))
        {
            return ToDto(row);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var before = row is null ? null : Measure(row.ValueBytes, row.ValueHash);
        var inserting = row is null;
        if (row is null)
        {
            row = new Preference
            {
                Key = contract!.Key,
                Value = document,
                ValueHash = measured.Hash,
                ValueBytes = measured.Bytes,
                UpdatedByType = actor.Type,
                UpdatedById = actor.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.Preferences.Add(row);
        }
        else
        {
            row.Value = document;
            row.ValueHash = measured.Hash;
            row.ValueBytes = measured.Bytes;
            row.UpdatedByType = actor.Type;
            row.UpdatedById = actor.Id;
            row.UpdatedAt = now;
        }

        journal.Append(
            db,
            actor,
            JournalKinds.PreferenceSet,
            campaign: null,
            key: row.Key,
            old: before,
            updated: Measure(measured.Bytes, measured.Hash),
            reason: Text(request.Reason));

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (inserting)
        {
            // Two people set the same key for the first time at once: the unique index decided, and the one who came
            // second is told to read the value and set it again rather than having it replaced under them. Only the insert
            // can lose that race, so only the insert is answered this way.
            db.ChangeTracker.Clear();
            if (!await db.Preferences.AsNoTracking().AnyAsync(p => p.Key == row.Key, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            throw DomainErrors.ConcurrentUpdate();
        }

        return ToDto(row);
    }

    /// <summary>
    /// Takes a key's value back: the row is deleted, so the value is gone rather than kept beside a newer one, and the line
    /// says only that it was there, by its size and hash. A key that is not set changes nothing and writes no line.
    /// </summary>
    public async Task<PreferenceDto> UnsetAsync(PreferenceUnsetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Person(request.Actor);

        var errors = new ValidationErrors();
        var contract = Contract(request.Key, errors);
        ValidateReason(request.Reason, errors);
        errors.ThrowIfAny();

        var row = await db.Preferences.FirstOrDefaultAsync(p => p.Key == contract!.Key, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            return Unset(contract!.Key);
        }

        db.Preferences.Remove(row);
        journal.Append(
            db,
            actor,
            JournalKinds.PreferenceUnset,
            campaign: null,
            key: row.Key,
            old: Measure(row.ValueBytes, row.ValueHash),
            reason: Text(request.Reason));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Unset(row.Key);
    }

    /// <summary>
    /// Every key the catalog knows, in key order, set or not: an unset key answers with a null value and nothing said
    /// about it, so a reader learns what is missing in the same call as what is there, as a role that never wrote reads an
    /// empty note.
    /// </summary>
    public async Task<Page<PreferenceDto>> ListAsync(PreferenceListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var keys = PreferenceCatalog.Keys
            .Where(key => after is null || string.CompareOrdinal(key, after) > 0)
            .Take(limit + 1)
            .ToList();
        var rows = await db.Preferences.AsNoTracking()
            .Where(p => keys.Contains(p.Key))
            .ToDictionaryAsync(p => p.Key, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        return Paging.ToPage(keys, limit, key => key, key => rows.TryGetValue(key, out var row) ? ToDto(row) : Unset(key));
    }

    /// <summary>
    /// Who is setting it. An absent actor is an anonymous person, which is right for creating work and wrong for a
    /// preference: a recorded preference names the person whose word it is. As with a decision, nothing here can tell a
    /// person from a process holding that person's command line; what it guarantees is that nothing inside the runtime
    /// claims to be one.
    /// </summary>
    private static ActorRef Person(ActorRef? claimed)
    {
        var actor = Actors.Resolve(claimed);
        if (actor.Type != ActorType.Human)
        {
            throw DomainErrors.PreferenceNotHuman(actor.Type);
        }

        return string.IsNullOrWhiteSpace(actor.Id) ? throw DomainErrors.PreferenceActorRequired() : actor;
    }

    /// <summary>The key's document in the catalog; a key it does not know is refused, naming the keys it does.</summary>
    private static PreferenceContract? Contract(string? key, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            errors.Add("key", "required", "key is required.");
            return null;
        }

        var contract = PreferenceCatalog.Find(key.Trim());
        if (contract is null)
        {
            errors.Add("key", "unknown", $"key must name a preference this build knows: {string.Join(", ", PreferenceCatalog.Keys)}.");
        }

        return contract;
    }

    /// <summary>
    /// A value is a JSON object the key's schema accepts, every problem said at its own pointer under <c>value</c>, and at
    /// most <see cref="PreferenceContract.MaxValueBytes"/> in its canonical form — measured and named in one pass, so the
    /// size the cap refuses is the size the chronicle reports.
    /// </summary>
    private Checked? Value(PreferenceContract? contract, JsonNode? value, ValidationErrors errors)
    {
        if (value is null)
        {
            errors.Add("value", "required", "value is required; unset the preference to take its value away.");
            return null;
        }

        if (value is not JsonObject given)
        {
            errors.Add("value", "invalid", "value must be a JSON object.");
            return null;
        }

        if (contract is null)
        {
            return null;
        }

        var document = (JsonObject)given.DeepClone();
        var problems = contract.CheckValue(document, _timing);
        foreach (var problem in SchemaValidator.Verdicts(problems))
        {
            errors.Add("value" + problem.Pointer, "invalid", problem.Message);
        }

        // A pattern that did not finish is no verdict on the value: it is answered as a request to send again, unless
        // something else is wrong with it.
        foreach (var problem in problems.Where(problem => problem.Reason == SchemaValidator.PatternTimedOut))
        {
            errors.Unmeasured("value" + problem.Pointer, problem.Message);
        }

        if (problems.Count > 0)
        {
            return null;
        }

        var measured = CanonicalJson.Measure(document)!.Value;
        if (measured.Bytes > PreferenceContract.MaxValueBytes)
        {
            errors.Add(
                "value",
                "too_large",
                string.Create(CultureInfo.InvariantCulture, $"value must serialize to at most {PreferenceContract.MaxValueBytes} bytes."));
            return null;
        }

        return new Checked(document, measured);
    }

    private static void ValidateReason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {MaxReasonLength} characters."));
        }
    }

    /// <summary>What the chronicle says about a value: its size and its hash, and never a word of it.</summary>
    private static JsonObject Measure(long bytes, string hash) => new() { ["bytes"] = bytes, ["hash"] = hash };

    private static PreferenceDto Unset(string key) => new(key, null, null, null, null, null, null);

    private static PreferenceDto ToDto(Preference row) =>
        new(
            row.Key,
            row.Value,
            row.ValueHash,
            row.ValueBytes,
            Moment(row.CreatedAt),
            Moment(row.UpdatedAt),
            new ActorRef(row.UpdatedByType, row.UpdatedById));

    private static DateTimeOffset Moment(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>A value the schema accepted, and its canonical size and hash.</summary>
    private sealed record Checked(JsonObject Document, CanonicalMeasure Measured);
}
