namespace Jason.Cli.Tests.Files;

/// <summary>A clock that moves only when a pause is taken, so a held-file wait ends after exactly the rule's pauses.</summary>
internal sealed class PausedClock : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Pass(TimeSpan delay) => _ticks += delay.Ticks;
}
