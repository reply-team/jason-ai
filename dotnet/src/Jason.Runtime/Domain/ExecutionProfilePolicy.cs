using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Domain;

/// <summary>
/// What a campaign or a role is allowed to say about which host its agent work uses. The policy is a name, and
/// the only thing asked of it here is that something answers it: a campaign or a role pointed at a profile that
/// does not exist would refuse every claim afterwards, with nothing but a work item to say why and nobody near
/// the person who typed the name.
/// </summary>
/// <remarks>
/// Whether the profile can actually run is a different question with a different answer: a disabled profile is
/// still a profile, and what it means for work is decided at the claim rather than by refusing to write the
/// policy down. The global default in settings is the one level that cannot be checked at all, because settings
/// are read before the database is open.
/// </remarks>
public static class ExecutionProfilePolicy
{
    /// <summary>
    /// The name to store, or null to store nothing. A blank name is nothing rather than a name, which is how
    /// the rest of the runtime reads a blank optional field.
    /// </summary>
    public static async Task<string?> ResolveAsync(
        JasonDbContext db,
        string field,
        string? name,
        ValidationErrors errors,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(errors);

        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var profile = name.Trim();
        if (!await db.ExecutionProfiles.AnyAsync(candidate => candidate.Name == profile, cancellationToken).ConfigureAwait(false))
        {
            errors.Add(field, "unknown", $"{field} must name an execution profile this runtime knows; create it with profile.create first.");
            return null;
        }

        return profile;
    }
}
