using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// A contact's membership in one campaign. Removal is the soft state <c>excluded</c>, kept on purpose: a bulk
/// re-import must never silently re-add somebody the user took out.
/// </summary>
public sealed class CampaignContact
{
    public int Id { get; set; }

    public int CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    public int ContactId { get; set; }

    public Contact? Contact { get; set; }

    public MembershipState State { get; set; } = MembershipState.Enrolled;

    public DateTime AddedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
