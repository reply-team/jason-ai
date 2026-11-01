namespace Jason.Contracts.Api;

/// <summary>
/// <c>ui.open</c>: a one-time link into the UI. The ticket in it is worth one pairing within a minute; the optional
/// ids deep-link into Attention.
/// </summary>
public sealed record UiOpenRequest(string? ApprovalId = null, string? DecisionId = null);

public sealed record UiOpenResponse(string Url, DateTimeOffset ExpiresAt);

/// <summary>One paired browser, as <c>ui.tokens</c> lists it. The token itself is never answered again.</summary>
public sealed record UiTokenDto(string Id, string IssuedTo, DateTimeOffset IssuedAt, DateTimeOffset? LastUsedAt);

public sealed record UiTokensRequest();

public sealed record UiTokensResponse(IReadOnlyList<UiTokenDto> Items);

/// <summary><c>ui.revoke</c>: one token by id, or every one with <c>all</c>.</summary>
public sealed record UiRevokeRequest(string? TokenId = null, bool? All = null);

public sealed record UiRevokeResponse(int Revoked);

/// <summary>The pairing door's body: the ticket from the link's fragment.</summary>
public sealed record UiSessionRequest(string? Ticket);

/// <summary>What the door answers once: the UI token, its id, who the page acts as, and what it is talking to.</summary>
public sealed record UiSessionResponse(string Token, string TokenId, string ActorId, string ApiVersion, string InstanceId);

/// <summary>
/// The UI module as the runtime serves it: which directory (<c>custom</c> or <c>default</c>), the manifest's version and
/// API version, whether the latter is this runtime's, the content digest, and a problem when the directory is not a UI.
/// Null in <c>system.info</c> means no module is installed.
/// </summary>
public sealed record UiModuleInfo(
    string Source,
    string Directory,
    string? Version,
    string? ApiVersion,
    bool ApiVersionMatches,
    string? Digest,
    string? Problem);
