using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Profiles;

/// <summary>
/// The registry of execution profiles: named, non-secret descriptions of launchable agent hosts. A profile has
/// two halves, and the difference between them is the whole point. The row carries what is meant to move —
/// which revision is in force, and whether the profile is in service. Everything behavioural lives in a
/// revision, and an edit appends a new one rather than changing the one that is there.
/// <para>
/// There is no delete. An attempt records the revision it ran under for ever, so a profile somebody's history
/// points at is not the runtime's to remove; a profile that should not be used again is disabled.
/// </para>
/// </summary>
public sealed class ProfileService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    public const string IdPrefix = "prf";

    /// <summary>Caps on what one profile may carry. Constants rather than settings: a limit an operator can raise
    /// is a limit somebody will raise, and none of these is an installation's choice.</summary>
    public const int MaxNameLength = 64;

    public const int MaxDescriptionLength = 500;

    public const int MaxProgramLength = 500;

    public const int MaxArgs = 64;

    public const int MaxArgLength = 1000;

    public const int MaxDenyEntries = 64;

    public const int MaxDenyEntryLength = 1000;

    public const int MaxCliCommandLength = 200;

    public const int MaxHostVersionLength = 100;

    public const int MaxReasonLength = 2000;

    /// <summary>
    /// What a launched agent types to call home when its profile named nothing: this program's own name, which
    /// is what the runtime puts within a child's reach. Read once, because it is a fact about this process.
    /// </summary>
    public static string DefaultCliCommand { get; } = Path.GetFileNameWithoutExtension(SelfExecutable.Command[^1]);

    /// <summary>A profile and the first revision of it, written together: a profile without a revision would say
    /// nothing about how to launch anything.</summary>
    public async Task<ExecutionProfileDto> CreateAsync(ProfileCreateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ProfileValidation.ValidateCreate(request, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var name = request.Name!.Trim();
        if (await db.ExecutionProfiles.AnyAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false))
        {
            throw DomainErrors.ProfileExists(name);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var revision = new ExecutionProfileRevision
        {
            Number = 1,
            Host = ProfileValidation.ParseHost(request.Host)!.Value,
            Program = request.Program!.Trim(),
            Args = ProfileValidation.Strings(request.Args),
            Deny = ProfileValidation.Strings(request.Deny),
            CliCommand = Text(request.CliCommand),
            HostVersionVerified = Text(request.HostVersionVerified),
            CreatedByType = actor.Type,
            CreatedById = actor.Id,
            CreatedAt = now,
        };

        var profile = new ExecutionProfile
        {
            PublicId = PublicId.New(IdPrefix),
            Name = name,
            Description = Text(request.Description),
            CurrentRevision = revision.Number,
            CreatedAt = now,
            UpdatedAt = now,
        };

        profile.Revisions.Add(revision);
        db.ExecutionProfiles.Add(profile);

        // A registry entry belongs to no campaign, like a role: the chronicle records it globally.
        journal.Append(db, actor, JournalKinds.ProfileCreated, campaign: null, key: name, updated: Describe(revision), reason: Text(request.Reason));

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Somebody registered the name first. The unique index is what decides, not the read above it, and
            // the caller is told the same thing either way. Anything else is not ours to swallow.
            db.ChangeTracker.Clear();
            if (!await Exists(name, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            throw DomainErrors.ProfileExists(name);
        }

        return ProfileMapper.ToDto(profile, revision);
    }

    /// <summary>
    /// An edit, which is a new revision. What the patch does not name is copied from the revision in force, so
    /// revision 7 is a whole statement of how to launch a host rather than something to be reassembled from the
    /// six rows before it — and the six rows before it still say exactly what they said.
    /// </summary>
    public async Task<ExecutionProfileDto> UpdateAsync(ProfileUpdateRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ProfileValidation.ValidateUpdate(request, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var name = request.Name!.Trim();
        var profile = await Tracked(name, cancellationToken).ConfigureAwait(false);
        var current = await RevisionAsync(profile, profile.CurrentRevision, cancellationToken).ConfigureAwait(false);

        var named = Named(request);
        if (named.Count == 0)
        {
            // A patch that names nothing asked for nothing. Appending a revision for it would move a number that
            // attempts record, to say that somebody called the verb.
            return ProfileMapper.ToDto(profile, current);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var next = new ExecutionProfileRevision
        {
            ProfileId = profile.Id,
            Number = profile.CurrentRevision + 1,
            Host = request.Host.IsSet ? ProfileValidation.ParseHost(request.Host.Value)!.Value : current.Host,
            Program = request.Program.IsSet ? request.Program.Value!.Trim() : current.Program,
            Args = request.Args.IsSet ? ProfileValidation.Strings(request.Args.Value) : [.. current.Args],
            Deny = request.Deny.IsSet ? ProfileValidation.Strings(request.Deny.Value) : [.. current.Deny],
            CliCommand = request.CliCommand.IsSet ? Text(request.CliCommand.Value) : current.CliCommand,
            HostVersionVerified = request.HostVersionVerified.IsSet ? Text(request.HostVersionVerified.Value) : current.HostVersionVerified,
            CreatedByType = actor.Type,
            CreatedById = actor.Id,
            CreatedAt = now,
        };

        db.ExecutionProfileRevisions.Add(next);
        if (request.Description.IsSet)
        {
            profile.Description = Text(request.Description.Value);
        }

        profile.CurrentRevision = next.Number;
        profile.UpdatedAt = now;

        journal.Append(
            db,
            actor,
            JournalKinds.ProfileRevised,
            campaign: null,
            key: name,
            old: new JsonObject { ["revision"] = current.Number },
            updated: new JsonObject { ["revision"] = next.Number, ["fields"] = new JsonArray([.. named.Select(field => (JsonNode?)JsonValue.Create(field))]) },
            reason: Text(request.Reason));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ProfileMapper.ToDto(profile, next);
    }

    /// <summary>One profile, at the revision in force or at the one asked for.</summary>
    public async Task<ExecutionProfileDto> GetAsync(ProfileGetRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new ValidationErrors();
        ProfileValidation.ValidateGet(request, errors);
        errors.ThrowIfAny();

        var profile = await Loaded(request.Name!.Trim(), cancellationToken).ConfigureAwait(false);
        var revision = await RevisionAsync(profile, request.Revision ?? profile.CurrentRevision, cancellationToken).ConfigureAwait(false);
        return ProfileMapper.ToDto(profile, revision);
    }

    /// <summary>
    /// The registry, ordered by public id — the order the profiles were added. A disabled profile is out of
    /// service and left out unless it is asked for: the question a listing answers is "what can run work now".
    /// </summary>
    public async Task<Page<ExecutionProfileDto>> ListAsync(ProfileListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.ExecutionProfiles.AsNoTracking();
        if (request.IncludeDisabled != true)
        {
            query = query.Where(p => p.DisabledAt == null);
        }

        if (after is not null)
        {
            query = query.Where(p => string.Compare(p.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(p => p.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        var revisions = await CurrentRevisionsAsync(fetched, cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, p => p.PublicId, profile => ProfileMapper.ToDto(profile, revisions[profile.Id]));
    }

    /// <summary>Out of service. The revisions stay exactly where they are; what changes is whether new work resolves to them.</summary>
    public Task<ExecutionProfileDto> DisableAsync(ProfileToggleRequest request, CancellationToken cancellationToken) =>
        ToggleAsync(request, disable: true, cancellationToken);

    public Task<ExecutionProfileDto> EnableAsync(ProfileToggleRequest request, CancellationToken cancellationToken) =>
        ToggleAsync(request, disable: false, cancellationToken);

    /// <summary>
    /// Both toggles, which are the same act in two directions. Setting a profile to the state it is already in
    /// changes nothing and is written down nowhere: the chronicle records what happened, and nothing did.
    /// </summary>
    private async Task<ExecutionProfileDto> ToggleAsync(ProfileToggleRequest request, bool disable, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        ProfileValidation.ValidateToggle(request, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var name = request.Name!.Trim();
        var profile = await Tracked(name, cancellationToken).ConfigureAwait(false);
        var revision = await RevisionAsync(profile, profile.CurrentRevision, cancellationToken).ConfigureAwait(false);
        if (profile.DisabledAt is not null == disable)
        {
            return ProfileMapper.ToDto(profile, revision);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        profile.DisabledAt = disable ? now : null;
        profile.UpdatedAt = now;
        journal.Append(
            db,
            actor,
            disable ? JournalKinds.ProfileDisabled : JournalKinds.ProfileEnabled,
            campaign: null,
            key: name,
            reason: Text(request.Reason));
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ProfileMapper.ToDto(profile, revision);
    }

    /// <summary>
    /// The current revision of every profile on one page, in one query. A profile's revisions are unbounded and
    /// a listing needs exactly one of each, so the rows are narrowed by both keys and matched exactly here.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, ExecutionProfileRevision>> CurrentRevisionsAsync(
        IReadOnlyList<ExecutionProfile> profiles,
        CancellationToken cancellationToken)
    {
        if (profiles.Count == 0)
        {
            return new Dictionary<int, ExecutionProfileRevision>();
        }

        var ids = profiles.Select(p => p.Id).ToList();
        var numbers = profiles.Select(p => p.CurrentRevision).Distinct().ToList();
        var fetched = await db.ExecutionProfileRevisions.AsNoTracking()
            .Where(r => ids.Contains(r.ProfileId) && numbers.Contains(r.Number))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        return profiles.ToDictionary(
            profile => profile.Id,
            profile => fetched.Single(r => r.ProfileId == profile.Id && r.Number == profile.CurrentRevision));
    }

    private async Task<ExecutionProfileRevision> RevisionAsync(ExecutionProfile profile, int number, CancellationToken cancellationToken) =>
        await db.ExecutionProfileRevisions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProfileId == profile.Id && r.Number == number, cancellationToken).ConfigureAwait(false)
        ?? throw DomainErrors.ProfileRevisionNotFound(profile.Name, number);

    private async Task<ExecutionProfile> Loaded(string name, CancellationToken cancellationToken) =>
        await db.ExecutionProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false)
        ?? throw DomainErrors.ProfileNotFound(name);

    /// <summary>The same row, tracked, for the two verbs that change it.</summary>
    private async Task<ExecutionProfile> Tracked(string name, CancellationToken cancellationToken) =>
        await db.ExecutionProfiles.FirstOrDefaultAsync(p => p.Name == name, cancellationToken).ConfigureAwait(false)
        ?? throw DomainErrors.ProfileNotFound(name);

    private Task<bool> Exists(string name, CancellationToken cancellationToken) =>
        db.ExecutionProfiles.AsNoTracking().AnyAsync(p => p.Name == name, cancellationToken);

    /// <summary>Which fields the patch named, in the order the profile states them: the journal line's own list.</summary>
    private static IReadOnlyList<string> Named(ProfileUpdateRequest request)
    {
        var fields = new List<string>();
        Add(request.Description.IsSet, "description");
        Add(request.Host.IsSet, "host");
        Add(request.Program.IsSet, "program");
        Add(request.Args.IsSet, "args");
        Add(request.Deny.IsSet, "deny");
        Add(request.CliCommand.IsSet, "cli_command");
        Add(request.HostVersionVerified.IsSet, "host_version_verified");
        return fields;

        void Add(bool named, string field)
        {
            if (named)
            {
                fields.Add(field);
            }
        }
    }

    /// <summary>
    /// What a chronicle line says about a revision: which host, what it was verified against, and the number.
    /// Not the arguments — the profile is where what to run is kept, and a line is a note that it changed.
    /// </summary>
    private static JsonObject Describe(ExecutionProfileRevision revision) => new()
    {
        ["revision"] = revision.Number,
        ["host"] = SnakeCaseEnumConverter<AgentHostKind>.Format(revision.Host),
        ["program"] = revision.Program,
        ["host_version_verified"] = revision.HostVersionVerified,
    };

    /// <summary>Trimmed, and absent where a caller sent nothing but space.</summary>
    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
