using Jason.Contracts.Api;
using Jason.Runtime.Journal;
using Jason.Runtime.WorkItems;

namespace Jason.Runtime.Tests.WorkItems;

public class WorkItemTransitionsTests
{
    private static readonly DateTime Noon = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    private static readonly (WorkItemStatus From, WorkItemStatus To)[] LegalPairs =
    [
        (WorkItemStatus.Created, WorkItemStatus.Scheduled),
        (WorkItemStatus.Created, WorkItemStatus.AwaitingApproval),
        (WorkItemStatus.Created, WorkItemStatus.Cancelled),
        (WorkItemStatus.Created, WorkItemStatus.Expired),
        (WorkItemStatus.Created, WorkItemStatus.Waiting),
        (WorkItemStatus.AwaitingApproval, WorkItemStatus.Created),
        (WorkItemStatus.AwaitingApproval, WorkItemStatus.Failed),
        (WorkItemStatus.AwaitingApproval, WorkItemStatus.Cancelled),
        (WorkItemStatus.AwaitingApproval, WorkItemStatus.Expired),
        (WorkItemStatus.Scheduled, WorkItemStatus.Processing),
        (WorkItemStatus.Scheduled, WorkItemStatus.Created),
        (WorkItemStatus.Scheduled, WorkItemStatus.Cancelled),
        (WorkItemStatus.Scheduled, WorkItemStatus.Failed),
        (WorkItemStatus.Processing, WorkItemStatus.Succeeded),
        (WorkItemStatus.Processing, WorkItemStatus.Failed),
        (WorkItemStatus.Processing, WorkItemStatus.Created),
        (WorkItemStatus.Processing, WorkItemStatus.Cancelled),
        (WorkItemStatus.Processing, WorkItemStatus.Waiting),
        (WorkItemStatus.Expired, WorkItemStatus.Created),
        (WorkItemStatus.Expired, WorkItemStatus.Cancelled),
        (WorkItemStatus.Waiting, WorkItemStatus.Created),
        (WorkItemStatus.Waiting, WorkItemStatus.Cancelled),
        (WorkItemStatus.Waiting, WorkItemStatus.Expired),
    ];

    [Fact]
    [Critical(Family.W1, "the transition table")]
    public void The_whole_matrix_agrees_with_the_table()
    {
        foreach (var from in Enum.GetValues<WorkItemStatus>())
        {
            foreach (var to in Enum.GetValues<WorkItemStatus>())
            {
                var expected = Array.IndexOf(LegalPairs, (from, to)) >= 0;
                Assert.Equal(expected, WorkItemTransitions.IsLegal(from, to));
            }
        }
    }

