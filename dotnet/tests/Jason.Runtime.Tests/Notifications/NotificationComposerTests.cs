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

    [Theory]
    [InlineData(1, 4, "1 provider block holds 4 work items in Jason.")]
    [InlineData(1, 1, "1 provider block holds 1 work item in Jason.")]
    [InlineData(2, 0, "2 provider blocks hold 0 work items in Jason.")]
    [InlineData(12_000, 50_000, "10k+ provider blocks hold 10k+ work items in Jason.")]
    public void A_block_alone_says_how_many_blocks_hold_how_much_work(int blocks, int held, string summary)
    {
        var message = NotificationComposer.Compose(0, 0, blocks: new HeldByBlocks(blocks, held));

        Assert.Equal(summary, message.Summary);
        Assert.Equal("Ask your agent what is blocked, or run jason block list.", message.Look);
        Assert.Equal(blocks, message.Blocks);
    }

    [Theory]
    [InlineData(1, 0, false, false, "1 approval is waiting in Jason. 1 provider block holds 2 work items.", "Ask your agent, or run jason approval list, jason decision list and jason block list.")]
    [InlineData(0, 0, false, true, "1 provider block holds 2 work items. 1 account has events Jason did not read.", "Ask your agent, or run jason block list and jason events sources.")]
    [InlineData(0, 1, true, true, "1 question is waiting in Jason. 1 protective action failed in Jason, in cmp_A. 1 provider block holds 2 work items. 1 account has events Jason did not read.", "Ask your agent, or run jason approval list, jason decision list, jason workitem list --status failed --status expired, jason block list and jason events sources.")]
    public void A_block_beside_other_things_is_said_in_its_place_and_points_at_each_of_them(int approvals, int decisions, bool failed, bool gaps, string summary, string look)
    {
        var failures = failed ? new FailedProtectiveWork(1, ["cmp_A"], 0) : null;

        var message = NotificationComposer.Compose(approvals, decisions, failures, gaps ? 1 : 0, new HeldByBlocks(1, 2));

        Assert.Equal((summary, look), (message.Summary, message.Look));
    }

    /// <summary>Beside a block, failed protective work names one campaign and counts the rest, so five things still fit.</summary>
    [Fact]
    public void Failed_protective_work_beside_a_block_names_one_campaign_and_counts_the_rest()
    {
        var failures = new FailedProtectiveWork(5, ["cmp_A", "cmp_B", "cmp_C"], 2);

        Assert.Equal(
            "5 protective actions failed in Jason, in cmp_A and 4 more. 1 provider block holds 2 work items.",
            NotificationComposer.Compose(0, 0, failures, blocks: new HeldByBlocks(1, 2)).Summary);
        Assert.Equal(
            "5 protective actions failed in Jason, in cmp_A, cmp_B, cmp_C and 2 more.",
            NotificationComposer.Compose(0, 0, failures).Summary);
    }

    /// <summary>No block says nothing: no sentence and no command.</summary>
    [Fact]
    public void No_block_is_no_sentence() =>
        Assert.Equal(NotificationComposer.Compose(1, 0), NotificationComposer.Compose(1, 0, blocks: new HeldByBlocks(0, 0)));

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public void A_negative_block_count_is_not_a_pointer(int blocks, int held) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationComposer.Compose(0, 0, blocks: new HeldByBlocks(blocks, held)));

    /// <summary>
    /// The longest pointer the composer can make, whatever the counts — every count at four digits or far past them —
    /// still fits the message the delivery contract takes, read from the contract itself: a longer one would be refused
    /// before any plugin channel ran. Two shapes compete for the longest. Without a block, failed protective work names
    /// three campaigns and counts the rest, beside both waits and a gap; with a block beside them all, it names one. A count
    /// is never said in more than four characters, so the worst case is the same at 9999 as at the largest count there is,
    /// and each shape is held to its exact length, 399 and 396 of the 400, so that a wording that spends the characters
    /// left is seen being spent.
    /// </summary>
    [Theory]
    [Critical(Family.N3, "the message fits the contract")]
    [InlineData(9_999)]
    [InlineData(10_000)]
    [InlineData(int.MaxValue)]
    public void The_longest_message_fits_the_delivery_contract_whatever_the_counts(int count)
    {
        var id = "cmp_01JB6K8TQ2W9V4MZ0C3Y7H5NRD";
        var failed = new FailedProtectiveWork(count, [id, id, id], count);
        var withoutBlock = NotificationComposer.Compose(count, count, failed, eventGaps: count);
        var withBlock = NotificationComposer.Compose(count, count, failed, eventGaps: count, blocks: new HeldByBlocks(count, count));
        var bound = Jason.Contracts.Notifications.DeliveryCatalog.Find("notification.send")!.InputSchema["properties"]!["message"]!["maxLength"]!.GetValue<int>();

        foreach (var (shape, pointer, length) in new[] { ("without a block", withoutBlock, 399), ("with a block", withBlock, 396) })
        {
            Assert.True(pointer.Text.Length <= bound, $"At {count} the longest pointer {shape} is {pointer.Text.Length} characters; the delivery contract takes {bound}: {pointer.Text}");
            Assert.True(pointer.Text.Length == length, $"At {count} the longest pointer {shape} is {pointer.Text.Length} characters, not {length}: {pointer.Text}");
        }
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

    private const string Approval = "apr_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    private const string Question = "dec_01JBZ3X7Q9K2M4N6P8R0S2T4V6";

    /// <summary>
    /// The one item that waits is carried by its opaque id, beside whatever else is said, and only then; what the
    /// pointer says is the same text with or without it.
    /// </summary>
    [Theory]
    [InlineData(1, 0, Approval, Approval)]
    [InlineData(0, 1, Question, Question)]
    [InlineData(1, 0, Question, null)]
    [InlineData(0, 1, Approval, null)]
    [InlineData(2, 0, Approval, null)]
    [InlineData(1, 1, Approval, null)]
    [InlineData(1, 0, "apr_01jbz3x7q9k2m4n6p8r0s2t4v6", null)]
    [InlineData(1, 0, "cmp_01JBZ3X7Q9K2M4N6P8R0S2T4V6", null)]
    [InlineData(1, 0, null, null)]
    public void Only_the_one_item_that_waits_is_carried_and_only_by_its_own_prefix(int approvals, int decisions, string? item, string? carried)
    {
        var message = NotificationComposer.Compose(approvals, decisions, item: item);

        Assert.Equal(carried, message.Item);
        Assert.Equal(NotificationComposer.Compose(approvals, decisions).Text, message.Text);
    }

    /// <summary>One approval beside failed protective work still carries the approval: it is the one thing a person can act on.</summary>
    [Fact]
    public void One_item_beside_other_sentences_is_still_carried()
    {
        var message = NotificationComposer.Compose(1, 0, new FailedProtectiveWork(1, ["cmp_01JBZ3X7Q9K2M4N6P8R0S2T4V6"], 0), item: Approval);

        Assert.Equal(Approval, message.Item);
    }

    /// <summary>Nothing that waits for a person means no item, whatever is passed.</summary>
    [Fact]
    public void Failed_work_alone_carries_no_item() =>
        Assert.Null(NotificationComposer.Compose(0, 0, new FailedProtectiveWork(1, [], 0), item: Approval).Item);
}
