using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>One item of an import: either an existing contact by id, or a payload the runtime matches or creates.</summary>
public sealed record AddContactsItem(
    string? ContactId,
    string? FirstName,
    string? LastName,
    string? Company,
    string? Title,
    string? TimeZone,
    IReadOnlyList<ChannelInput>? Channels,
    JsonObject? Custom)
    : ContactInput(FirstName, LastName, Company, Title, TimeZone, Channels, Custom);

/// <summary><c>match_by</c> is a channel name (<c>email</c>) or <c>custom:&lt;field&gt;</c>; absent means every payload item creates a contact.</summary>
public sealed record AddContactsRequest(
    string? CampaignId,
    string? MatchBy,
    IReadOnlyList<AddContactsItem>? Contacts,
    ActorRef? Actor,
    string? Reason);

/// <summary>The outcome of one item, in input order. A rejected item never fails the batch.</summary>
public sealed record AddContactsItemResult(
    int Index,
    AddContactsItemStatus Status,
    string? ContactId,
    bool ContactCreated,
    MembershipState? State,
    ErrorBody? Error);

public sealed record AddContactsSummary(int Added, int AlreadyMember, int Rejected);

public sealed record AddContactsResult(string CampaignId, AddContactsSummary Summary, IReadOnlyList<AddContactsItemResult> Items);

public sealed record RemoveContactsRequest(string? CampaignId, IReadOnlyList<string>? ContactIds, ActorRef? Actor, string? Reason);

public sealed record RemoveContactsItemResult(int Index, RemoveContactsItemStatus Status, string? ContactId, ErrorBody? Error);

public sealed record RemoveContactsSummary(int Removed, int NotMember, int AlreadyExcluded, int Rejected);

public sealed record RemoveContactsResult(string CampaignId, RemoveContactsSummary Summary, IReadOnlyList<RemoveContactsItemResult> Items);

/// <summary>Excluded members are hidden unless <c>state</c> asks for them.</summary>
public sealed record ListContactsRequest(string? CampaignId, MembershipState? State, int? Limit, string? Cursor);

/// <summary>A membership carries the whole contact so an agent listing a campaign avoids a get per member.</summary>
public sealed record MembershipItemDto(ContactDto Contact, MembershipState State, DateTimeOffset AddedAt, DateTimeOffset UpdatedAt);
