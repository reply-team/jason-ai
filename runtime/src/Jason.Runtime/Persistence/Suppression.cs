namespace Jason.Runtime.Persistence;

/// <summary>
/// A do-not-contact entry, global across campaigns and scoped to one channel: opting out of email is not
/// opting out of LinkedIn.
/// </summary>
public sealed class Suppression
{
    public int Id { get; set; }

    /// <summary><c>sup_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public required string Channel { get; set; }

    /// <summary>Normalized by the channel's rule, so a suppression matches however the address was typed.</summary>
    public required string Value { get; set; }

    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }
}
