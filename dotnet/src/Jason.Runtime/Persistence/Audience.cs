using System.Text.Json.Nodes;

namespace Jason.Runtime.Persistence;

/// <summary>
/// One definition a campaign's searches run under: made by the claim of the first <c>audience.search</c> under it that the
/// claim lets through, to run or to wait for a person, numbered by the runtime in that order, and recognized by its
/// content, so the same definition again, a reverted one included, is the same audience. Never changed and never removed,
/// a search a person then rejected included.
/// </summary>
public sealed class Audience
{
    public int Id { get; set; }

    /// <summary><c>aud_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public int CampaignId { get; set; }

    public Campaign? Campaign { get; set; }

    /// <summary>1, 2, 3 within the campaign, in the order the claim made them: the runtime's, never a writer's.</summary>
    public int Number { get; set; }

    /// <summary>The definition as the claim that made the audience held it to the routed plugin's schema.</summary>
    public required JsonObject Definition { get; set; }

    /// <summary>The <c>sha256:</c> of the definition's canonical form: how the same definition is found again.</summary>
    public required string DefinitionHash { get; set; }

    /// <summary>When the claim that made it ran.</summary>
    public DateTime CreatedAt { get; set; }
}
