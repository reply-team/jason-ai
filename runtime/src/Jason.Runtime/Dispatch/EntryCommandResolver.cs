using Jason.Runtime.Configuration;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Dispatch;

/// <summary>
/// How a role is started. The role's own command comes first; one command configured for every role is what
/// makes the whole builtin roster launchable from a single line of settings. Nothing else is invented: a role
/// nothing can start is a visible failure, not a silent skip.
/// </summary>
public sealed class EntryCommandResolver(IOptionsMonitor<RolesOptions> roles)
{
    public async Task<IReadOnlyList<string>?> ResolveAsync(JasonDbContext db, string roleName, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);

        var own = await db.Roles.AsNoTracking()
            .Where(r => r.Name == roleName)
            .Select(r => r.EntryCommand)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (own is { Count: > 0 })
        {
            return own;
        }

        var configured = roles.CurrentValue.DefaultEntryCommand;
        return configured.Count > 0 ? [.. configured] : null;
    }
}
