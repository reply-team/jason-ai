namespace Jason.Runtime.Configuration;

/// <summary>What a work item of one kind gets when it names no override of its own.</summary>
public class KindDefaults
{
    public int TimeoutSeconds { get; set; }

    /// <summary>0 disables the heartbeat for the kind: the lease alone decides whether the attempt is still alive.</summary>
    public int HeartbeatSeconds { get; set; }

    public int MaxAttempts { get; set; }
}

/// <summary>An AI role's defaults, and the bounds on how it waits: a role's wait always ends, and so does its waiting.</summary>
public sealed class AiRoleDefaults : KindDefaults
{
    /// <summary>The longest a ceiling may be: thirty days.</summary>
    public const int MaximumWaitSeconds = 2_592_000;

    /// <summary>The shortest a ceiling may be: a minute, the shortest anything the dispatcher waits for is counted in.</summary>
    public const int MinimumWaitSeconds = 60;

    /// <summary>The most times one item may be allowed to wait.</summary>
    public const int MaximumWaits = 100;

    /// <summary>
    /// 60..2592000; the deadline of a wait that names no moment, counted from when it opens, and the latest moment a wait
    /// may name. Fourteen days unless set.
    /// </summary>
    public int MaxWaitSeconds { get; set; } = 1_209_600;

    /// <summary>1..100; how many times one item may wait before its run has to finish or fail instead.</summary>
    public int MaxWaits { get; set; } = 20;
}

/// <summary>How work a provider block holds is tried again: one item at a time, at a cadence, until a call gets past it.</summary>
public sealed class BlockDefaults
{
    /// <summary>The shortest cadence: a minute, the shortest anything the dispatcher waits for is counted in.</summary>
    public const int MinimumTrialSeconds = 60;

    /// <summary>The longest cadence: a day, the same as the furthest a provider's own word that it clears puts a trial off.</summary>
    public const int MaximumTrialSeconds = 86_400;

    /// <summary>
    /// 60..86400; how long after a block was met, or a trial of it said nothing, the next trial is due, where the provider
    /// did not say when the block clears by itself. Fifteen minutes unless set.
    /// </summary>
    public int TrialSeconds { get; set; } = 900;
}

/// <summary>
/// The dispatcher's own dials. Everything here is read through <c>IOptionsMonitor</c> at each tick, so a hand
/// edit of the user's settings file applies without a restart — except <see cref="MaxParallel"/>, which sizes
/// the handler pool once when the runtime starts.
/// </summary>
public sealed class DispatcherOptions
{
    public const string Section = "Dispatcher";

    /// <summary>The shortest events cadence: a minute, as the inbound read's.</summary>
    public const int MinimumEventsCadenceSeconds = 60;

    /// <summary>The longest events cadence: a day.</summary>
    public const int MaximumEventsCadenceSeconds = 86_400;

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

    /// <summary>
    /// 60..86400; how long after one read of an account's events starts the next is due. Read at every scan, so an edit
    /// applies at the next one.
    /// </summary>
    public int EventsCadenceSeconds { get; set; } = 600;

    /// <summary>The shortest search cadence: a minute, as the other cadences.</summary>
    public const int MinimumSearchCadenceSeconds = 60;

    /// <summary>The longest search cadence: a day.</summary>
    public const int MaximumSearchCadenceSeconds = 86_400;

    /// <summary>
    /// 60..86400; how long after the dispatcher's last read of a search's people the next is due, and after the search
    /// began, its first. Five minutes unless set: a search lands its people a few at a time every two minutes or so.
    /// </summary>
    public int SearchCadenceSeconds { get; set; } = 300;

    /// <summary>The shortest stall bound: a minute.</summary>
    public const int MinimumSearchStallSeconds = 60;

    /// <summary>The longest stall bound: a week.</summary>
    public const int MaximumSearchStallSeconds = 604_800;

    /// <summary>
    /// 60..604800; how long the reads of a search may land nobody before the dispatcher calls it stalled and stops reading
    /// it, whatever the provider says of it meanwhile. An hour unless set.
    /// </summary>
    public int SearchStallSeconds { get; set; } = 3_600;

    /// <summary>
    /// 60..86400; how long after the dispatcher's last read of a search the provider holds — paused, out of credits, at
    /// the plan's limit — the next is due: a held search spends nothing until a person acts at the provider, so it is read
    /// rarely, and read at the ordinary cadence again once it moves. An hour unless set.
    /// </summary>
    public int SearchHeldCadenceSeconds { get; set; } = 3_600;

    /// <summary>
    /// 60..604800; how long a search may stay held at the provider before the dispatcher calls it stalled, its state named,
    /// and stops reading it, so no search is read for ever. A week unless set.
    /// </summary>
    public int SearchHeldStallSeconds { get; set; } = 604_800;

    public AiRoleDefaults AiRole { get; set; } = new() { TimeoutSeconds = 3600, HeartbeatSeconds = 120, MaxAttempts = 3 };

    /// <summary>
    /// The lease covers the slowest operation this build publishes plus the grace a child is given to stop, with
    /// room for the next one — <see cref="ProviderOpBudgetValidator"/> refuses a runtime whose lease is shorter.
    /// One consequence an operator should know: a stuck provider item holds a handler slot for up to this long,
    /// and <see cref="MaxParallel"/> bounds both kinds of work.
    /// </summary>
    public KindDefaults ProviderOp { get; set; } = new() { TimeoutSeconds = 600, HeartbeatSeconds = 0, MaxAttempts = 3 };

    public BlockDefaults Blocks { get; set; } = new();
}
