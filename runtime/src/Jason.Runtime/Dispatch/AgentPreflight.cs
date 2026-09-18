using Jason.Contracts.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jason.Runtime.Dispatch;

/// <summary>What the claim decided about which host runs one piece of agent work.</summary>
public abstract record AgentVerdict
{
    /// <summary>A profile resolved and can be run: the revision to launch, and which level chose it.</summary>
    /// <param name="LineageRevision">
    /// The revision the item inherited, where lineage chose. Inherited work runs the profile as it stands now,
    /// so this is kept beside the revision that ran and a difference between them can be read.
    /// </param>
    public sealed record Launch(
        ExecutionProfile Profile,
        ExecutionProfileRevision Revision,
        ProfileResolutionSource Source,
        int? LineageRevision) : AgentVerdict;

    /// <summary>
    /// No profile is configured at any level. The role's own entry command runs the work, exactly as every agent
    /// attempt ran before profiles existed — and the attempt records that this is what happened.
    /// </summary>
    public sealed record Legacy : AgentVerdict;

    /// <summary>The work cannot run, and this is the one reason it could not.</summary>
    public sealed record Refused(string Code, string Message) : AgentVerdict;
}

/// <summary>
/// Which execution profile runs one agent work item, decided at the claim and before any child process exists —
/// the same rule the twelve provider checks follow, for the same reason: an item that cannot run should say so
/// with an attempt and a code somebody can act on, rather than be skipped and look claimable for ever.
/// <para>
/// The order itself lives in <see cref="ProfileResolution"/>, which is pure. What this adds is the part that
/// needs the database and the settings: whether the profile a level named actually exists, and whether it is
/// still in service.
/// </para>
/// </summary>
public sealed class AgentPreflight(LiveSettings<RolesOptions> roles)
{
    public async Task<AgentVerdict> DecideAsync(JasonDbContext db, WorkItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(item);

        // A role policy is a property of the role, which the item names rather than holds.
        var rolePolicy = item.Role is { } role
            ? await db.Roles.AsNoTracking()
                .Where(r => r.Name == role)
                .Select(r => r.ExecutionProfile)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false)
            : null;

        var decision = ProfileResolution.Decide(new ProfileInputs(
            item.ExecutionProfile,
            item.Campaign?.ExecutionProfile,
            rolePolicy,
            item.LineageState,
            item.LineageProfileName,
            item.LineageProfileRevision,
            roles.Current.DefaultExecutionProfile));

        switch (decision)
        {
            case ProfileDecision.None:
                return new AgentVerdict.Legacy();

            case ProfileDecision.Block block:
                return new AgentVerdict.Refused(block.Code, block.Message);

            case ProfileDecision.Use use:
                return await ResolveAsync(db, use, cancellationToken).ConfigureAwait(false);

            default:
                throw new InvalidOperationException($"Unknown profile decision '{decision.GetType().Name}'.");
        }
    }

    private static async Task<AgentVerdict> ResolveAsync(JasonDbContext db, ProfileDecision.Use use, CancellationToken cancellationToken)
    {
        var profile = await db.ExecutionProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == use.Name, cancellationToken)
            .ConfigureAwait(false);

        // Named by a level that could not check, or named when it existed and gone since. Either way the work
        // stops here rather than being run by something nobody chose.
        if (profile is null)
        {
            return new AgentVerdict.Refused(
                AttemptErrors.ProfileNotFound,
                $"The execution profile '{use.Name}' does not exist; it was chosen by {Source(use.Source)}.");
        }

        if (profile.DisabledAt is not null)
        {
            return new AgentVerdict.Refused(
                AttemptErrors.ProfileDisabled,
                $"The execution profile '{use.Name}' is disabled; it was chosen by {Source(use.Source)}. Enable it, or name another.");
        }

        // The revision in force now, not the one an ancestor ran: a profile repaired since is a profile whose
        // repair reaches the work that inherited it. What was inherited is kept beside it on the attempt.
        var revision = await db.ExecutionProfileRevisions.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ProfileId == profile.Id && r.Number == profile.CurrentRevision, cancellationToken)
            .ConfigureAwait(false);
        if (revision is null)
        {
            return new AgentVerdict.Refused(
                AttemptErrors.ProfileNotFound,
                $"The execution profile '{use.Name}' has no revision {profile.CurrentRevision} to run.");
        }

        return new AgentVerdict.Launch(profile, revision, use.Source, use.LineageRevision);
    }

    /// <summary>Which level chose it, in the words a person would use, because that is where the repair is.</summary>
    private static string Source(ProfileResolutionSource source) => source switch
    {
        ProfileResolutionSource.WorkItemOverride => "the work item itself",
        ProfileResolutionSource.CampaignPolicy => "its campaign's policy",
        ProfileResolutionSource.RolePolicy => "its role's policy",
        ProfileResolutionSource.Lineage => "the run that created it",
        _ => "the configured default, Roles:DefaultExecutionProfile",
    };
}
