using Jason.Runtime.Events;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Tests.Events;

/// <summary>
/// When an account's events are read next, what the read starts from, and whether the inbound read may leave a campaign
/// to them: three pure answers over a source's row and the clock, held here on a clock that does not move.
/// </summary>
public class EventScheduleTests
{
    private const int Cadence = 600;

    /// <summary>The inbound read's shipped look-back: four hours, far longer than two cadences.</summary>
    private const int Lookback = 14_400;

    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    // ---------------------------------------------------------------------------------------------------------------
    // When
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Due_one_cadence_after_the_read_started()
    {
        var source = Source(started: Now);

        Assert.False(EventSchedule.IsDue(source, readByThisProcess: true, Cadence, Now.AddSeconds(599)), "Due 599 s after the read started.");
        Assert.True(EventSchedule.IsDue(source, readByThisProcess: true, Cadence, Now.AddSeconds(600)), "Not due 600 s after the read started.");
    }

    [Theory]
    [InlineData(1, 1_200)]
    [InlineData(2, 2_400)]
    [InlineData(7, 76_800)]
    [InlineData(8, 86_400)]
    [InlineData(40, 86_400)]
    public void Failures_double_the_wait_up_to_a_day(int failures, int wait)
    {
        var source = Source(started: Now, failures: failures, failedAs: "transient");

        Assert.Equal(Now.AddSeconds(wait), EventSchedule.DueAt(source, readByThisProcess: true, Cadence));
    }

    [Theory]
    [InlineData("permanent")]
    [InlineData("validation")]
    public void A_permanent_or_a_validation_failure_waits_a_day(string failedAs)
    {
        var source = Source(started: Now, failures: 1, failedAs: failedAs);

        Assert.Equal(Now.AddDays(1), EventSchedule.DueAt(source, readByThisProcess: true, Cadence));
    }

    [Fact]
    public void A_read_that_stopped_with_more_to_read_is_due_at_once()
    {
        var source = Source(started: Now);
        source.More = true;

        Assert.True(EventSchedule.IsDue(source, readByThisProcess: true, Cadence, Now), "A read that stopped with more to read is not due at once.");
    }

    /// <summary>
    /// A read that stopped with more to read is due from the moment it ended, not before everything: an account overdue
    /// for longer still goes first, so one account's backlog takes turns with the others rather than holding the reader.
    /// </summary>
    [Fact]
    public void A_read_that_stopped_with_more_waits_behind_an_account_overdue_for_longer()
    {
        var backlog = Source(started: Now.AddSeconds(-5), id: 1);
        backlog.More = true;
        backlog.ReadEndedAt = Now;
        var overdue = Source(started: Now.AddSeconds(-700), id: 2);

        Assert.Equal(Now, EventSchedule.DueAt(backlog, readByThisProcess: true, Cadence));
        Assert.Equal([2, 1], EventSchedule.MostOverdueFirst([backlog, overdue], _ => true, Cadence, Now).Select(source => source.Id));
    }

    [Fact]
    public void A_source_this_process_has_not_read_is_due_at_once()
    {
        var source = Source(started: Now, failures: 3, failedAs: "permanent");

        Assert.True(EventSchedule.IsDue(source, readByThisProcess: false, Cadence, Now), "A start does not catch up a source it has not read.");
        Assert.True(EventSchedule.IsDue(Source(started: null), readByThisProcess: true, Cadence, Now), "A source never read is not due.");
    }

