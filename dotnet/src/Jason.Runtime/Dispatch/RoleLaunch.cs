using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.WorkItems;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// Whether the claim could launch a role's work that the runtime is about to create: a profile is resolved for it, enabled
/// or not, or an entry command is. Otherwise the claim would end it <c>role_not_launchable</c>, so the runtime creates
/// nothing. The one question the respond step asks before a responder and a create asks before the critic of a send.
/// </summary>
public static class RoleLaunch
{
    public static async Task<bool> ConfiguredAsync(
        JasonDbContext db,
        EntryCommandResolver entryCommands,
        RolesOptions roles,
        string role,
        string? campaignProfile,
        LineageRecord lineage,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entryCommands);
        ArgumentNullException.ThrowIfNull(roles);
        var rolePolicy = await db.Roles
            .AsNoTracking()
            .Where(r => r.Name == role)
            .Select(r => r.ExecutionProfile)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        var decision = ProfileResolution.Decide(new ProfileInputs(
            Override: null,
            campaignProfile,
            rolePolicy,
            lineage.State,
            lineage.ProfileName,
            lineage.ProfileRevision,
            roles.DefaultExecutionProfile));

        return decision is not ProfileDecision.None
            || await entryCommands.ResolveAsync(db, role, ct).ConfigureAwait(false) is not null;
    }
}
