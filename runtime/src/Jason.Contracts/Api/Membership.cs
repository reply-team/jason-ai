using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

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

/// <summary>
/// Excluded members are hidden unless <c>state</c> asks for them. <c>summary</c> lists the people one count of
/// <c>campaign.summary</c> counts, and <c>activity</c>, with its <c>since</c> and <c>until</c>, the people one count of
/// <c>campaign.activity</c> counts, removed ones included; one of the two at most, and neither takes a <c>state</c>.
/// <c>query</c> finds people as <c>contact.list</c> does.
/// </summary>
public sealed record ListContactsRequest(
    string? CampaignId,
    MembershipState? State,
    int? Limit,
    string? Cursor,
    CampaignSummaryFilter? Summary = null,
    CampaignActivityFilter? Activity = null,
    DateTimeOffset? Since = null,
    DateTimeOffset? Until = null,
    string? Query = null);

/// <summary>
/// A membership carries the whole contact so an agent listing a campaign avoids a get per member. <c>why</c> is there
/// when a filter asked: what the person is counted for.
/// </summary>
public sealed record MembershipItemDto(
    ContactDto Contact,
    MembershipState State,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MembershipWhy? Why = null);

/// <summary>
/// Why a listed person is counted. <c>approval</c>: the approval that waits on a person (<c>approval_id</c>, and what it
/// would do as <c>text</c>). <c>question</c>: the question that does (<c>decision_id</c>, and the question as
/// <c>text</c>). <c>failure</c>: how their latest work ended (<c>code</c>, the error's message as <c>text</c>, and
/// <c>at</c>). <c>activity</c>: when it happened in the period (<c>at</c>) and what (<c>code</c>: the operation or role of
/// the work, the channel of a reply or an opt-out, <c>added</c> for an addition).
/// </summary>
public sealed record MembershipWhy(
    string Kind,
    string? Text,
    string? Code,
    DateTimeOffset? At,
    string? ApprovalId,
    string? DecisionId);
