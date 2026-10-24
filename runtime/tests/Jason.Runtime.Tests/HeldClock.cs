namespace Jason.Runtime.Tests;

/// <summary>
/// A clock whose timestamps move only when the test moves them, so a wait timed by it lasts until the test says it is
/// over and never ends because the machine was slow. Its time of day is the system's: it holds how long things take, not
/// when they happen.
/// </summary>
public sealed class HeldClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _ticks);

    /// <summary>Moves the clock on; never back, since what it holds is how long things take.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        Interlocked.Add(ref _ticks, by.Ticks);
    }
}
