namespace Jason.Runtime.Tests;

/// <summary>
/// The suite's clock has a timer that fires when the clock is moved, and never otherwise. A service that
/// sleeps through <c>Task.Delay(…, clock, …)</c> goes through <see cref="TimeProvider.CreateTimer"/>, so this is
/// what lets a test say "five minutes have passed" instead of waiting five minutes for them to.
/// </summary>
public class FixedClockTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_timer_fires_when_the_clock_reaches_its_due_time_and_not_a_minute_before()
    {
        var clock = new FixedClock(Noon);
        var fired = new List<DateTimeOffset>();
        using var timer = clock.CreateTimer(_ => fired.Add(clock.GetUtcNow()), null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromMinutes(9));
        Assert.Empty(fired);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal([Noon.AddMinutes(10)], fired);

        // One-shot: the hour after does not ring it again.
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Single(fired);
    }

    [Fact]
    public void A_periodic_timer_fires_once_per_interval_the_clock_moves_through()
    {
        var clock = new FixedClock(Noon);
        var fired = new List<DateTimeOffset>();
        using var timer = clock.CreateTimer(_ => fired.Add(clock.GetUtcNow()), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal([Noon.AddHours(1), Noon.AddHours(2)], fired);
    }

    [Fact]
    public void A_disposed_timer_never_fires()
    {
        var clock = new FixedClock(Noon);
        var fired = 0;
        var timer = clock.CreateTimer(_ => fired++, null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
        timer.Dispose();

        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Timers_fire_in_the_order_they_fall_due_whatever_order_they_were_made_in()
    {
        var clock = new FixedClock(Noon);
        var fired = new List<string>();
        using var later = clock.CreateTimer(_ => fired.Add("later"), null, TimeSpan.FromMinutes(20), Timeout.InfiniteTimeSpan);
        using var sooner = clock.CreateTimer(_ => fired.Add("sooner"), null, TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(["sooner", "later"], fired);
    }

    /// <summary>
    /// A delay loop registers its next timer from inside the callback of the last one. That timer belongs to
    /// the same move of the clock if the move reaches it — and the callback must be able to register it at all,
    /// which it cannot if the clock is holding a lock while it fires.
    /// </summary>
    [Fact]
    public void A_timer_made_by_a_callback_fires_in_the_same_move_when_the_move_reaches_it()
    {
        var clock = new FixedClock(Noon);
        var fired = new List<DateTimeOffset>();
        ITimer? second = null;
        using var first = clock.CreateTimer(
            _ =>
            {
                fired.Add(clock.GetUtcNow());
                second = clock.CreateTimer(_ => fired.Add(clock.GetUtcNow()), null, TimeSpan.FromMinutes(1), Timeout.InfiniteTimeSpan);
            },
            null,
            TimeSpan.FromMinutes(5),
            Timeout.InfiniteTimeSpan);

        clock.Advance(TimeSpan.FromMinutes(10));

        Assert.Equal([Noon.AddMinutes(5), Noon.AddMinutes(6)], fired);
        Assert.Equal(Noon.AddMinutes(10), clock.GetUtcNow());
        second?.Dispose();
    }

    /// <summary>The way a hosted service actually sleeps, so the checker's wait is the thing being proved here.</summary>
    [Fact]
    public async Task A_delay_on_this_clock_completes_when_the_clock_is_advanced_to_it()
    {
        var clock = new FixedClock(Noon);
        var delay = Task.Delay(TimeSpan.FromMinutes(5), clock, Ct);

        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.False(delay.IsCompleted);

        clock.Advance(TimeSpan.FromMinutes(1));
        await delay.WaitAsync(TimeSpan.FromSeconds(5), Ct);
    }
}
