namespace Jason.Contracts.Api;

/// <summary>Response of <c>system.info</c>: what the runtime says about itself. Non-business by design.</summary>
/// <param name="Update">What the last successful update check learned; null until there has been one.</param>
public sealed record SystemInfoResponse(
    string RuntimeVersion,
    string ApiVersion,
    string InstanceId,
    int Pid,
    DateTimeOffset StartedAt,
    string DataDir,
    DatabaseInfo Database,
    DispatcherInfo Dispatcher,
    PluginsInfo Plugins,
    RoutesInfo Routes,
    UpdateInfo? Update = null);

/// <summary>
/// What the database is, and what this start did to it: every migration that has been applied, the ones this
/// start applied itself, and the file it copied the database to before it did.
/// </summary>
/// <remarks>
/// The last two are for one caller. An applier that has just started a new binary has to know whether that
/// start migrated — because that is what decides whether rolling the binary back also means putting a database
/// back — and which file to put back. It never opens the database to find out; it asks.
/// </remarks>
public sealed record DatabaseInfo(
    IReadOnlyList<string> AppliedMigrations,
    IReadOnlyList<string> NewlyApplied,
    string? BackupFile);

/// <summary>
/// What the last successful update check learned: whether the version it found is newer than the one running,
/// which version that was, when it looked, and where the notes are. Absent until a check has succeeded, so a
/// runtime that has not looked says so rather than saying "up to date".
/// </summary>
public sealed record UpdateInfo(bool Available, string Version, DateTimeOffset CheckedAt, string? ReleaseNotesUrl);

/// <summary>What the dispatcher is doing right now: the cheapest way for an operator or a test to see the loop is alive.</summary>
public sealed record DispatcherInfo(
    DispatcherState State,
    int TickSeconds,
    int MaxParallel,
    int RunningAttempts,
    DateTimeOffset? LastScanAt,
    long Scans,
    long Summons);

/// <summary>
/// Whether the plugin registry is alive and what it holds, in four values: the cheapest way to see the active
/// snapshot without a second call. The registry itself is read through <c>plugin.list</c>.
/// </summary>
public sealed record PluginsInfo(
    int ActiveCount,
    string SnapshotId,
    DateTimeOffset LoadedAt,
    bool? LastReloadActivated);

/// <summary>
/// Where work is being sent, in five values. It sits beside <see cref="PluginsInfo"/> because "what is running"
/// is only half of the question an operator is asking: the other half is which plugin each operation reaches,
/// and the two are frozen by the same act. The routes themselves are read through <c>route.list</c>.
/// </summary>
public sealed record RoutesInfo(
    string SnapshotId,
    DateTimeOffset ActivatedAt,
    string? GlobalDefaultPlugin,
    int GlobalOverrideCount,
    int CampaignRouteCount);
