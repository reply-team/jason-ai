namespace Jason.Contracts.Api;

/// <summary>Response of <c>system.info</c>: what the runtime says about itself. Non-business by design.</summary>
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
    RoutesInfo Routes);

public sealed record DatabaseInfo(IReadOnlyList<string> AppliedMigrations);

/// <summary>What the dispatcher is doing right now: the cheapest way for an operator or a test to see the loop is alive.</summary>
public sealed record DispatcherInfo(
    DispatcherState State,
    int TickSeconds,
    int MaxParallel,
    int RunningAttempts,
    DateTimeOffset? LastScanAt,
    long Scans);

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
