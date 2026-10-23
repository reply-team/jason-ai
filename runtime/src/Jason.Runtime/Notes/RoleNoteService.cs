using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Json;
using Jason.Runtime.Persistence;
using Jason.Runtime.Projects;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Notes;

/// <summary>
/// Role notes: what a role remembers, for its own later sessions, at one of three levels — the client's, which is its
/// general craft, a project's, or a campaign's. Three verbs, each naming exactly one level, and none of them means
/// anything to the rest of the runtime — nothing here is read by the dispatcher, the executor or a plugin, and no
/// decision is taken because of what a note says.
/// <para>
/// That is the point rather than a limitation. A launched agent has no harness that survives its attempt, so
/// the working file it would keep on somebody's laptop has to live somewhere the next attempt can reach; making
/// it a runtime table is how it gets there, and it stays as non-authoritative as the file was. Where a note
/// disagrees with campaign state, the state is what is true (INV-MEM-001). The levels are composed by the reader —
/// client, then project, then campaign, the lower winning — and never here: the runtime does not read a note.
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
    /// One role's memory at one level. A role that has never written answers with an empty document rather
    /// than a 404: reading its memory is the first thing a launched role does, and on a first run there is none
    /// — telling "nothing yet" from "something went wrong" is code every role would otherwise have to write.
    /// </summary>
    public async Task<RoleNoteDto> GetAsync(RoleNoteGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        RoleNoteValidation.ValidateGet(request, errors);
        errors.ThrowIfAny();

        var level = await LevelAsync(request.CampaignId, request.ProjectId, cancellationToken).ConfigureAwait(false);
        var role = await RoleAsync(request.Role!, cancellationToken).ConfigureAwait(false);

        var note = await At(db.RoleNotes.AsNoTracking(), level)
            .FirstOrDefaultAsync(n => n.Role == role, cancellationToken)
            .ConfigureAwait(false);

        return note is null ? Empty(level, role) : ToDto(note, level);
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

        var level = await LevelAsync(request.CampaignId, request.ProjectId, cancellationToken).ConfigureAwait(false);
        if (level.Campaign is { Status: CampaignStatus.Archived } archived)
        {
            throw DomainErrors.CampaignArchived(archived.PublicId);
        }

        var role = await RoleAsync(request.Role!, cancellationToken).ConfigureAwait(false);

        // Measured in UTF-8 and named in one pass over the canonical form, so the size the cap refuses is the size
        // the chronicle reports and the hash two readers compute is the same hash.
        var document = (JsonObject)request.Note!.DeepClone();
        var measured = CanonicalJson.MeasureInUtf8(document)!.Value;
        if (measured.Bytes > MaxNoteBytes)
        {
            throw DomainErrors.RoleNoteTooLarge(measured.Bytes, MaxNoteBytes);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var note = await At(db.RoleNotes, level)
            .FirstOrDefaultAsync(n => n.Role == role, cancellationToken)
            .ConfigureAwait(false);

        var before = note is null ? null : new JsonObject { ["bytes"] = note.NoteBytes, ["hash"] = note.NoteHash };
        var inserting = note is null;

        if (note is null)
        {
            note = new RoleNote
            {
                CampaignId = level.Campaign?.Id,
                Campaign = level.Campaign,
                ProjectId = level.Project?.Id,
                Project = level.Project,
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
        // that did not, and a note is where a role keeps half-formed guesses about people. A campaign's note is a
        // line of that campaign; a project's or the client's is a line of the installation.
        journal.Append(
            db,
            actor,
            JournalKinds.RoleNoteSet,
            level.Campaign,
            key: role,
            old: before,
            updated: Receipt(level, role, measured.Bytes, measured.Hash),
            reason: Text(request.Reason));

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) when (inserting)
        {
            // Two sessions of one role wrote the *first* note at one level at once: that level's unique index
            // decided, and the loser is told to read the row and write again rather than handed a merge nobody asked
            // for. Only the insert can lose that race, so only the insert is answered this way — a failure on the
            // update path is some other problem, and calling it retryable would send a caller back for more of it.
            db.ChangeTracker.Clear();
            if (!await At(db.RoleNotes.AsNoTracking(), level)
                    .AnyAsync(n => n.Role == role, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw;
            }

            throw DomainErrors.ConcurrentUpdate();
        }

        return ToDto(note, level);
    }

    /// <summary>
    /// One level's memory, without any of it being read: which roles have written, how much and when. Scoped to
    /// one level on purpose — "every note anywhere" is not a question anybody asks, and it is a way to sweep up
    /// what roles have written about people. Naming no campaign and no project is the client's level, not all of them.
    /// </summary>
    public async Task<Page<RoleNoteSummaryDto>> ListAsync(RoleNoteListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new ValidationErrors();
        RoleNoteValidation.ValidateList(request, errors);
        errors.ThrowIfAny();

        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var level = await LevelAsync(request.CampaignId, request.ProjectId, cancellationToken).ConfigureAwait(false);

        var query = At(db.RoleNotes.AsNoTracking(), level);
        if (after is not null)
        {
            query = query.Where(n => string.Compare(n.Role, after) > 0);
        }

        var fetched = await query.OrderBy(n => n.Role).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, n => n.Role, note => ToSummary(note, level));
    }

    /// <summary>
    /// The one level a request names, loaded: a campaign, a project, or — naming neither — the client. Validation has
    /// already refused a request that names both, or names one by a blank id.
    /// </summary>
    private async Task<Level> LevelAsync(string? campaignId, string? projectId, CancellationToken cancellationToken)
    {
        if (campaignId is not null)
        {
            return new Level(await CampaignService.LoadAsync(db, campaignId, cancellationToken).ConfigureAwait(false), null);
        }

        return projectId is not null
            ? new Level(null, await ProjectService.LoadAsync(db, projectId.Trim(), cancellationToken).ConfigureAwait(false))
            : Level.Client;
    }

    /// <summary>
    /// The notes of one level. Each condition is the one its level's index of one note per role is filtered on, or
    /// implies it, which is what SQLite needs to see before it reads a partial index.
    /// </summary>
    private static IQueryable<RoleNote> At(IQueryable<RoleNote> notes, Level level)
    {
        if (level.Campaign is { } campaign)
        {
            var campaignKey = campaign.Id;
            return notes.Where(n => n.CampaignId == campaignKey);
        }

        if (level.Project is { } project)
        {
            var projectKey = project.Id;
            return notes.Where(n => n.ProjectId == projectKey);
        }

        return notes.Where(n => n.CampaignId == null && n.ProjectId == null);
    }

    /// <summary>A note exists only for a role the roster knows: a typo would otherwise become a memory nobody reads.</summary>
    private async Task<string> RoleAsync(string claimed, CancellationToken cancellationToken)
    {
        var role = claimed.Trim();
        return await db.Roles.AsNoTracking().AnyAsync(r => r.Name == role, cancellationToken).ConfigureAwait(false)
            ? role
            : throw DomainErrors.RoleNotFound(role);
    }

    /// <summary>
    /// The chronicle's account of a write: where the note is, whose it is, its size and its hash. A campaign's line has
    /// named the campaign since notes existed; a project's names the project; the client's names neither, which is what
    /// says it is the client's.
    /// </summary>
    private static JsonObject Receipt(Level level, string role, long bytes, string hash)
    {
        var receipt = new JsonObject();
        if (level.CampaignId is { } campaign)
        {
            receipt["campaign_id"] = campaign;
        }
        else if (level.ProjectId is { } project)
        {
            receipt["project_id"] = project;
        }

        receipt["role"] = role;
        receipt["bytes"] = bytes;
        receipt["hash"] = hash;
        return receipt;
    }

    private static RoleNoteDto Empty(Level level, string role) =>
        new(level.CampaignId, level.ProjectId, role, [], null, 0, null, null, null);

    private static RoleNoteDto ToDto(RoleNote note, Level level) =>
        new(
            level.CampaignId,
            level.ProjectId,
            note.Role,
            note.Note,
            note.NoteHash,
            note.NoteBytes,
            Moment(note.CreatedAt),
            Moment(note.UpdatedAt),
            new ActorRef(note.UpdatedByType, note.UpdatedById));

    private static RoleNoteSummaryDto ToSummary(RoleNote note, Level level) =>
        new(
            level.CampaignId,
            level.ProjectId,
            note.Role,
            note.NoteHash,
            note.NoteBytes,
            Moment(note.CreatedAt),
            Moment(note.UpdatedAt),
            new ActorRef(note.UpdatedByType, note.UpdatedById));

    private static DateTimeOffset Moment(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Where a note is: a campaign's, a project's, or — naming neither — the client's.</summary>
    private sealed record Level(Campaign? Campaign, Project? Project)
    {
        public static Level Client { get; } = new(null, null);

        public string? CampaignId => Campaign?.PublicId;

        public string? ProjectId => Project?.PublicId;
    }
}
