namespace Jason.Contracts.Api;

/// <summary>
/// One block a provider said it holds, as a person and the UI read it: what is blocked — the account as a whole or one
/// sending account under it — why, where a person goes to clear it, how long and how often it has held, the work it
/// holds now, its next trial, and, once cleared, when and by whom: the attempt that got past it, or the person who said
/// the cause is gone. The link travels here and in a held item's error, and nowhere else.
/// </summary>
public sealed record ProviderBlockDto(
    string Id,
    string PluginId,
    string BindingIdentity,
    ProviderBlockScope Scope,

    /// <summary>The provider's identifier of the sending account; null for a block of the account.</summary>
    string? SenderExternalId,
    string Reason,
    string? Link,
    DateTimeOffset? ClearsAt,
    ProviderBlockStatus Status,
    DateTimeOffset OpenedAt,
    DateTimeOffset LastSeenAt,
    int SeenCount,
    DateTimeOffset NextTrialAt,
    string? TrialWorkItemId,
    string OpenedByAttemptId,

    /// <summary>The open waits parked on this block itself; an account's count leaves out what its senders' blocks hold.</summary>
    int HeldCount,
    DateTimeOffset? ClearedAt,
    string? ClearedByAttemptId,
    ActorRef? ClearedByActor);

/// <summary>
/// Open blocks by default: the question a person opens this with is "what is blocked now?". Newest first, by the moment
/// a block opened and then by its id.
/// </summary>
public sealed record BlockListRequest(IReadOnlyList<ProviderBlockStatus>? Statuses, string? PluginId, int? Limit, string? Cursor);

public sealed record BlockGetRequest(string? BlockId);

/// <summary>
/// A person's clear. The actor is a person with an id: a block is cleared on a person's word, and the line says whose.
/// What it releases, the next scan releases.
/// </summary>
public sealed record BlockClearRequest(string? BlockId, ActorRef? Actor, string? Reason);
