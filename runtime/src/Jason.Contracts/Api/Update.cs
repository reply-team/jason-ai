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

/// <summary>
/// Where this installation's update stands, read from the ledger and from nothing else. <c>in_flight</c> is the
/// one field a script needs: true while an update is part-way through, false both when none has ever run and
/// when the last one finished. <c>step</c> tells those two apart — absent when there is no record at all,
/// <c>complete</c> when there is one and it is over.
/// </summary>
/// <remarks>
/// The rest is the record a finished update leaves behind: which versions, what its first start applied to the
/// database and which backup it wrote. That record is what <c>jason update rollback</c> reads an hour later, so
/// a person can see the same thing the rollback would act on before they ask for it.
/// </remarks>
public sealed record UpdateStatusResponse(
    bool InFlight,
    string? Step,
    string? From,
    string? To,
    DateTimeOffset? StartedAt,
    string? BackupFile,
    IReadOnlyList<string> NewlyApplied);