    /// <summary>
    /// Work parked for a person's decision leaves that state in four ways and no others: the person approves it
    /// and it is queued again, the person rejects it and it is over, somebody cancels the work, or its due date
    /// passes and the clock gives up on it. What it may never do is go straight to a dispatcher — a claim is
    /// precisely the thing the parking is there to prevent.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Created, WorkItemStatus.AwaitingApproval, true)]
    [InlineData(WorkItemStatus.AwaitingApproval, WorkItemStatus.Created, true)]
    [InlineData(WorkItemStatus.AwaitingApproval, WorkItemStatus.Failed, true)]
    [InlineData(WorkItemStatus.AwaitingApproval, WorkItemStatus.Cancelled, true)]
    [InlineData(WorkItemStatus.AwaitingApproval, WorkItemStatus.Expired, true)]
    // Critical: parked work never goes to a dispatcher.
    [InlineData(WorkItemStatus.AwaitingApproval, WorkItemStatus.Scheduled, false, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.W1)])]
    // Critical: parked work never goes to a dispatcher.
    [InlineData(WorkItemStatus.AwaitingApproval, WorkItemStatus.Processing, false, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.W1)])]
    [InlineData(WorkItemStatus.AwaitingApproval, WorkItemStatus.Succeeded, false)]
    [InlineData(WorkItemStatus.Scheduled, WorkItemStatus.AwaitingApproval, false)]
    public void Work_waiting_for_a_person_may_only_go_where_a_person_or_the_clock_sends_it(
        WorkItemStatus from, WorkItemStatus to, bool legal) =>
        Assert.Equal(legal, WorkItemTransitions.IsLegal(from, to));

    /// <summary>
    /// Waiting work is entered from a running attempt, which is the attempt ending waiting, or from the queue, which is the
    /// claim parking work on a provider block before any attempt exists; and it is left in three ways: the dispatcher
    /// releases it into the queue, somebody cancels it, or its due date passes. Like parked work, it never goes to a
    /// dispatcher straight from here, and it is never finished by waiting.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Processing, WorkItemStatus.Waiting, true)]
    [InlineData(WorkItemStatus.Waiting, WorkItemStatus.Created, true)]
    [InlineData(WorkItemStatus.Waiting, WorkItemStatus.Cancelled, true)]
    [InlineData(WorkItemStatus.Waiting, WorkItemStatus.Expired, true)]
    [InlineData(WorkItemStatus.Waiting, WorkItemStatus.Scheduled, false)]
    [InlineData(WorkItemStatus.Waiting, WorkItemStatus.Processing, false)]
    [InlineData(WorkItemStatus.Waiting, WorkItemStatus.Succeeded, false)]
    [InlineData(WorkItemStatus.Waiting, WorkItemStatus.Failed, false)]
    [InlineData(WorkItemStatus.Waiting, WorkItemStatus.AwaitingApproval, false)]
    [InlineData(WorkItemStatus.Created, WorkItemStatus.Waiting, true)]
    [InlineData(WorkItemStatus.Scheduled, WorkItemStatus.Waiting, false)]
    [InlineData(WorkItemStatus.AwaitingApproval, WorkItemStatus.Waiting, false)]
    [InlineData(WorkItemStatus.Expired, WorkItemStatus.Waiting, false)]
    public void Waiting_work_is_entered_from_a_run_or_the_claim_and_left_only_where_the_dispatcher_or_the_clock_sends_it(
        WorkItemStatus from, WorkItemStatus to, bool legal) =>
        Assert.True(legal == WorkItemTransitions.IsLegal(from, to), $"{from} to {to} is legal: {WorkItemTransitions.IsLegal(from, to)}; expected {legal}.");

    [Fact]
    public void Waiting_is_neither_final_nor_finished_and_a_release_keeps_no_finish_time()
    {
        Assert.DoesNotContain(WorkItemStatus.Waiting, WorkItemTransitions.Final);
        Assert.DoesNotContain(WorkItemStatus.Waiting, WorkItemTransitions.Terminal);

        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), now: Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Scheduled, Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Processing, Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Waiting, Noon.AddMinutes(1));
        Assert.Null(item.FinishedAt);
        Assert.Equal(Noon.AddMinutes(1), item.UpdatedAt);

        WorkItemTransitions.Apply(item, WorkItemStatus.Created, Noon.AddHours(1));
        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.Null(item.FinishedAt);
    }

    [Fact]
    public void Final_and_terminal_say_what_can_still_change()
    {
        Assert.Equal<IEnumerable<WorkItemStatus>>(
            [WorkItemStatus.Succeeded, WorkItemStatus.Failed, WorkItemStatus.Cancelled],
            WorkItemTransitions.Final.Order());
        Assert.Equal<IEnumerable<WorkItemStatus>>(
            [WorkItemStatus.Succeeded, WorkItemStatus.Failed, WorkItemStatus.Cancelled, WorkItemStatus.Expired],
            WorkItemTransitions.Terminal.Order());
        Assert.DoesNotContain(WorkItemStatus.Expired, WorkItemTransitions.Final);
    }

    [Fact]
    public void Finishing_an_item_stamps_the_moment_it_finished()
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), now: Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Scheduled, Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Processing, Noon.AddMinutes(1));
        Assert.Null(item.FinishedAt);

        WorkItemTransitions.Apply(item, WorkItemStatus.Succeeded, Noon.AddMinutes(2));

        Assert.Equal(WorkItemStatus.Succeeded, item.Status);
        Assert.Equal(Noon.AddMinutes(2), item.FinishedAt);
        Assert.Equal(Noon.AddMinutes(2), item.UpdatedAt);
    }

    [Fact]
    public void Reopening_an_expired_item_takes_its_finish_time_back()
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), now: Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Expired, Noon);
        Assert.Equal(Noon, item.FinishedAt);

        WorkItemTransitions.Apply(item, WorkItemStatus.Created, Noon.AddHours(1));

        Assert.Equal(WorkItemStatus.Created, item.Status);
        Assert.Null(item.FinishedAt);
    }

    [Fact]
    public void Cancelling_an_expired_item_still_finishes_it()
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), now: Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Expired, Noon);

        WorkItemTransitions.Apply(item, WorkItemStatus.Cancelled, Noon.AddHours(1));

        Assert.Equal(Noon.AddHours(1), item.FinishedAt);
    }

    [Fact]
    public void The_retry_moment_is_cleared_where_it_no_longer_means_anything()
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), now: Noon);
        item.RetryAfter = Noon.AddMinutes(1);

        WorkItemTransitions.Apply(item, WorkItemStatus.Scheduled, Noon);
        Assert.Null(item.RetryAfter);

        var cancelled = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), now: Noon);
        cancelled.RetryAfter = Noon.AddMinutes(1);
        WorkItemTransitions.Apply(cancelled, WorkItemStatus.Cancelled, Noon);
        Assert.Null(cancelled.RetryAfter);

        // Past its due date the dispatcher's retry moment means nothing either; a reopened item starts clean.
        var expired = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), now: Noon);
        expired.RetryAfter = Noon.AddMinutes(1);
        WorkItemTransitions.Apply(expired, WorkItemStatus.Expired, Noon);
        Assert.Null(expired.RetryAfter);
    }

    /// <summary>
    /// A doubt's two sentences leave the row when the send can never be asked about again — cancelled, failed or gone — and
    /// stay on an expired one, which may be reopened and then parks with them.
    /// </summary>
    [Theory]
    [InlineData(WorkItemStatus.Cancelled, true)]
    [InlineData(WorkItemStatus.Failed, true)]
    [InlineData(WorkItemStatus.Succeeded, true)]
    [InlineData(WorkItemStatus.Expired, false)]
    public void A_send_that_can_never_be_asked_about_again_keeps_no_doubt_s_sentence(WorkItemStatus to, bool cleared)
    {
        var send = WorkItemFactory.NewProviderOp(WorkItemFactory.NewCampaign(), "message.send", Noon);
        send.AskPersonNote = "the brief never mentions pricing.";
        send.ReviewNote = "the subject promises a discount.";
        if (to is WorkItemStatus.Failed or WorkItemStatus.Succeeded)
        {
            // A send fails from the claim, as one the pre-flight refuses does; it goes once its run has started.
            WorkItemTransitions.Apply(send, WorkItemStatus.Scheduled, Noon);
        }

        if (to == WorkItemStatus.Succeeded)
        {
            WorkItemTransitions.Apply(send, WorkItemStatus.Processing, Noon);
        }

        WorkItemTransitions.Apply(send, to, Noon);

        Assert.Equal(cleared, send.AskPersonNote is null);
        Assert.Equal(cleared, send.ReviewNote is null);
    }

    [Fact]
    [Critical(Family.W1, "illegal moves throw")]
    public void An_illegal_pair_is_a_bug_rather_than_a_bad_request()
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), now: Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Scheduled, Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Processing, Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Succeeded, Noon);

        Assert.Throws<InvalidOperationException>(() => WorkItemTransitions.Apply(item, WorkItemStatus.Created, Noon));
    }

    [Fact]
    [Critical(Family.W1, "every move is journaled")]
    public void Every_transition_names_the_reserved_kind_that_records_it()
    {
        Assert.Equal(JournalKinds.WorkItemScheduled, WorkItemTransitions.JournalKind(WorkItemStatus.Created, WorkItemStatus.Scheduled));
        Assert.Equal(JournalKinds.WorkItemProcessing, WorkItemTransitions.JournalKind(WorkItemStatus.Scheduled, WorkItemStatus.Processing));
        Assert.Equal(JournalKinds.WorkItemSucceeded, WorkItemTransitions.JournalKind(WorkItemStatus.Processing, WorkItemStatus.Succeeded));
        Assert.Equal(JournalKinds.WorkItemFailed, WorkItemTransitions.JournalKind(WorkItemStatus.Processing, WorkItemStatus.Failed));
        Assert.Equal(JournalKinds.WorkItemCancelled, WorkItemTransitions.JournalKind(WorkItemStatus.Created, WorkItemStatus.Cancelled));
        Assert.Equal(JournalKinds.WorkItemExpired, WorkItemTransitions.JournalKind(WorkItemStatus.Created, WorkItemStatus.Expired));
        Assert.Equal(JournalKinds.WorkItemReleased, WorkItemTransitions.JournalKind(WorkItemStatus.Scheduled, WorkItemStatus.Created));
        Assert.Equal(JournalKinds.WorkItemReleased, WorkItemTransitions.JournalKind(WorkItemStatus.Processing, WorkItemStatus.Created));
        Assert.Equal(JournalKinds.WorkItemReopened, WorkItemTransitions.JournalKind(WorkItemStatus.Expired, WorkItemStatus.Created));
        Assert.Equal(JournalKinds.WorkItemWaiting, WorkItemTransitions.JournalKind(WorkItemStatus.Processing, WorkItemStatus.Waiting));
        Assert.Equal(JournalKinds.WorkItemResumed, WorkItemTransitions.JournalKind(WorkItemStatus.Waiting, WorkItemStatus.Created));
        Assert.Equal(JournalKinds.WorkItemCancelled, WorkItemTransitions.JournalKind(WorkItemStatus.Waiting, WorkItemStatus.Cancelled));
        Assert.Equal(JournalKinds.WorkItemExpired, WorkItemTransitions.JournalKind(WorkItemStatus.Waiting, WorkItemStatus.Expired));
    }
}
