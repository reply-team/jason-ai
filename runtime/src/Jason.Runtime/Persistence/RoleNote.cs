using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// What one role remembers at one level — the client's, a project's or a campaign's. It is a knowledge concept of its
/// own: a skill teaches the job, the person's memory files are their own standing guidance, which the runtime never
/// reads, a named preference is a fact a person states about the client, campaign context is the campaign's shared
/// knowledge — a note is one role's own working memory, written by that role for its own later sessions. A note at the
/// client's level is shared across campaigns only because the role's craft is; learned practice, which this version
/// does not have, would be the installation's own knowledge, distilled from what happened across campaigns and
/// written by no role for itself.
/// </summary>
/// <remarks>
/// Nothing in the runtime reads a note or acts on one: it is explicitly not authoritative, and where it
/// disagrees with campaign state the state is what is true. One row per role at each level, replaced whole; the
/// chronicle records that it changed, how big it is and what it hashes to, and never a word of what it says.
/// </remarks>
public sealed class RoleNote
{
    public int Id { get; set; }

    /// <summary>
    /// The campaign the note is about, or null for a note at a project's level or the client's — a role's general craft,
    /// refined per project and per campaign.
    /// </summary>
    public int? CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>
    /// The project the note is about, for a note at a project's level; null for the client's level and for a campaign's
    /// note, whose project is its campaign's and is never stored twice. A note names at most one of the two.
    /// </summary>
    public int? ProjectId { get; set; }

    public Project? Project { get; set; }

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
