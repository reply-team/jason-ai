using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Notes;

/// <summary>
/// Role notes: what a role remembers about a campaign, for its own later sessions. Three verbs, and none of
/// them means anything to the rest of the runtime — nothing here is read by the dispatcher, the executor or a
/// plugin, and no decision is taken because of what a note says.
/// <para>
/// That is the point rather than a limitation. A launched agent has no harness that survives its attempt, so
/// the working file it would keep on somebody's laptop has to live somewhere the next attempt can reach; making
/// it a runtime table is how it gets there, and it stays as non-authoritative as the file was. Where a note
/// disagrees with campaign state, the state is what is true (INV-MEM-001).
/// </para>
/// </summary>
public sealed class RoleNoteService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    /// <summary>
    /// What one note may carry. A constant rather than a setting: a limit an operator can raise is a limit
    /// somebody will raise, and a role that needs more than this is keeping a record rather than a note.
    /// </summary>
    public const int MaxNoteBytes = 64 * 1024;

    public const int MaxReasonLength = 2000;

    /// <summary>
    /// One role's memory of one campaign. A role that has never written answers with an empty document rather
    /// than a 404: reading its memory is the first thing a launched role does, and on a first run there is none
    /// — telling "nothing yet" from "something went wrong" is code every role would otherwise have to write.
    /// </summary>
    public async Task<RoleNoteDto> GetAsync(RoleNoteGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        RoleNoteValidation.ValidateGet(request, errors);
        errors.ThrowIfAny();

        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        var role = await RoleAsync(request.Role!, cancellationToken).ConfigureAwait(false);

        var note = await db.RoleNotes.AsNoTracking()
            .FirstOrDefaultAsync(n => n.CampaignId == campaign.Id && n.Role == role, cancellationToken)
            .ConfigureAwait(false);

        return note is null ? Empty(campaign.PublicId, role) : ToDto(note, campaign.PublicId);
    }

    /// <summary>
    /// Replaces a role's note whole. There is no patch verb and no merge: the last writer owns the document,
    /// which is the one rule that needs no reasoning about what an earlier session of the same role meant.
    /// </summary>
    public async Task<RoleNoteDto> SetAsync(RoleNoteSetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        RoleNoteValidation.ValidateSet(request, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);
        if (campaign.Status == CampaignStatus.Archived)
        {
            throw DomainErrors.CampaignArchived(campaign.PublicId);
        }

        var role = await RoleAsync(request.Role!, cancellationToken).ConfigureAwait(false);

        // Measured and named in one pass over the canonical form, so the size the cap refuses is the size the
        // chronicle reports and the hash two readers compute is the same hash.
        var document = (JsonObject)request.Note!.DeepClone();
        var measured = CanonicalJson.Measure(document)!.Value;
        if (measured.Bytes > MaxNoteBytes)
        {
            throw DomainErrors.RoleNoteTooLarge(MaxNoteBytes);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var note = await db.RoleNotes
            .FirstOrDefaultAsync(n => n.CampaignId == campaign.Id && n.Role == role, cancellationToken)
            .ConfigureAwait(false);

        var before = note is null ? null : new JsonObject { ["bytes"] = note.NoteBytes, ["hash"] = note.NoteHash };

        if (note is null)
        {
            note = new RoleNote
            {
                CampaignId = campaign.Id,
                Campaign = campaign,
                Role = role,
                Note = document,
                NoteHash = measured.Hash,
                NoteBytes = measured.Bytes,
                UpdatedByType = actor.Type,
                UpdatedById = actor.Id,
                CreatedAt = now,
                UpdatedAt = now,
            };
            db.RoleNotes.Add(note);
        }
        else
        {
            note.Note = document;
            note.NoteHash = measured.Hash;
            note.NoteBytes = measured.Bytes;
            note.UpdatedByType = actor.Type;
            note.UpdatedById = actor.Id;
            note.UpdatedAt = now;
        }

        // What changed, never what it holds: the size and the hash are enough to tell a note that moved from one
        // that did not, and a note is where a role keeps half-formed guesses about people.
        journal.Append(
            db,
            actor,
            JournalKinds.RoleNoteSet,
            campaign,
            key: role,
            old: before,
            updated: new JsonObject
            {
                ["campaign_id"] = campaign.PublicId,
                ["role"] = role,
                ["bytes"] = measured.Bytes,
                ["hash"] = measured.Hash,
            },
            reason: Text(request.Reason));

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Two sessions of one role wrote the first note at once. The unique index is what decides, and the
            // loser is told to read the row and write again rather than handed a merge nobody asked for.
            db.ChangeTracker.Clear();
            if (!await db.RoleNotes.AsNoTracking()
                    .AnyAsync(n => n.CampaignId == campaign.Id && n.Role == role, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw;
            }

            throw DomainErrors.ConcurrentUpdate();
        }

        return ToDto(note, campaign.PublicId);
    }

    /// <summary>
    /// A campaign's memory, without any of it being read: which roles have written, how much and when. Scoped to
    /// one campaign on purpose — "every note anywhere" is not a question anybody asks, and it is a way to sweep
    /// up what roles have written about people.
    /// </summary>
    public async Task<Page<RoleNoteSummaryDto>> ListAsync(RoleNoteListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var campaign = await CampaignService.LoadAsync(db, request.CampaignId, cancellationToken).ConfigureAwait(false);

        var query = db.RoleNotes.AsNoTracking().Where(n => n.CampaignId == campaign.Id);
        if (after is not null)
        {
            query = query.Where(n => string.Compare(n.Role, after) > 0);
        }

        var fetched = await query.OrderBy(n => n.Role).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, n => n.Role, note => ToSummary(note, campaign.PublicId));
    }

    /// <summary>A note exists only for a role the roster knows: a typo would otherwise become a memory nobody reads.</summary>
    private async Task<string> RoleAsync(string claimed, CancellationToken cancellationToken)
    {
        var role = claimed.Trim();
        return await db.Roles.AsNoTracking().AnyAsync(r => r.Name == role, cancellationToken).ConfigureAwait(false)
            ? role
            : throw DomainErrors.RoleNotFound(role);
    }

    private static RoleNoteDto Empty(string campaignId, string role) =>
        new(campaignId, role, [], null, 0, null, null, null);

    private static RoleNoteDto ToDto(RoleNote note, string campaignId) =>
        new(
            campaignId,
            note.Role,
            note.Note,
            note.NoteHash,
            note.NoteBytes,
            Moment(note.CreatedAt),
            Moment(note.UpdatedAt),
            new ActorRef(note.UpdatedByType, note.UpdatedById));

    private static RoleNoteSummaryDto ToSummary(RoleNote note, string campaignId) =>
        new(
            campaignId,
            note.Role,
            note.NoteHash,
            note.NoteBytes,
            Moment(note.CreatedAt),
            Moment(note.UpdatedAt),
            new ActorRef(note.UpdatedByType, note.UpdatedById));

    private static DateTimeOffset Moment(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
