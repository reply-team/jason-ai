using Jason.Runtime.Notifications;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>A pointer says how many things wait and where to look, and nothing about any of them.</summary>
public sealed class NotificationComposerTests
{
    [Theory]
    [InlineData(1, 0, "1 approval is waiting in Jason.")]
    [InlineData(30, 0, "30 approvals are waiting in Jason.")]
    [InlineData(0, 1, "1 question is waiting in Jason.")]
    [InlineData(0, 2, "2 questions are waiting in Jason.")]
    [InlineData(3, 1, "3 approvals and 1 question are waiting in Jason.")]
    [InlineData(1, 1, "1 approval and 1 question are waiting in Jason.")]
    [InlineData(1200, 2, "1200 approvals and 2 questions are waiting in Jason.")]
    public void The_pointer_says_how_many_wait_and_nothing_else(int approvals, int decisions, string summary)
    {
        var message = NotificationComposer.Compose(approvals, decisions);
        Assert.Equal("Jason", message.Title);
        Assert.Equal(summary, message.Summary);
        Assert.Equal("Ask your agent what Jason is waiting for, or run jason approval list and jason decision list.", message.Look);
        Assert.Equal($"{summary} {message.Look}", message.Text);
        Assert.Equal((approvals, decisions), (message.Approvals, message.Decisions));
    }

    [Theory]
    [InlineData(1, "1 account has events Jason did not read.")]
    [InlineData(2, "2 accounts have events Jason did not read.")]
    public void A_gap_alone_says_how_many_accounts_were_not_read(int gaps, string summary)
    {
        var message = NotificationComposer.Compose(0, 0, eventGaps: gaps);

        Assert.Equal(summary, message.Summary);
        Assert.Equal("Ask your agent what Jason did not read.", message.Look);
        Assert.Equal(gaps, message.EventGaps);
    }

    [Theory]
    [InlineData(1, 0, false, "Ask your agent, or run jason approval list and jason decision list.")]
    [InlineData(0, 0, true, "Ask your agent, or run jason workitem list --status failed --status expired.")]
    [InlineData(0, 1, true, "Ask your agent, or run jason approval list, jason decision list and jason workitem list --status failed --status expired.")]
    public void A_gap_beside_other_things_points_at_each_of_them(int approvals, int decisions, bool failed, string look)
    {
        var failures = failed ? new FailedProtectiveWork(1, ["cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD"], 0) : null;

        Assert.Equal(look, NotificationComposer.Compose(approvals, decisions, failures, eventGaps: 1).Look);
    }

    /// <summary>
    /// The longest pointer the composer can make — every count at four digits, three campaigns named and more counted, and
    /// a gap — still fits the message the delivery contract takes, read from the contract itself: a longer one would be
    /// refused before any plugin channel ran.
    /// </summary>
    [Fact]
    public void The_longest_message_fits_the_delivery_contract()
    {
        var id = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
        var longest = NotificationComposer.Compose(9_999, 9_999, new FailedProtectiveWork(9_999, [id, id, id], 9_999), eventGaps: 9_999);
        var bound = Jason.Contracts.Notifications.DeliveryCatalog.Find("notification.send")!.InputSchema["properties"]!["message"]!["maxLength"]!.GetValue<int>();

        Assert.True(longest.Text.Length <= bound, $"The longest pointer is {longest.Text.Length} characters; the delivery contract takes {bound}: {longest.Text}");
    }

    /// <summary>Nothing waiting is never composed: the sender sends nothing then.</summary>
    [Fact]
    public void Nothing_waiting_is_not_a_pointer() => Assert.Throws<ArgumentOutOfRangeException>(() => NotificationComposer.Compose(0, 0));

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void A_negative_count_is_not_a_pointer(int approvals, int decisions) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationComposer.Compose(approvals, decisions));
}
