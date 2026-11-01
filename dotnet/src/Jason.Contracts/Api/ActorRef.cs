namespace Jason.Contracts.Api;

/// <summary>
/// Who performs an operation, as claimed by the caller. Default is human; "system" is reserved for the runtime.
/// The claim is recorded in the journal, never verified: the runtime holds one capability token and no caller identity.
/// </summary>
public sealed record ActorRef(ActorType Type, string? Id = null);
