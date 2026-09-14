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
        (WorkItemStatus.Created, WorkItemStatus.Cancelled),
        (WorkItemStatus.Created, WorkItemStatus.Expired),
        (WorkItemStatus.Scheduled, WorkItemStatus.Processing),
        (WorkItemStatus.Scheduled, WorkItemStatus.Created),
        (WorkItemStatus.Scheduled, WorkItemStatus.Cancelled),
        (WorkItemStatus.Scheduled, WorkItemStatus.Failed),
        (WorkItemStatus.Processing, WorkItemStatus.Succeeded),
        (WorkItemStatus.Processing, WorkItemStatus.Failed),
        (WorkItemStatus.Processing, WorkItemStatus.Created),
        (WorkItemStatus.Processing, WorkItemStatus.Cancelled),
        (WorkItemStatus.Expired, WorkItemStatus.Created),
        (WorkItemStatus.Expired, WorkItemStatus.Cancelled),
    ];

    [Fact]
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
    }

    [Fact]
    public void An_illegal_pair_is_a_bug_rather_than_a_bad_request()
    {
        var item = WorkItemFactory.NewAiRole(WorkItemFactory.NewCampaign(), now: Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Scheduled, Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Processing, Noon);
        WorkItemTransitions.Apply(item, WorkItemStatus.Succeeded, Noon);

        Assert.Throws<InvalidOperationException>(() => WorkItemTransitions.Apply(item, WorkItemStatus.Created, Noon));
    }

    [Fact]
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
    }
}
