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
}
