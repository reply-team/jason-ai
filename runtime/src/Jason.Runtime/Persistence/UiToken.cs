namespace Jason.Runtime.Persistence;

/// <summary>A browser paired by <c>POST /ui/session</c>: the SHA-256 of its token, who paired, when, and when it was last seen.</summary>
public sealed class UiToken
{
    public int Id { get; set; }

    public required string PublicId { get; set; }

    /// <summary>Lowercase hex SHA-256 of the token. The token itself is answered once and never stored.</summary>
    public required string TokenHash { get; set; }

    public required string IssuedTo { get; set; }

    public DateTime IssuedAt { get; set; }

    /// <summary>Coarse: written at most once a minute per token.</summary>
    public DateTime? LastUsedAt { get; set; }
}
