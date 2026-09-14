namespace Jason.Contracts.Api;

/// <summary>draft → active → paused → archived. Archived is terminal.</summary>
public enum CampaignStatus
{
    Draft,
    Active,
    Paused,
    Archived,
}

/// <summary>Where a contact stands inside one campaign. Removal is the soft state <c>excluded</c>, never a deletion.</summary>
public enum MembershipState
{
    Enrolled,
    Paused,
    Finished,
    Excluded,
}

/// <summary>Who performs an operation. <c>System</c> belongs to the runtime's own writes and is refused from callers.</summary>
public enum ActorType
{
    Human,
    Role,
    System,
}

/// <summary>Outcome of one item of <c>campaign.add_contacts</c>.</summary>
public enum AddContactsItemStatus
{
    Added,
    AlreadyMember,
    Rejected,
}

/// <summary>Outcome of one item of <c>campaign.remove_contacts</c>.</summary>
public enum RemoveContactsItemStatus
{
    Removed,
    NotMember,
    AlreadyExcluded,
    Rejected,
}
