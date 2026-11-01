using Jason.Runtime.Notifications;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// The notifier's state, held under one lock: which notification is being sent. A release or a return acts on its own
/// notification only, so a second notification handed over in between is never freed by the first one's end.
/// </summary>
public sealed class NotifierStateTests
{
    private static readonly DateTime At = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static NotifierState Started()
    {
        var state = new NotifierState();
        state.Start(1, "jrn_1", lastDeliveredAt: null, seed: new Burst(1, At, At));
        return state;
    }

    private static NotificationRequest HandOver(NotifierState state)
    {
        Assert.True(state.TryHandOver((_, _) => true, out var request));
        return request;
    }

    /// <summary>
    /// The sender's failure path returns its burst and then, in its finally, releases: a scan landing between the two
    /// hands over the next notification, which the late release must leave in flight.
    /// </summary>
    [Fact]
    public void A_release_after_a_return_leaves_the_next_notification_in_flight()
    {
        var state = Started();
        var first = HandOver(state);

        state.Return(first);
        var second = HandOver(state);
        state.Release(first.NotificationId);

        Assert.True(state.InFlight);
        Assert.False(state.TryHandOver((_, _) => true, out _));

        state.Release(second.NotificationId);
        Assert.False(state.InFlight);
    }

    /// <summary>A return of a notification that is not the one in flight puts its burst back and frees nothing.</summary>
    [Fact]
    public void A_return_frees_only_its_own_notification()
    {
        var state = Started();
        var first = HandOver(state);
        state.Release(first.NotificationId);
        state.Advance(2, "jrn_2", [At.AddMinutes(1)]);
        var second = HandOver(state);

        state.Return(first);

        Assert.True(state.InFlight);
        Assert.Equal(1, state.Pending!.Lines);
        state.Release(second.NotificationId);
        Assert.False(state.InFlight);
    }
}
