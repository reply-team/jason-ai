using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Roles;

/// <summary>
/// The registry of roles. A role is a job description, not a process: the runtime keeps the name, what the role
/// is for and — where one is known — the command that starts an agent host for it. The nine builtins arrive with
/// the database; anything else is added here and, for now, never changed again.
/// </summary>
public sealed class RoleService(JasonDbContext db, JournalWriter journal, TimeProvider clock)
{
    public const int MaxNameLength = 64;

    public const int MaxDescriptionLength = 500;

    public const int MaxEntryCommandArgs = 64;

    public const int MaxEntryCommandArgLength = 1000;

    public const int MaxProfileDefaultsBytes = 16 * 1024;

    public const int MaxReasonLength = 2000;

    /// <summary>
    /// Ordered by public id, which puts the seeded roster first in the order it was written and every later
    /// role after it in the order it was added — the order an agent reading the registry would expect.
    /// </summary>
    public async Task<Page<RoleDto>> ListAsync(RoleListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limit = Paging.ResolveLimit(request.Limit);
        var after = Paging.DecodeCursor(request.Cursor);

        var query = db.Roles.AsNoTracking();
        if (after is not null)
        {
            query = query.Where(r => string.Compare(r.PublicId, after) > 0);
        }

        var fetched = await query.OrderBy(r => r.PublicId).Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        return Paging.ToPage(fetched, limit, r => r.PublicId, RoleMapper.ToDto);
    }

    public async Task<RoleDto> AddAsync(RoleAddRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        RoleValidation.ValidateAdd(request, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var name = request.Name!.Trim();
        var profile = await ExecutionProfilePolicy
            .ResolveAsync(db, "execution_profile", request.ExecutionProfile, errors, cancellationToken)
            .ConfigureAwait(false);
        errors.ThrowIfAny();

        // Builtin names are taken too: the roster is the runtime's own vocabulary, and shadowing a role would
        // make every item that names it ambiguous.
        if (await db.Roles.AnyAsync(r => r.Name == name, cancellationToken).ConfigureAwait(false))
        {
            throw DomainErrors.RoleExists(name);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var entryCommand = request.EntryCommand is null ? [] : request.EntryCommand.Select(argument => argument.Trim()).ToList();
        var role = new Role
        {
            PublicId = PublicId.New("rol"),
            Name = name,
            Builtin = false,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            EntryCommand = entryCommand,
            ProfileDefaults = request.ProfileDefaults?.DeepClone().AsObject() ?? new JsonObject(),
            ExecutionProfile = profile,
            CreatedAt = now,
            UpdatedAt = now,
        };

        db.Roles.Add(role);

        // A registry entry belongs to no campaign: the chronicle records it globally, and what matters later is
        // whether the role could be launched at all.
        journal.Append(
            db,
            actor,
            JournalKinds.RoleAdded,
            campaign: null,
            key: name,
            updated: new JsonObject { ["entry_command_present"] = JsonValue.Create(entryCommand.Count > 0) },
            reason: string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RoleMapper.ToDto(role);
    }

    /// <summary>
    /// Points a role at an execution profile, or takes the policy away. This is the only thing a registered role
    /// can be told to change: which host a kind of worker uses is a decision somebody revisits, and the rest of
    /// a job description is not, so there is no general role update verb to fold this into.
    /// </summary>
    public async Task<RoleDto> SetProfileAsync(RoleSetProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);

        var errors = new ValidationErrors();
        RoleValidation.ValidateSetProfile(request, errors);
        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);

        var name = request.Name!.Trim();
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == name, cancellationToken).ConfigureAwait(false)
            ?? throw DomainErrors.RoleNotFound(name);

        if (!request.ExecutionProfile.IsSet)
        {
            return RoleMapper.ToDto(role);
        }

        var profile = await ExecutionProfilePolicy
            .ResolveAsync(db, "execution_profile", request.ExecutionProfile.Value, errors, cancellationToken)
            .ConfigureAwait(false);
        errors.ThrowIfAny();

        // Asking a role for the profile it already uses writes nothing, so a repeated call is not a second
        // event in the chronicle.
        if (string.Equals(role.ExecutionProfile, profile, StringComparison.Ordinal))
        {
            return RoleMapper.ToDto(role);
        }

        var previous = role.ExecutionProfile;
        role.ExecutionProfile = profile;
        role.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        // A role belongs to no campaign, so the chronicle records the change globally and under the role's own
        // name — the same key the registration was written under.
        journal.Append(
            db,
            actor,
            JournalKinds.RoleUpdated,
            campaign: null,
            key: name,
            old: JsonValue.Create(previous),
            updated: JsonValue.Create(profile),
            reason: string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim());
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return RoleMapper.ToDto(role);
    }
}
