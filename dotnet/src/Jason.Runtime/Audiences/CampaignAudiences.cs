using System.Text.Json.Nodes;
using Jason.Contracts.Ids;
using Jason.Contracts.Operations;
using Jason.Runtime.Domain;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.Routing;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Audiences;

/// <summary>
/// A campaign's audiences: the definitions its searches were let through under by the claim, each found by its number —
/// a top-up, a preview by number — or by its content, which is how the claim of a search finds the audience a definition
/// already is, or makes the campaign's next one. Every read goes through one of the two unique indexes the table carries, so it costs the same
/// at a campaign's thousandth audience as at its first.
/// </summary>
public static class CampaignAudiences
{
    /// <summary><c>aud_</c>: an audience's public id.</summary>
    public const string IdPrefix = "aud";

    /// <summary>
    /// The reserved key of a search's context under which the claim records the audience the search runs under: its id,
    /// its number and the definition the search is searched by. The runtime's to write: a caller naming it on a search is
    /// refused.
    /// </summary>
    public const string ContextKey = "audience";

    /// <summary>
    /// Whether work of <paramref name="operation"/> runs under an audience the claim records: it makes a search, and carries
    /// the definition it searches by.
    /// </summary>
    public static bool Recorded(string? operation) =>
        CanonicalInput.MakesASearch(operation)
        && OperationCatalog.Find(operation!) is { } contract
        && CanonicalInput.CarriesADefinition(contract);

    /// <summary>
    /// Refuses <see cref="ContextKey"/> where a caller names it on work whose audience the claim records, at create or in a
    /// patch: it is the runtime's record, and a value written beside it would say something the claim never decided.
    /// </summary>
    public static void RefuseWritten(string? operation, bool named, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (named && Recorded(operation))
        {
            errors.Add(
                "context." + ContextKey,
                "not_allowed",
                "audience is the runtime's on a search: the claim records there the audience the search runs under and the definition it is searched by. Name the audience under input, by its number or its definition.");
        }
    }

    /// <summary>The campaign's audience under <paramref name="number"/>, at most one.</summary>
    public static IQueryable<Audience> ByNumber(JasonDbContext db, int campaignId, int number)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Audiences.Where(audience => audience.CampaignId == campaignId && audience.Number == number);
    }

    /// <summary>The campaign's audience whose definition has the canonical name <paramref name="hash"/>, at most one.</summary>
    public static IQueryable<Audience> ByDefinition(JasonDbContext db, int campaignId, string hash)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Audiences.Where(audience => audience.CampaignId == campaignId && audience.DefinitionHash == hash);
    }

    /// <summary>The campaign's highest number, read off the end of its numbers; nothing where it has no audience yet.</summary>
    public static IQueryable<int> Latest(JasonDbContext db, int campaignId)
    {
        ArgumentNullException.ThrowIfNull(db);
        return db.Audiences
            .Where(audience => audience.CampaignId == campaignId)
            .OrderByDescending(audience => audience.Number)
            .Select(audience => audience.Number)
            .Take(1);
    }

    /// <summary>
    /// The campaign's audience for <paramref name="definition"/>: the one already made from a definition with the same
    /// content — its members in any order — or a new one, numbered after the campaign's highest and added to
    /// <paramref name="db"/> for the caller's save. Two writers cannot both make one: the claim that calls this holds the
    /// database's write lock, and the two unique indexes refuse a second row with the number or the content.
    /// </summary>
    public static async Task<Audience> FindOrMakeAsync(JasonDbContext db, int campaignId, JsonObject definition, DateTime now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(definition);

        var hash = CanonicalJson.Hash(definition);
        if (await ByDefinition(db, campaignId, hash).AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false) is { } found)
        {
            return found;
        }

        var made = new Audience
        {
            PublicId = PublicId.New(IdPrefix),
            CampaignId = campaignId,
            Number = await Latest(db, campaignId).FirstOrDefaultAsync(ct).ConfigureAwait(false) + 1,
            Definition = definition.DeepClone().AsObject(),
            DefinitionHash = hash,
            CreatedAt = now,
        };
        db.Audiences.Add(made);
        return made;
    }

    /// <summary>What a search carries under <see cref="ContextKey"/>: the audience, and the definition it is searched by.</summary>
    public static JsonObject Carried(Audience audience, JsonObject definition)
    {
        ArgumentNullException.ThrowIfNull(audience);
        ArgumentNullException.ThrowIfNull(definition);
        return new JsonObject
        {
            ["id"] = audience.PublicId,
            ["number"] = audience.Number,
            ["definition"] = definition.DeepClone(),
        };
    }
}
