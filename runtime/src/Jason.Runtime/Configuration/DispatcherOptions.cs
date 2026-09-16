namespace Jason.Runtime.Configuration;

/// <summary>What a work item of one kind gets when it names no override of its own.</summary>
public sealed class KindDefaults
{
    public int TimeoutSeconds { get; set; }

    /// <summary>0 disables the heartbeat for the kind: the lease alone decides whether the attempt is still alive.</summary>
    public int HeartbeatSeconds { get; set; }

    public int MaxAttempts { get; set; }
}

/// <summary>
/// The dispatcher's own dials. Everything here is read through <c>IOptionsMonitor</c> at each tick, so a hand
/// edit of the user's settings file applies without a restart — except <see cref="MaxParallel"/>, which sizes
/// the handler pool once when the runtime starts.
/// </summary>
public sealed class DispatcherOptions
{
    public const string Section = "Dispatcher";

    public bool Enabled { get; set; } = true;

    /// <summary>1..3600.</summary>
    public int TickSeconds { get; set; } = 10;

    /// <summary>1..64, applied when the runtime starts.</summary>
    public int MaxParallel { get; set; } = 4;

    /// <summary>0..20 — it has to fit inside the 30 s a <c>runtime stop</c> waits.</summary>
    public int DrainSeconds { get; set; } = 10;

    /// <summary>0..86400; 0 = retry as soon as the next scan comes round.</summary>
    public int RetryDelaySeconds { get; set; } = 60;

    /// <summary>0..600; how long a process may linger after its attempt was completed through the API before the dispatcher stops it.</summary>
    public int ExitGraceSeconds { get; set; } = 30;

    public KindDefaults AiRole { get; set; } = new() { TimeoutSeconds = 3600, HeartbeatSeconds = 120, MaxAttempts = 3 };

    /// <summary>
    /// The lease covers the slowest operation this build publishes plus the grace a child is given to stop, with
    /// room for the next one — <see cref="ProviderOpBudgetValidator"/> refuses a runtime whose lease is shorter.
    /// One consequence an operator should know: a stuck provider item holds a handler slot for up to this long,
    /// and <see cref="MaxParallel"/> bounds both kinds of work.
    /// </summary>
    public KindDefaults ProviderOp { get; set; } = new() { TimeoutSeconds = 600, HeartbeatSeconds = 0, MaxAttempts = 3 };
}
