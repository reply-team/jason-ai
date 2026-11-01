using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Campaigns;
using Jason.Runtime.Contacts;
using Jason.Runtime.Domain;
using Jason.Runtime.Harness;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Projects;

/// <summary>
/// The projects a client's campaigns work toward, and the default one every installation has. A project is the runtime's
/// own: nothing here reaches a plugin. Every change is written to the chronicle in the same transaction as the change, as a
/// line of the installation's — a project is not a campaign — keyed by the project's id. A project made here gets its
/// folder under the harness root in the same request, and is not made when its folder cannot be.
/// </summary>
public sealed class ProjectService(JasonDbContext db, JournalWriter journal, TimeProvider clock, HarnessFolders folders)
{
    /// <summary>
    /// The default project, asked for by the filter of the index that holds there is one, word for word: SQLite reads a
    /// filtered index only for a query whose own condition implies the filter, and the condition EF Core writes for a
    /// boolean names the column alone, which implies nothing.
    /// </summary>
    internal const string DefaultQuery = "SELECT * FROM projects WHERE is_default = 1";

    public const int MaxNameLength = CampaignService.MaxNameLength;

    /// <summary>
    /// Makes a project. The slug, given or made from the name, is unique, so the same create sent again is refused as
    /// <c>project_exists</c> naming the project the first one made: that is the retry, and why there is no idempotency key.
    /// </summary>
    public async Task<ProjectDto> CreateAsync(ProjectCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ValidateName(request.Name, errors);
        ValidateReason(request.Reason, errors);
        var given = request.Slug is null ? null : ProjectSlug.Read(request.Slug, errors);
        errors.ThrowIfAny();

        var name = request.Name!.Trim();
        var publicId = PublicId.New("prj");
        var slug = given ?? ProjectSlug.From(name);
        if (await HolderAsync(slug, cancellationToken).ConfigureAwait(false) is { } holder)
        {
            throw DomainErrors.ProjectExists(slug, holder);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var project = new Project { PublicId = publicId, Name = name, Slug = slug, CreatedAt = now, UpdatedAt = now };

        // The row is written first and held uncommitted while the folder is made: the unique slug decides a race before
        // any folder exists, the write lock keeps two creates from choosing a folder name at once, and a folder that
        // cannot be made takes the row and its line back with it.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        db.Projects.Add(project);
        journal.Append(
            db,
            actor,
            JournalKinds.ProjectCreated,
            campaign: null,
            key: publicId,
            updated: new JsonObject { ["name"] = name, ["slug"] = slug },
            reason: NormalizeReason(request.Reason));

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Another create took the slug between the look above and this insert: the unique index is what decides, and
            // the caller is told what it would have been told had it come second. Anything else is not ours to swallow.
            db.ChangeTracker.Clear();
            if (await HolderAsync(slug, cancellationToken).ConfigureAwait(false) is { } winner)
            {
                throw DomainErrors.ProjectExists(slug, winner);
            }

            throw;
        }

        project.FolderPath = folders.MakeProjectFolder(project.PublicId, project.Slug);

        // Once the folder stands, the create is finished whether or not its caller is still there, as a campaign's is: a
        // row rolled back now would leave a folder marked for nobody, and that folder would refuse the same create again.
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
        return ProjectMapper.ToDto(project);
    }

