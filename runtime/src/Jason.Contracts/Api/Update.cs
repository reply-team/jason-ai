namespace Jason.Contracts.Api;

/// <summary>
/// What <c>jason update check</c> prints: the version running, the newest one the feed names, whether that is
/// an update, when the feed was asked, and where the notes are. Not an answer from the runtime — the CLI asks
/// the feed itself, so that the question can be asked on a machine whose runtime will not start — but shaped
/// like one, so an agent parses it the way it parses everything else.
/// </summary>
public sealed record UpdateCheckResponse(string Current, string Latest, bool Available, DateTimeOffset CheckedAt, string? ReleaseNotesUrl);

/// <summary>
/// What an update did: the version it came from, the version it installed, the step it reached, and the steps
/// themselves in the order they happened — the same lines <c>--human</c> prints, so a script and a person are
/// reading the same account.
/// </summary>
public sealed record UpdateApplyResponse(string From, string To, string Step, IReadOnlyList<string> Steps);
