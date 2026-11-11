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
/// The people of the campaigns <c>campaign_ids</c> names (one to a hundred, archived ones included), or of the one
/// <c>campaign_id</c> names, or of every campaign <c>campaign.list</c> shows when neither is given; both is refused.
/// Each person is listed once. Excluded members are hidden unless <c>state</c> asks for them. <c>summary</c> lists the
/// people one count of <c>campaign.summary</c> counts, and <c>activity</c>, with its <c>since</c> and <c>until</c>, the
/// people one count of <c>campaign.activity</c> counts, removed ones included, the latest to whom it happened first; one
/// of the two at most, and neither takes a <c>state</c>. <c>query</c> finds people as <c>contact.list</c> does.
/// <c>audience</c>, the number of one of the campaign's audiences, lists only the people a search landed under it, with or
/// without a count; an audience is one campaign's, so the request then names that campaign alone.
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
    string? Query = null,
    IReadOnlyList<string>? CampaignIds = null,
    int? Audience = null);

/// <summary>
/// A person listed once, with the whole contact so an agent listing a campaign avoids a get per member. <c>counted</c>
/// names each campaign asked that lists them, with what it counts them for when a filter asked: the latest first under an
/// activity count, otherwise in the campaigns' order. <c>state</c>, <c>added_at</c>, <c>updated_at</c>, <c>why</c> and
/// <c>source</c> are the first of those memberships.
/// </summary>
public sealed record MembershipItemDto(
    ContactDto Contact,
    MembershipState State,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MembershipWhy? Why = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CountedIn>? Counted = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MembershipSource? Source = null);

/// <summary>
/// Where a member came from, when a search brought them: the campaign's audience it ran under (<c>audience</c>, its id, and
/// <c>number</c>), the search's work item (<c>search</c>) and when the person landed (<c>landed_at</c>). Absent for a
/// member added any other way.
/// </summary>
public sealed record MembershipSource(string Audience, int Number, string Search, DateTimeOffset LandedAt);

/// <summary>A campaign that lists the person, and what it counts them for when a filter asked.</summary>
public sealed record CountedIn(
    string CampaignId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MembershipWhy? Why = null);

/// <summary>
/// Why a listed person is counted. <c>approval</c>: the approval that waits on a person (<c>approval_id</c>, and what it
/// would do as <c>text</c>). <c>question</c>: the question that does (<c>decision_id</c>, and the question as
/// <c>text</c>). <c>failure</c>: how their latest work ended (<c>code</c>, the error's message as <c>text</c>, and
/// <c>at</c>). <c>activity</c>: when it happened in the period (<c>at</c>) and what (<c>code</c>: the operation or role of
/// the work, the channel of a reply or an opt-out, <c>added</c> for an addition); under the <c>people</c> count, the latest
/// of what happened, with <c>text</c> naming its count (<c>added</c>, <c>worked_on</c>, <c>replied</c>, <c>opted_out</c> or
/// <c>removed</c>).
/// </summary>
public sealed record MembershipWhy(
    string Kind,
    string? Text,
    string? Code,
    DateTimeOffset? At,
    string? ApprovalId,
    string? DecisionId);
