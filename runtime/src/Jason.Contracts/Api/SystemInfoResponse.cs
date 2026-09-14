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
    DispatcherInfo Dispatcher);

public sealed record DatabaseInfo(IReadOnlyList<string> AppliedMigrations);

/// <summary>What the dispatcher is doing right now: the cheapest way for an operator or a test to see the loop is alive.</summary>
public sealed record DispatcherInfo(
    DispatcherState State,
    int TickSeconds,
    int MaxParallel,
    int RunningAttempts,
    DateTimeOffset? LastScanAt,
    long Scans);
