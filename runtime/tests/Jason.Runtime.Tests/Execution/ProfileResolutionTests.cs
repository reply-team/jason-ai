using Jason.Contracts.Api;
using Jason.Runtime.Execution;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The order in which an agent attempt's execution profile is chosen, read as a function: the item's own
/// override, the campaign's policy, the role's policy, what the item inherited, the global default — and, where
/// a run caused this work and nothing about its profile can be read, a refusal instead of a quiet fallback.
/// </summary>
public class ProfileResolutionTests
{
    /// <summary>
    /// The whole point of the level: falling through to the global default here would change which AI executor
    /// runs somebody's work without anybody saying so.
    /// </summary>
    [Fact]
    public void An_unresolved_ancestry_blocks_instead_of_reaching_the_global_default()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs(null, null, null, LineageState.Unresolved, null, null, "house-default"));

        var block = Assert.IsType<ProfileDecision.Block>(decision);
        Assert.Equal(AttemptErrors.LineageResolutionUnsupported, block.Code);
        Assert.Contains("execution_profile", block.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_items_own_override_is_the_most_specific_thing_anybody_said()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs("named-here", "campaign-policy", "role-policy", LineageState.Inherited, "inherited", 4, "house-default"));

        var use = Assert.IsType<ProfileDecision.Use>(decision);
        Assert.Equal(ProfileResolutionSource.WorkItemOverride, use.Source);
        Assert.Equal("named-here", use.Name);
        Assert.Null(use.LineageRevision);
    }

    /// <summary>
    /// A campaign is this runtime's unit of isolation and the operator's most specific standing statement about
    /// a body of work; a role policy is a statement about a kind of worker everywhere.
    /// </summary>
    [Fact]
    public void A_campaign_policy_beats_a_role_policy()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs(null, "campaign-policy", "role-policy", LineageState.Root, null, null, "house-default"));

        var use = Assert.IsType<ProfileDecision.Use>(decision);
        Assert.Equal(ProfileResolutionSource.CampaignPolicy, use.Source);
        Assert.Equal("campaign-policy", use.Name);
    }

    [Fact]
    public void A_role_policy_decides_where_the_campaign_has_said_nothing()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs(null, null, "role-policy", LineageState.Root, null, null, "house-default"));

        var use = Assert.IsType<ProfileDecision.Use>(decision);
        Assert.Equal(ProfileResolutionSource.RolePolicy, use.Source);
        Assert.Equal("role-policy", use.Name);
    }

    /// <summary>
    /// What the item inherited, with the revision it inherited — kept because inherited work runs the profile as
    /// it is now rather than as it was when the ancestor ran, and that difference is meant to be readable.
    /// </summary>
    [Fact]
    public void An_inherited_profile_decides_where_no_policy_does_and_carries_its_revision()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs(null, null, null, LineageState.Inherited, "inherited", 4, "house-default"));

        var use = Assert.IsType<ProfileDecision.Use>(decision);
        Assert.Equal(ProfileResolutionSource.Lineage, use.Source);
        Assert.Equal("inherited", use.Name);
        Assert.Equal(4, use.LineageRevision);
    }

    /// <summary>Root is work nobody's run caused, so the house default is exactly the right answer for it.</summary>
    [Fact]
    public void Root_work_reaches_the_global_default()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs(null, null, null, LineageState.Root, null, null, "house-default"));

        var use = Assert.IsType<ProfileDecision.Use>(decision);
        Assert.Equal(ProfileResolutionSource.GlobalDefault, use.Source);
        Assert.Equal("house-default", use.Name);
        Assert.Null(use.LineageRevision);
    }

    /// <summary>
    /// Nothing anywhere names a profile. That is not a refusal: the claim falls back to the role's own entry
    /// command, which is how every agent attempt ran before profiles existed.
    /// </summary>
    [Fact]
    public void Nothing_configured_anywhere_is_no_profile_rather_than_a_refusal()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs(null, null, null, LineageState.Root, null, null, null));

        Assert.IsType<ProfileDecision.None>(decision);
    }

    /// <summary>The documented repair: a higher level outranks the block, so the work runs normally.</summary>
    [Fact]
    public void An_override_runs_work_whose_ancestry_cannot_be_resolved()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs("named-here", null, null, LineageState.Unresolved, null, null, null));

        var use = Assert.IsType<ProfileDecision.Use>(decision);
        Assert.Equal(ProfileResolutionSource.WorkItemOverride, use.Source);
        Assert.Equal("named-here", use.Name);
    }

    [Fact]
    public void A_campaign_policy_repairs_unresolved_ancestry_as_well()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs(null, "campaign-policy", null, LineageState.Unresolved, null, null, null));

        var use = Assert.IsType<ProfileDecision.Use>(decision);
        Assert.Equal(ProfileResolutionSource.CampaignPolicy, use.Source);
    }

    [Fact]
    public void A_role_policy_repairs_unresolved_ancestry_as_well()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs(null, null, "role-policy", LineageState.Unresolved, null, null, null));

        var use = Assert.IsType<ProfileDecision.Use>(decision);
        Assert.Equal(ProfileResolutionSource.RolePolicy, use.Source);
    }

    /// <summary>
    /// An unresolved ancestry blocks where it is reached, and it is reached whether or not a global default
    /// exists: the block is about not knowing, not about having nothing to fall back on.
    /// </summary>
    [Fact]
    public void An_unresolved_ancestry_blocks_even_with_no_global_default_to_fall_back_on()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs(null, null, null, LineageState.Unresolved, null, null, null));

        Assert.IsType<ProfileDecision.Block>(decision);
    }

    /// <summary>
    /// Inherited without a name to inherit is a contradiction, and the safe reading of a contradiction is the
    /// one that refuses: falling through would be the silent executor change the level exists to prevent.
    /// </summary>
    [Fact]
    public void An_inherited_record_with_no_profile_to_read_blocks_like_an_unresolved_one()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs(null, null, null, LineageState.Inherited, null, null, "house-default"));

        var block = Assert.IsType<ProfileDecision.Block>(decision);
        Assert.Equal(AttemptErrors.LineageResolutionUnsupported, block.Code);
    }

    /// <summary>A level somebody left blank is a level that says nothing, not a level that names an empty name.</summary>
    [Fact]
    public void A_blank_level_is_skipped_rather_than_taken_as_a_name()
    {
        var decision = ProfileResolution.Decide(
            new ProfileInputs("   ", "", null, LineageState.Root, null, null, "house-default"));

        var use = Assert.IsType<ProfileDecision.Use>(decision);
        Assert.Equal(ProfileResolutionSource.GlobalDefault, use.Source);
        Assert.Equal("house-default", use.Name);
    }
}