    /// <summary>
    /// Where a campaign of <paramref name="project"/> goes, with nothing made: the folder recorded for it, which has to be
    /// there still and carry its marker, or where the policy would make one.
    /// </summary>
    internal static string PlannedFolder(Project project, HarnessFolders folders)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(folders);
        return project.FolderPath is { } recorded
            ? folders.RequireProjectFolder(project.PublicId, recorded)
            : folders.ProjectFolderOf(project.Slug);
    }

    /// <summary>
    /// A project's first need: the folder a campaign of it is made in or moved to. A recorded one has to be there still
    /// and carry its marker. Where none is recorded, the policy makes one now and records it on the tracked project in a
    /// step of its own, saved before anything of the campaign's is written — the caller has nothing else pending — so the
    /// folder stands, and is recorded, whatever the campaign's request then meets.
    /// </summary>
    internal static async Task<string> FirstNeedAsync(JasonDbContext db, Project project, HarnessFolders folders)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(folders);
        if (project.FolderPath is { } recorded)
        {
            return folders.RequireProjectFolder(project.PublicId, recorded);
        }

        // Once the folder stands its path is recorded, whether or not the caller is still there to hear it.
        var made = folders.MakeProjectFolder(project.PublicId, project.Slug);
        project.FolderPath = made;
        await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
        return made;
    }

    public async Task<ProjectDto> GetAsync(ProjectGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var id = Trimmed(request.ProjectId);
        var slug = Trimmed(request.Slug);
        if (id is null && slug is null)
        {
            throw new ValidationException([new ErrorDetail("project_id", "required", "project_id or slug is required.")]);
        }

        if (id is not null && slug is not null)
        {
            throw new ValidationException([new ErrorDetail("slug", "not_allowed", "name the project by project_id or by slug, not both.")]);
        }

        var project = id is not null
            ? await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.PublicId == id, cancellationToken).ConfigureAwait(false)
                ?? throw DomainErrors.ProjectNotFound(id)
            : await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug, cancellationToken).ConfigureAwait(false)
                ?? throw DomainErrors.ProjectSlugNotFound(slug!);
        return ProjectMapper.ToDto(project);
    }

    /// <summary>Every project, in the order they were made — the default first on an installation that has made none since.</summary>
    public async Task<Page<ProjectDto>> ListAsync(ProjectListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = string.IsNullOrWhiteSpace(request.Query) ? null : request.Query.Trim();
        if (text is { Length: > ContactSearch.MaxLength })
        {
            throw new ValidationException(
                [new ErrorDetail("query", "too_long", string.Create(CultureInfo.InvariantCulture, $"query must be at most {ContactSearch.MaxLength} characters."))]);
        }

        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.Projects.AsNoTracking();
        if (text is not null)
        {
            var pattern = TextFold.Containing(text);
            query = query.Where(p => EF.Functions.Like(TextFold.Fold(p.Name), pattern, TextFold.Escape));
        }

        if (after is not null)
        {
            query = query.Where(p => string.Compare(p.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(p => p.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, p => p.PublicId, ProjectMapper.ToDto);
    }

    /// <summary>
    /// Renames a project. Only the name changes: the slug a person types and the folder it names stay as they are. Asking
    /// for the name a project already has writes nothing, so a retry after a lost answer is never a second line.
    /// </summary>
    public async Task<ProjectDto> UpdateAsync(ProjectUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ValidateReason(request.Reason, errors);
        if (request.Name.IsSet)
        {
            ValidateName(request.Name.Value, errors);
        }

        var id = Trimmed(request.ProjectId);
        if (id is null)
        {
            errors.Add("project_id", "required", "project_id is required.");
        }

        errors.ThrowIfAny();

        var project = await LoadAsync(db, id!, cancellationToken).ConfigureAwait(false);
        var name = request.Name.IsSet ? request.Name.Value!.Trim() : project.Name;
        if (string.Equals(project.Name, name, StringComparison.Ordinal))
        {
            return ProjectMapper.ToDto(project);
        }

        journal.Append(
            db,
            actor,
            JournalKinds.ProjectUpdated,
            campaign: null,
            key: project.PublicId,
            old: new JsonObject { ["name"] = project.Name },
            updated: new JsonObject { ["name"] = name },
            reason: NormalizeReason(request.Reason));
        project.Name = name;
        project.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ProjectMapper.ToDto(project);
    }

    /// <summary>A project by its public id, tracked; 404 when there is none. What a campaign names its project by.</summary>
    internal static async Task<Project> LoadAsync(JasonDbContext db, string publicId, CancellationToken cancellationToken) =>
        await db.Projects.FirstOrDefaultAsync(p => p.PublicId == publicId, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.ProjectNotFound(publicId);

    /// <summary>
    /// The default project, which the migration that brought projects in made on every installation. One that is gone was
    /// taken away by hand — no operation removes it — and is refused for good, saying what to do instead: asking again
    /// would find it gone again.
    /// </summary>
    internal static async Task<Project> DefaultAsync(JasonDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.Projects.FromSqlRaw(DefaultQuery).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.NoDefaultProject();
    }

    /// <summary>The id of the project that holds a slug, or null when none does.</summary>
    private Task<string?> HolderAsync(string slug, CancellationToken cancellationToken) =>
        db.Projects.AsNoTracking().Where(p => p.Slug == slug).Select(p => p.PublicId).FirstOrDefaultAsync(cancellationToken);

    private static void ValidateName(string? name, ValidationErrors errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("name", "required", "name is required.");
            return;
        }

        if (name.Trim().Length > MaxNameLength)
        {
            errors.Add("name", "too_long", string.Create(CultureInfo.InvariantCulture, $"name must be at most {MaxNameLength} characters."));
        }
    }

    private static void ValidateReason(string? reason, ValidationErrors errors)
    {
        if (reason is not null && reason.Trim().Length > CampaignService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {CampaignService.MaxReasonLength} characters."));
        }
    }

    private static string? NormalizeReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
