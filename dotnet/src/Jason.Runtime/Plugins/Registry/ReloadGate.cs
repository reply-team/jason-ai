namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// One reload at a time. Scanning, hashing and resolving are not cheap and must not interleave: two reloads at
/// once could end with the older of two snapshots active.
/// </summary>
public sealed class ReloadGate
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
