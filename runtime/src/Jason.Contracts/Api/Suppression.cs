namespace Jason.Contracts.Api;

/// <summary>A do-not-contact entry. Suppression is global: opting out of email is not opting out of LinkedIn.</summary>
public sealed record SuppressionDto(string Id, string Channel, string Value, string? Reason, DateTimeOffset CreatedAt);

public sealed record SuppressionAddRequest(string? Channel, string? Value, ActorRef? Actor, string? Reason);

/// <summary>Lifting a suppression is a deliberate act: <c>reason</c> is required and lands in the journal.</summary>
public sealed record SuppressionRemoveRequest(string? Channel, string? Value, ActorRef? Actor, string? Reason);

public sealed record SuppressionRemovedDto(string Channel, string Value, bool Removed);

public sealed record SuppressionListRequest(string? Channel, string? Value, int? Limit, string? Cursor);
