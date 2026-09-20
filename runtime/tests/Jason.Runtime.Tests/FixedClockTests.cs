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

    /// <summary>
    /// A periodic timer fires <b>once</b> for a move, however many of its periods that move passes through, and
    /// re-arms from where the move ended. That is what a real one does: a process that was not looking for an
    /// hour does not get an hour's worth of callbacks when it looks again.
    /// </summary>
    /// <remarks>
    /// The rule here used to be the other one, asserted by this test in that shape. It is not a model anything
    /// in <c>runtime/src</c> reaches — every wait there is a <c>Task.Delay</c>, which is a one-shot — but it is
    /// reachable by anything written next, and a move of an hour against a one-second period would have run
    /// three thousand six hundred callbacks inside <c>Advance</c> before it returned.
    /// </remarks>
    [Fact]
    public void A_periodic_timer_fires_once_for_a_move_however_many_periods_it_passes()
    {
        var clock = new FixedClock(Noon);
        var fired = new List<DateTimeOffset>();
        using var timer = clock.CreateTimer(_ => fired.Add(clock.GetUtcNow()), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal([Noon.AddHours(1)], fired);

        // Still armed, a period after the moment that move ended rather than a period after the tick it missed.
        Assert.Equal(1, clock.Armed);

        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal([Noon.AddHours(1), Noon.AddHours(3)], fired);
    }

    /// <summary>And the ordinary case is unchanged: one move, one period, one callback, every time.</summary>
    [Fact]
    public void A_periodic_timer_fires_on_each_move_that_reaches_its_next_period()
    {
        var clock = new FixedClock(Noon);
        var fired = new List<DateTimeOffset>();
        using var timer = clock.CreateTimer(_ => fired.Add(clock.GetUtcNow()), null, TimeSpan.FromHours(1), TimeSpan.FromHours(1));

        clock.Advance(TimeSpan.FromHours(1));
        clock.Advance(TimeSpan.FromHours(1));
        clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal([Noon.AddHours(1), Noon.AddHours(2), Noon.AddHours(3)], fired);
    }

    /// <summary>
    /// The other half, which must keep working: a delay loop registers its next one-shot from inside the
    /// callback of the last, and every tick of a move really does run. That is faithful too — a loop that
    /// sleeps a second at a time between pieces of work does run each iteration when it wakes, which is why the
    /// two are modelled differently.
    /// </summary>
    [Fact]
    public void A_delay_loop_replays_every_tick_it_slept_through()
    {
        var clock = new FixedClock(Noon);
        var woke = 0;

        void Sleep() =>
            clock.CreateTimer(
                _ =>
                {
                    woke++;
                    if (woke < 10)
                    {
                        Sleep();
                    }
                },
                null,
                TimeSpan.FromSeconds(1),
                Timeout.InfiniteTimeSpan);

        Sleep();
        clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Equal(10, woke);
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
