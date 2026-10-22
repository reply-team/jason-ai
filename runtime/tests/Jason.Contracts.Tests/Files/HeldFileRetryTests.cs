using System.Globalization;
using Jason.Contracts.Files;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.Files;

/// <summary>
/// A call on a file held open by another program, tried again on Windows until the rule's wait is over, and every other
/// failure thrown at once. Nothing here waits in real time: the clock moves only when a pause is taken, by the pause's
/// own length, or when a call stands for a slow act, so the tries a wait allows and the pauses between them can be counted
/// exactly.
/// </summary>
public class HeldFileRetryTests
{
    private static readonly HeldFileRule Rule = new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250), AccessDeniedCounts: false);

    private const int SharingViolation = unchecked((int)0x80070020);
    private const int LockViolation = unchecked((int)0x80070021);

    [Theory]
    [InlineData(SharingViolation)]
    [InlineData(LockViolation)]
    public void A_call_held_a_few_times_goes_through_after_a_pause_of_the_interval_for_each(int hold)
    {
        var clock = new SteppedClock();
        var call = new Call(times: 3, () => new IOException("held", hold));

        var held = Run(call, Rule, clock, windows: true);

        Assert.Null(held);
        Assert.Equal(4, call.Tries);
        Assert.Equal([Rule.Interval, Rule.Interval, Rule.Interval], clock.Pauses);
    }

    [Fact]
    [Critical(Family.F5, "the wait ends on time with the cause kept")]
    public void A_call_held_throughout_is_given_up_at_exactly_the_wait_with_the_last_refusal_kept()
    {
        var clock = new SteppedClock();
        var call = new Call(times: int.MaxValue, () => new IOException("held", SharingViolation));

        var held = Run(call, Rule, clock, windows: true);

        // A try at the start and one after each of forty pauses of a quarter second: the last is the try at ten seconds.
        Assert.Same(call.Last, held);
        Assert.Equal(41, call.Tries);
        Assert.Equal(40, clock.Pauses.Count);
        Assert.Equal(Rule.Wait, clock.Now);
    }

    [Fact]
    public void The_last_pause_is_cut_to_what_is_left_of_the_wait()
    {
        var clock = new SteppedClock();
        var rule = new HeldFileRule(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(300), AccessDeniedCounts: false);
        var call = new Call(times: int.MaxValue, () => new IOException("held", SharingViolation));

        Assert.NotNull(Run(call, rule, clock, windows: true));

        Assert.Equal([TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(100)], clock.Pauses);
        Assert.Equal(5, call.Tries);
        Assert.Equal(rule.Wait, clock.Now);
    }

    [Fact]
    [Critical(Family.F5, "the held-file wait begins at the first refusal")]
    public void A_first_try_slower_than_the_wait_is_still_paused_after_because_the_wait_counts_from_the_first_refusal()
    {
        var clock = new SteppedClock();
        var slow = true;
        var call = new Call(times: 1, () =>
        {
            // The first try alone takes longer than the whole wait, and is then refused.
            if (slow)
            {
                clock.Advance(Rule.Wait + TimeSpan.FromMilliseconds(500));
                slow = false;
            }

            return new IOException("held", SharingViolation);
        });

        var held = Run(call, Rule, clock, windows: true);

        Assert.True(held is null, $"The call was given up after its first refusal: {held?.Message}");
        Assert.Equal(2, call.Tries);
        Assert.Equal([Rule.Interval], clock.Pauses);
    }

    [Fact]
    public void An_access_denied_is_thrown_at_once_where_the_rule_does_not_count_it()
    {
        var clock = new SteppedClock();
        var call = new Call(times: int.MaxValue, () => new UnauthorizedAccessException("denied"));

        var thrown = Assert.Throws<UnauthorizedAccessException>(() => Run(call, Rule, clock, windows: true));

        Assert.Same(call.Last, thrown);
        Assert.Equal(1, call.Tries);
        Assert.Empty(clock.Pauses);
    }

    [Fact]
    public void An_access_denied_counts_as_a_hold_where_the_rule_says_so()
    {
        var clock = new SteppedClock();
        var call = new Call(times: 2, () => new UnauthorizedAccessException("denied"));

        var held = Run(call, Rule with { AccessDeniedCounts = true }, clock, windows: true);

        Assert.Null(held);
        Assert.Equal(3, call.Tries);
        Assert.Equal(2, clock.Pauses.Count);
    }

    public static TheoryData<Exception> NotAHold => new()
    {
        new IOException("no hresult"),
        new IOException("disk full", unchecked((int)0x80070070)),
        new IOException("access denied, as an IOException", unchecked((int)0x80070005)),
        new FileNotFoundException("gone"),
        new DirectoryNotFoundException("gone"),
        new PathTooLongException("long"),
        new InvalidOperationException("not a file system failure"),
    };

    [Theory]
    [MemberData(nameof(NotAHold))]
    public void Any_other_failure_is_thrown_at_once_even_where_an_access_denied_counts(Exception failure)
    {
        var clock = new SteppedClock();
        var call = new Call(times: int.MaxValue, () => failure);

        var thrown = Assert.Throws(failure.GetType(), () => Run(call, Rule with { AccessDeniedCounts = true }, clock, windows: true));

        Assert.Same(failure, thrown);
        Assert.Equal(1, call.Tries);
        Assert.Empty(clock.Pauses);
    }

    [Theory]
    [InlineData(SharingViolation, false)]
    [InlineData(LockViolation, false)]
    [InlineData(0, true)]
    public void Off_Windows_nothing_counts_and_the_call_is_tried_once(int hold, bool accessDenied)
    {
        var clock = new SteppedClock();
        var call = new Call(times: int.MaxValue, () => accessDenied ? new UnauthorizedAccessException("denied") : new IOException("held", hold));

        var thrown = Assert.ThrowsAny<Exception>(() => Run(call, Rule with { AccessDeniedCounts = true }, clock, windows: false));

        Assert.Same(call.Last, thrown);
        Assert.Equal(1, call.Tries);
        Assert.Empty(clock.Pauses);
    }

    [Fact]
    public void A_call_that_goes_through_at_once_is_tried_once_and_never_paused()
    {
        var clock = new SteppedClock();
        var call = new Call(times: 0, () => new IOException("never thrown", SharingViolation));

        Assert.Null(Run(call, Rule, clock, windows: true));

        Assert.Equal(1, call.Tries);
        Assert.Empty(clock.Pauses);
    }

    [Fact]
    public void A_cancelled_pause_ends_the_wait_with_its_cancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var call = new Call(times: int.MaxValue, () => new IOException("held", SharingViolation));

        Assert.Throws<OperationCanceledException>(() => HeldFileRetry.Run(call.Invoke, Rule, new SteppedClock(), HeldFileRetry.Sleep, windows: true, cancelled.Token));

        Assert.Equal(1, call.Tries);
    }

    [Fact]
    public void The_real_pause_waits_out_its_delay_and_returns_when_nothing_cancels_it()
    {
        var started = TimeProvider.System.GetTimestamp();

        HeldFileRetry.Sleep(TimeSpan.FromMilliseconds(30), CancellationToken.None);

        Assert.True(TimeProvider.System.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(25));
    }

    [Theory]
    [InlineData(10_000, "10 seconds")]
    [InlineData(2_000, "2 seconds")]
    [InlineData(1_000, "1 second")]
    [InlineData(300, "0.3 seconds")]
    [InlineData(1_500, "1.5 seconds")]
    public void A_wait_is_said_in_seconds_whatever_the_culture(int milliseconds, string said)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            // A culture whose decimal separator is a comma: a sentence is the same wherever the program runs.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("uk-UA");

            Assert.Equal(said, (Rule with { Wait = TimeSpan.FromMilliseconds(milliseconds) }).WaitInWords);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static Exception? Run(Call call, HeldFileRule rule, SteppedClock clock, bool windows) =>
        HeldFileRetry.Run(call.Invoke, rule, clock, clock.Pause, windows, CancellationToken.None);

    /// <summary>A call that fails with a made refusal a number of times, then goes through.</summary>
    private sealed class Call(int times, Func<Exception> refusal)
    {
        public int Tries { get; private set; }

        public Exception? Last { get; private set; }

        public void Invoke()
        {
            Tries++;
            if (Tries <= times)
            {
                Last = refusal();
                throw Last;
            }
        }
    }

    /// <summary>
    /// A clock that stands still until a pause is taken, and then moves by exactly the pause, or until a call moves it as a
    /// slow act would.
    /// </summary>
    private sealed class SteppedClock : TimeProvider
    {
        public TimeSpan Now { get; private set; }

        public List<TimeSpan> Pauses { get; } = [];

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Now.Ticks;

        public void Pause(TimeSpan delay, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Pauses.Add(delay);
            Now += delay;
        }

        public void Advance(TimeSpan by) => Now += by;
    }
}
