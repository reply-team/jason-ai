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
        Assert.Equal("Ask your agent what Jason did not read, or run jason events sources.", message.Look);
        Assert.Equal(gaps, message.EventGaps);
    }

    [Theory]
    [InlineData(1, 0, false, "Ask your agent, or run jason approval list, jason decision list and jason events sources.")]
    [InlineData(0, 0, true, "Ask your agent, or run jason workitem list --status failed --status expired and jason events sources.")]
    [InlineData(0, 1, true, "Ask your agent, or run jason approval list, jason decision list, jason workitem list --status failed --status expired and jason events sources.")]
    public void A_gap_beside_other_things_points_at_each_of_them(int approvals, int decisions, bool failed, string look)
    {
        var failures = failed ? new FailedProtectiveWork(1, ["cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD"], 0) : null;

        Assert.Equal(look, NotificationComposer.Compose(approvals, decisions, failures, eventGaps: 1).Look);
    }

    /// <summary>
    /// The longest pointer the composer can make, whatever the counts — every count at four digits or far past them, three
    /// campaigns named and more counted, and a gap — still fits the message the delivery contract takes, read from the
    /// contract itself: a longer one would be refused before any plugin channel ran. A count is never said in more than
    /// four characters, so the worst case is the same at 9999 as at the largest count there is, and it is held to its exact
    /// length, 399 of the 400, so that a wording that spends the one character left is seen being spent.
    /// </summary>
    [Theory]
    [InlineData(9_999)]
    [InlineData(10_000)]
    [InlineData(int.MaxValue)]
    public void The_longest_message_fits_the_delivery_contract_whatever_the_counts(int count)
    {
        var id = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
        var longest = NotificationComposer.Compose(count, count, new FailedProtectiveWork(count, [id, id, id], count), eventGaps: count);
        var bound = Jason.Contracts.Notifications.DeliveryCatalog.Find("notification.send")!.InputSchema["properties"]!["message"]!["maxLength"]!.GetValue<int>();

        Assert.True(longest.Text.Length <= bound, $"At {count} the longest pointer is {longest.Text.Length} characters; the delivery contract takes {bound}: {longest.Text}");
        Assert.True(longest.Text.Length == 399, $"At {count} the longest pointer is {longest.Text.Length} characters, not 399: {longest.Text}");
    }

    /// <summary>A count is said as it is up to four digits, and past them as "10k+": a toast needs the size of it, not the figure.</summary>
    [Theory]
    [InlineData(9_999, 0, "9999 approvals are waiting in Jason.")]
    [InlineData(10_000, 0, "10k+ approvals are waiting in Jason.")]
    [InlineData(1, 123_456, "1 approval and 10k+ questions are waiting in Jason.")]
    public void A_count_past_four_digits_is_said_as_ten_thousand_and_more(int approvals, int decisions, string summary)
    {
        Assert.Equal(summary, NotificationComposer.Compose(approvals, decisions).Summary);
        Assert.Equal((approvals, decisions), (NotificationComposer.Compose(approvals, decisions).Approvals, NotificationComposer.Compose(approvals, decisions).Decisions));
    }

    [Fact]
    public void Campaigns_past_four_digits_more_are_said_the_same_way()
    {
        var message = NotificationComposer.Compose(0, 0, new FailedProtectiveWork(20_000, ["cmp_A", "cmp_B", "cmp_C"], 19_997));

        Assert.Equal("10k+ protective actions failed in Jason, in cmp_A, cmp_B, cmp_C and 10k+ more.", message.Summary);
        Assert.Equal(20_000, message.ProtectiveFailures);
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
