using Jason.Contracts;
using Jason.Contracts.Ids;

namespace Jason.Runtime.Discovery;

/// <summary>Identity of one runtime process. A fresh instance id per start lets clients detect stale discovery data.</summary>
public sealed record RuntimeInfo(string InstanceId, int Pid, DateTimeOffset StartedAt, string RuntimeVersion)
{
    public static RuntimeInfo Create() => new(PublicId.New("rt"), Environment.ProcessId, StartTimestamp(), JasonVersion.Current);

    /// <summary>
    /// The JSON dialect carries milliseconds, so the in-memory timestamp is truncated to the same resolution.
    /// A client that reads <c>started_at</c> back from the descriptor then sees exactly the value the runtime
    /// reports through <c>system.info</c>, instead of one that differs by sub-millisecond ticks.
    /// </summary>
    private static DateTimeOffset StartTimestamp() =>
        DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
}
