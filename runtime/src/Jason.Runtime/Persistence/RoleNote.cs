using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// What one role remembers about one campaign. It is the fourth knowledge concept and none of the other three:
/// a skill teaches the job, campaign context is the campaign's shared knowledge, learned practice would be
/// cross-campaign — a note is one role's own working memory of one campaign, written by that role for its own
/// later sessions.
/// </summary>
/// <remarks>
/// Nothing in the runtime reads a note or acts on one: it is explicitly not authoritative, and where it
/// disagrees with campaign state the state is what is true. One row per campaign and role, replaced whole; the
/// chronicle records that it changed, how big it is and what it hashes to, and never a word of what it says.
/// </remarks>
public sealed class RoleNote
{
    public int Id { get; set; }

    public int CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>The role's name, as the roster spells it. A note exists only for a role the runtime knows.</summary>
    public required string Role { get; set; }

    /// <summary>The document the role last wrote, exactly as it wrote it.</summary>
    public required JsonObject Note { get; set; }

    /// <summary>The canonical <c>sha256:</c> of <see cref="Note"/>, which is what the chronicle names it by.</summary>
    public required string NoteHash { get; set; }

    /// <summary>The size of its canonical form: what the cap is measured against, and what a listing shows.</summary>
    public long NoteBytes { get; set; }

    /// <summary>Who last wrote it, as they claimed to be — a role in a session, a running attempt, or a person.</summary>
    public ActorType UpdatedByType { get; set; }

    public string? UpdatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
