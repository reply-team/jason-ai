namespace Jason.Runtime.Ui;

/// <summary>
/// Set on a request the bearer check let in by a UI token rather than the run token: the hash of that token, so a
/// connection that stays open after the check, the journal's stream, can ask <see cref="UiTokenStore.IsLive"/> whether
/// the token is still accepted. The token itself is never kept.
/// </summary>
public sealed record UiTokenBearer(string Hash);
