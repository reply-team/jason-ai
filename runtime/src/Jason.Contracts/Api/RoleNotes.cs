using System.Text.Json.Nodes;

namespace Jason.Contracts.Api;

/// <summary>
/// One role's memory at one level: a document that role wrote for its own future sessions. It is the role's working
/// knowledge and not the runtime's: where a note disagrees with campaign state, the state is what is true and the note
/// is out of date.
/// </summary>
/// <param name="CampaignId">The campaign the note is about, for a campaign's note; null at the other two levels.</param>
/// <param name="ProjectId">The project the note is about, for a project's note; null at the other two levels — a
/// campaign's note is in the campaign's project already, and that is never said twice. Neither id is the client's
/// level: the role's general craft.</param>
/// <param name="Note">The document as it was last written, or an empty object where nothing has been.</param>
/// <param name="NoteHash">Its canonical <c>sha256:</c>, so a reader can tell a note that changed from one that
/// did not without carrying it around. Null until something has been written.</param>
/// <param name="NoteBytes">The UTF-8 size of its canonical form, which is what the cap is measured against.</param>
/// <param name="UpdatedBy">Who wrote it last, as they claimed to be. Null until something has been written.</param>
public sealed record RoleNoteDto(
    string? CampaignId,
    string? ProjectId,
    string Role,
    JsonObject Note,
    string? NoteHash,
    long NoteBytes,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    ActorRef? UpdatedBy);

/// <summary>One level's memory listed without any of it being read: which roles have written, how much, when.</summary>
public sealed record RoleNoteSummaryDto(
    string? CampaignId,
    string? ProjectId,
    string Role,
    string NoteHash,
    long NoteBytes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ActorRef UpdatedBy);

/// <summary>
/// A role's note at one level: <paramref name="CampaignId"/> for a campaign's, <paramref name="ProjectId"/> for a
/// project's, neither for the client's. Naming both is refused: a note is at one level.
/// </summary>
public sealed record RoleNoteGetRequest(string? CampaignId, string? Role, string? ProjectId = null);

/// <summary>
/// Replaces a role's note whole, at the one level the request names. There is no patch: a role merging into its own
/// memory would have to reason about what an earlier session of itself meant by a key, and the rule that needs no
/// reasoning is that the last writer owns the document.
/// </summary>
/// <param name="Note">The whole document. Bound as a node rather than an object so a caller who sends an array
/// is told which field is wrong instead of having the request refused as unreadable.</param>
public sealed record RoleNoteSetRequest(string? CampaignId, string? Role, JsonNode? Note, ActorRef? Actor, string? Reason, string? ProjectId = null);

/// <summary>The roles that have written at one level: a campaign's, a project's, or — naming neither — the client's.</summary>
public sealed record RoleNoteListRequest(string? CampaignId, int? Limit, string? Cursor, string? ProjectId = null);
