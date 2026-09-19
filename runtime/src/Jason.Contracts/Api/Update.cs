namespace Jason.Contracts.Api;

/// <summary>
/// What <c>jason update check</c> prints: the version running, the newest one the feed names, whether that is
/// an update, when the feed was asked, and where the notes are. Not an answer from the runtime — the CLI asks
/// the feed itself, so that the question can be asked on a machine whose runtime will not start — but shaped
/// like one, so an agent parses it the way it parses everything else.
/// </summary>
public sealed record UpdateCheckResponse(string Current, string Latest, bool Available, DateTimeOffset CheckedAt, string? ReleaseNotesUrl);
