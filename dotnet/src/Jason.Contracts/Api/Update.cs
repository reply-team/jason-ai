namespace Jason.Contracts.Api;

/// <summary>
/// What <c>jason update check</c> prints: the version running, the newest one the feed names, whether that is
/// an update, when the feed was asked, and where the notes are. Not an answer from the runtime — the CLI asks
/// the feed itself, so that the question can be asked on a machine whose runtime will not start — but shaped
/// like one, so an agent parses it the way it parses everything else.
/// </summary>
/// <param name="UpgradeCommand">
/// The one command that updates this installation: <c>jason update apply</c> on Jason's own, and the package manager's
/// upgrade where one installed it. One command and never the sequence around it: the runtime is stopped before a
/// manager's upgrade and started after, and the operating skill says so. Below the newest release's floor it is the
/// apply of <paramref name="InstallFirst"/>, the first of the two that reach that release.
/// </param>
/// <param name="InstallFirst">
/// The version to install first: the newest release's <c>min_upgrade_from</c>, when that release is newer than this
/// installation, the installation is Jason's own and its version is below the floor. Null otherwise — and always null
/// under a package manager, which replaces the file whole with no applier of this build's in between.
/// </param>
/// <param name="PluginUpdates">
/// The plugin packages installed here that the newest release publishes a later version of, by id. Null when the newest
/// release is not newer than this installation or names no package; empty when it is newer and nothing installed here
/// that the record holds is behind. A package the release's record does not hold is a package the list does not know:
/// built or edited on that machine, or a version before the list's first line, 0.3.2. The check never names it — whether
/// it is behind is <c>jason status</c>'s to say, by its version — and never names one that cannot be read. Read from the
/// plugins directory and the feed alone, under a package manager as well.
/// </param>
public sealed record UpdateCheckResponse(
    string Current,
    string Latest,
    bool Available,
    DateTimeOffset CheckedAt,
    string? ReleaseNotesUrl,
    string UpgradeCommand,
    string? InstallFirst,
    IReadOnlyList<PluginUpdate>? PluginUpdates);

/// <summary>
/// A plugin package installed here, by <paramref name="Id"/>, that is behind the newest release's: the version installed
/// and the version the release publishes.
/// </summary>
public sealed record PluginUpdate(string Id, string Installed, string Latest);

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
