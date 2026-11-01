using Jason.Contracts.Api;

namespace Jason.Runtime.Persistence;

/// <summary>
/// One block a provider said it holds: a plugin and the identity of the binding the attempt ran under, the account as a
/// whole or one sending account under it, why in the contract's words, and where a person goes to clear it. Opened by the
/// attempt that met it, seen again by every later answer that names it, and cleared by the first call that gets past it
/// or by a person who says the cause is gone. While it is open, the work it holds waits on it, and one item at a time is let through as its trial.
/// </summary>
/// <remarks>
/// The account itself is never named: the binding identity is the <c>sha256:</c> of the binding's canonical form, or
/// <see cref="EventSource.NoBinding"/> for a route that names none, the key event sources and reads by account use.
/// <see cref="Link"/> is the provider's text: it is redacted wherever an error's details are, never written to a log and
/// never carried by a pointer.
/// </remarks>
public sealed class ProviderBlock
{
    public int Id { get; set; }

    /// <summary><c>blk_</c> + ULID.</summary>
    public required string PublicId { get; set; }

    public required string PluginId { get; set; }

    public required string BindingIdentity { get; set; }

    public ProviderBlockScope Scope { get; set; }

    /// <summary>The provider's identifier of the sending account; present exactly when the scope is <c>sender</c>.</summary>
    public string? SenderExternalId { get; set; }

    /// <summary>Why, as the contract's word, from the latest answer that named the block.</summary>
    public required string Reason { get; set; }

    /// <summary>Where a person goes to clear it, from the latest answer that named the block; null where it gave none.</summary>
    public string? Link { get; set; }

    /// <summary>When the provider said the block clears by itself, from the latest answer; null where it did not say.</summary>
    public DateTime? ClearsAt { get; set; }

    public ProviderBlockStatus Status { get; set; } = ProviderBlockStatus.Open;

    public DateTime OpenedAt { get; set; }

    /// <summary>The attempt whose answer opened the block.</summary>
    public int OpenedByAttemptId { get; set; }

    public Attempt? OpenedByAttempt { get; set; }

    /// <summary>When an answer last named the block, the one that opened it included.</summary>
    public DateTime LastSeenAt { get; set; }

    /// <summary>How many answers have named the block, the one that opened it included.</summary>
    public int SeenCount { get; set; } = 1;

    /// <summary>When the next trial is due: the trial cadence from the latest answer, or the moment it said the block clears.</summary>
    public DateTime NextTrialAt { get; set; }

    /// <summary>The item let through as the block's trial while it is under way; null when no trial is.</summary>
    public int? TrialWorkItemId { get; set; }

    public WorkItem? TrialWorkItem { get; set; }

    public DateTime? ClearedAt { get; set; }

    /// <summary>The attempt that got past the block; null while it is open, and when a person cleared it.</summary>
    public int? ClearedByAttemptId { get; set; }

    public Attempt? ClearedByAttempt { get; set; }

    /// <summary>Who cleared the block on their word, with <see cref="ClearedById"/>; null while it is open, and when an attempt got past it.</summary>
    public ActorType? ClearedByType { get; set; }

    public string? ClearedById { get; set; }
}
