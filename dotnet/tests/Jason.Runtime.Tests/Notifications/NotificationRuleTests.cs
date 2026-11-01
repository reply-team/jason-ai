using Jason.Runtime.Configuration;
using Jason.Runtime.Notifications;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// When a burst of things that need a person has settled enough to say so once: (a) the newest line has been quiet
/// for the window, or (b) the oldest has waited the cap; and (c) the floor since the last notification that reached
/// anybody has passed; and (d) the window has passed since the runtime started.
/// </summary>
public sealed class NotificationRuleTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(60), MaxWait = TimeSpan.FromMinutes(10), Floor = TimeSpan.FromMinutes(30);

    private static bool Due(Burst burst, double atSeconds, double? lastDeliveredSeconds = null) =>
        NotificationRule.IsDue(burst, Start.AddSeconds(atSeconds), Start,
            lastDeliveredSeconds is { } l ? Start.AddSeconds(l) : null, Quiet, MaxWait, Floor);

    /// <summary>(a): sent once the newest line is a quiet window old.</summary>
    [Fact]
    public void A_burst_is_sent_once_it_has_gone_quiet()
    {
        var burst = new Burst(1, Start.AddSeconds(100), Start.AddSeconds(100));
        Assert.False(Due(burst, 159));
        Assert.True(Due(burst, 160));
    }

    /// <summary>(b): a burst that never goes quiet is sent when its oldest line has waited the cap — 60 parks ten seconds apart.</summary>
    [Fact]
    [Critical(Family.N3, "the cap")]
    public void A_burst_that_never_goes_quiet_is_sent_at_the_cap()
    {
        var burst = new Burst(60, Start.AddSeconds(100), Start.AddSeconds(690));
        Assert.False(Due(burst, 699));
        Assert.True(Due(burst, 700));
    }

    /// <summary>(c): nothing within the floor of the last one that reached anybody.</summary>
    [Fact]
    [Critical(Family.N3, "the floor")]
    public void Nothing_is_sent_within_the_floor_of_the_last_delivery()
    {
        var burst = new Burst(1, Start.AddSeconds(1_000), Start.AddSeconds(1_000));
        Assert.False(Due(burst, 2_000, lastDeliveredSeconds: 300));
        Assert.True(Due(burst, 2_100, lastDeliveredSeconds: 300));
    }

    /// <summary>(d), the start grace: lines from before a sign-in are not sent the moment the runtime starts.</summary>
    [Fact]
    public void Nothing_is_sent_before_a_quiet_window_has_passed_since_the_start()
    {
        var before = new Burst(3, Start.AddHours(-2), Start.AddHours(-1));
        Assert.False(Due(before, 59));
        Assert.True(Due(before, 60));
    }

    /// <summary>A floor of zero is no floor: a delivery a second ago delays nothing.</summary>
    [Fact]
    public void A_floor_of_zero_spaces_nothing()
    {
        var burst = new Burst(1, Start.AddSeconds(100), Start.AddSeconds(100));
        Assert.True(NotificationRule.IsDue(burst, Start.AddSeconds(160), Start, Start.AddSeconds(159), Quiet, MaxWait, TimeSpan.Zero));
    }

    /// <summary>A burst grows by its lines: one more, with the window stretched to cover it.</summary>
    [Fact]
    public void A_line_widens_the_burst_it_joins()
    {
        var burst = new Burst(1, Start.AddSeconds(100), Start.AddSeconds(100)).With(Start.AddSeconds(150)).With(Start.AddSeconds(90));
        Assert.Equal(new Burst(3, Start.AddSeconds(90), Start.AddSeconds(150)), burst);
    }

    /// <summary>Never less than two ticks: a quiet window shorter than the scan could not be seen to have passed.</summary>
    [Theory]
    [InlineData(60, 10, 60)]
    [InlineData(5, 10, 20)]
    [InlineData(30, 30, 60)]
    public void The_quiet_window_is_never_less_than_two_ticks(int quiet, int tick, int effective) =>
        Assert.Equal(TimeSpan.FromSeconds(effective),
            NotificationRule.EffectiveQuiet(new NotificationsOptions { QuietSeconds = quiet }, new DispatcherOptions { TickSeconds = tick }));
}