    [Fact]
    public void The_most_overdue_source_comes_first()
    {
        var recent = Source(started: Now.AddSeconds(-700), id: 1);
        var older = Source(started: Now.AddSeconds(-900), id: 2);
        var waiting = Source(started: Now.AddSeconds(-100), id: 3);
        var fresh = Source(started: null, id: 4);

        var order = EventSchedule.MostOverdueFirst([recent, older, waiting, fresh], _ => true, Cadence, Now).Select(source => source.Id);

        Assert.Equal([4, 2, 1], order);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // From where
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_first_start_reads_thirty_days_back()
    {
        var plan = EventSchedule.Plan(Source(started: null), Now.AddMilliseconds(750));

        Assert.Equal(EventReadMode.FirstStart, plan.Mode);
        Assert.Equal("""{"cursor":null,"since":"2026-09-18T12:00:00Z","until":null,"limit":100}""", plan.Input.ToJsonString());
        Assert.Null(plan.Gap);
    }

    [Fact]
    public void A_cursor_younger_than_thirty_days_is_read_from()
    {
        var plan = EventSchedule.Plan(Source(cursor: "c-41", cursorAt: Now.AddDays(-29)), Now);

        Assert.Equal(EventReadMode.Cursor, plan.Mode);
        Assert.Equal("""{"cursor":"c-41","since":null,"until":null,"limit":100}""", plan.Input.ToJsonString());
        Assert.Null(plan.Gap);
    }

    [Fact]
    public void A_cursor_thirty_days_old_to_the_tick_is_still_read_from()
    {
        var plan = EventSchedule.Plan(Source(cursor: "c-41", cursorAt: Now.AddDays(-30)), Now);

        Assert.Equal(EventReadMode.Cursor, plan.Mode);
        Assert.Null(plan.Gap);
    }

    [Fact]
    public void An_older_cursor_reads_thirty_days_back_and_names_what_was_not_read()
    {
        var stored = Now.AddDays(-30).AddTicks(-1);
        var plan = EventSchedule.Plan(Source(cursor: "c-41", cursorAt: stored), Now);

        Assert.Equal(EventReadMode.Gap, plan.Mode);
        Assert.Equal("""{"cursor":null,"since":"2026-09-18T12:00:00Z","until":null,"limit":100}""", plan.Input.ToJsonString());
        Assert.Equal(new EventGap(stored, Now.AddDays(-30)), plan.Gap);
    }

    [Fact]
    public void A_forgotten_cursor_is_read_by_date_from_a_day_before_it()
    {
        var plan = EventSchedule.Plan(Source(cursor: null, cursorAt: Now.AddDays(-3)), Now);

        Assert.Equal(EventReadMode.ForgottenCursor, plan.Mode);
        Assert.Equal("""{"cursor":null,"since":"2026-10-14T12:00:00Z","until":null,"limit":100}""", plan.Input.ToJsonString());
        Assert.Null(plan.Gap);
    }

    [Fact]
    public void A_cursor_forgotten_long_ago_is_read_thirty_days_back_and_names_what_was_not_read()
    {
        var stored = Now.AddDays(-40);
        var plan = EventSchedule.Plan(Source(cursor: null, cursorAt: stored), Now);

        Assert.Equal(EventReadMode.ForgottenCursor, plan.Mode);
        Assert.Equal("2026-09-18T12:00:00Z", (string?)plan.Input["since"]);
        Assert.Equal(new EventGap(stored.AddDays(-1), Now.AddDays(-30)), plan.Gap);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Healthy
    // ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Healthy_while_the_last_read_succeeded_and_started_within_two_cadences()
    {
        var source = Source(started: Now.AddSeconds(-2 * Cadence), succeeded: Now.AddSeconds(-2 * Cadence));

        Assert.True(EventSchedule.IsHealthy(source, Cadence, Lookback, Now), "A source whose last read succeeded two cadences ago is not healthy.");
        Assert.False(EventSchedule.IsHealthy(source, Cadence, Lookback, Now.AddSeconds(1)), "A source with no success for over two cadences is healthy.");
    }

    [Fact]
    public void Not_healthy_once_its_last_read_failed()
    {
        var source = Source(started: Now.AddSeconds(-60), succeeded: Now.AddSeconds(-660), failures: 1, failedAs: "transient");

        Assert.False(EventSchedule.IsHealthy(source, Cadence, Lookback, Now), "A source whose last read failed is healthy.");
    }

    [Fact]
    public void Never_healthy_before_its_first_successful_read()
    {
        Assert.False(EventSchedule.IsHealthy(null, Cadence, Lookback, Now), "A source with no row is healthy.");
        Assert.False(EventSchedule.IsHealthy(Source(started: Now.AddSeconds(-10)), Cadence, Lookback, Now), "A source being read for the first time is healthy.");
    }

    /// <summary>
    /// A source is healthy for the shorter of two cadences and the inbound read's look-back: past the look-back, the
    /// campaigns handed back would be read from later than the source's last read that succeeded.
    /// </summary>
    [Fact]
    public void The_window_is_the_look_back_where_that_is_shorter_than_two_cadences()
    {
        var source = Source(started: Now.AddSeconds(-Lookback), succeeded: Now.AddSeconds(-Lookback));

        Assert.True(EventSchedule.IsHealthy(source, 86_400, Lookback, Now), "Not healthy within the look-back.");
        Assert.False(EventSchedule.IsHealthy(source, 86_400, Lookback, Now.AddSeconds(1)), "Healthy past the look-back, though within two cadences.");
    }

    [Fact]
    public void A_look_back_of_zero_never_leaves_a_campaign_to_the_events()
    {
        var source = Source(started: Now, succeeded: Now);

        Assert.False(EventSchedule.IsHealthy(source, Cadence, 0, Now), "Healthy with a look-back of 0.");
    }

    [Fact]
    public void The_defaults_keep_twenty_minutes()
    {
        var cadence = new Jason.Runtime.Configuration.DispatcherOptions().EventsCadenceSeconds;
        var lookback = new Jason.Runtime.Configuration.InboundOptions().LookbackSeconds;
        var source = Source(started: Now, succeeded: Now);

        Assert.True(EventSchedule.IsHealthy(source, cadence, lookback, Now.AddMinutes(20)), "Not healthy twenty minutes on, at the defaults.");
        Assert.False(EventSchedule.IsHealthy(source, cadence, lookback, Now.AddMinutes(20).AddSeconds(1)), "Healthy past twenty minutes, at the defaults.");
    }

    private static EventSource Source(
        DateTime? started = null,
        DateTime? succeeded = null,
        int failures = 0,
        string? failedAs = null,
        string? cursor = null,
        DateTime? cursorAt = null,
        int id = 1) =>
        new()
        {
            Id = id,
            PublicId = "evs_01JB6K8TQ2W9V4MZ0C3Y7H5NRD",
            PluginId = "reader",
            BindingIdentity = EventSource.NoBinding,
            Cursor = cursor,
            CursorAt = cursorAt,
            CreatedAt = Now.AddDays(-60),
            ReadStartedAt = started,
            ReadEndedAt = started,
            SucceededAt = succeeded,
            Failures = failures,
            LastCode = failedAs is null ? null : "events_unreachable",
            LastClass = failedAs,
        };
}
