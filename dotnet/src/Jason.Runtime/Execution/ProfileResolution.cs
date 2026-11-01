using Jason.Contracts.Api;

namespace Jason.Runtime.Execution;

/// <summary>
/// Every level of the order in one value, read off the work item, its campaign, its role and the settings before
/// anything is decided.
/// </summary>
/// <param name="Override">The profile the work item names for itself.</param>
/// <param name="CampaignPolicy">The profile its campaign puts on the work it owns.</param>
/// <param name="RolePolicy">The profile the role carries wherever it works.</param>
/// <param name="Lineage">What the run that caused this work handed down, as the item materialized it.</param>
/// <param name="LineageName">The inherited profile's name, where one was inherited.</param>
/// <param name="LineageRevision">The revision it was inherited at, which is kept beside the one that runs.</param>
/// <param name="GlobalDefault">The house default: what root work runs under when nobody said otherwise.</param>
public sealed record ProfileInputs(
    string? Override, string? CampaignPolicy, string? RolePolicy,
    LineageState Lineage, string? LineageName, int? LineageRevision, string? GlobalDefault);

/// <summary>
/// What the order decided: a profile to use, a refusal, or nothing at all. <see cref="None"/> is not a refusal —
/// no profile is configured anywhere, and the claim falls back to the role's own entry command, which is how
/// every agent attempt ran before profiles existed.
/// </summary>
public abstract record ProfileDecision
{
    /// <param name="LineageRevision">
    /// The revision the item inherited, present only where lineage decided. Inherited work runs the profile as
    /// it stands now rather than as it stood when the ancestor ran, so both numbers are kept and the difference
    /// can be read instead of guessed.
    /// </param>
    public sealed record Use(ProfileResolutionSource Source, string Name, int? LineageRevision) : ProfileDecision;

    public sealed record Block(string Code, string Message) : ProfileDecision;

    public sealed record None : ProfileDecision;
}

/// <summary>
/// Which execution profile runs a piece of agent work, as a function anybody can read: the item's own override,
/// then the campaign's policy, then the role's policy, then what the item inherited, then the global default.
/// Pure — no database, no clock, no settings of its own — so the order is one readable thing rather than a
/// decision spread across the claim.
/// <para>
/// A campaign beats a role deliberately. A campaign is this runtime's unit of isolation and the operator's most
/// specific standing statement about a body of work; a role policy is a statement about a kind of worker
/// everywhere, and the narrower statement wins.
/// </para>
/// </summary>
public static class ProfileResolution
{
    /// <summary>What a person is told to do about work whose ancestry cannot be resolved.</summary>
    private const string Repair =
        "the run that created this work resolved no execution profile, so there is none to inherit. "
        + "Name one in the work item's execution_profile, or give its campaign or its role a profile policy.";

    public static ProfileDecision Decide(ProfileInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        if (Named(inputs.Override) is { } chosen)
        {
            return new ProfileDecision.Use(ProfileResolutionSource.WorkItemOverride, chosen, null);
        }

        if (Named(inputs.CampaignPolicy) is { } campaign)
        {
            return new ProfileDecision.Use(ProfileResolutionSource.CampaignPolicy, campaign, null);
        }

        if (Named(inputs.RolePolicy) is { } role)
        {
            return new ProfileDecision.Use(ProfileResolutionSource.RolePolicy, role, null);
        }

        // The lineage level, and the only level that can refuse. Every level above it outranks the refusal, which
        // is what makes naming a profile — on the item, on the campaign or on the role — the repair.
        switch (inputs.Lineage)
        {
            case LineageState.Inherited when Named(inputs.LineageName) is { } inherited:
                return new ProfileDecision.Use(ProfileResolutionSource.Lineage, inherited, inputs.LineageRevision);

            // Inherited with no name to read is a contradiction, and the safe reading of a contradiction is the
            // one that refuses: falling through would be exactly the silent change of executor this level exists
            // to prevent.
            case LineageState.Inherited:
            case LineageState.Unresolved:
                return new ProfileDecision.Block(AttemptErrors.LineageResolutionUnsupported, Repair);

            // Root is work nobody's run caused — a person, a role, or the runtime itself — so the house default
            // is the right answer for it and must not block.
            default:
                break;
        }

        return Named(inputs.GlobalDefault) is { } house
            ? new ProfileDecision.Use(ProfileResolutionSource.GlobalDefault, house, null)
            : new ProfileDecision.None();
    }

    /// <summary>A level somebody left blank says nothing; it does not name a profile called nothing.</summary>
    private static string? Named(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
