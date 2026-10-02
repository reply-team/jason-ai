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

    /// <summary>Nothing waiting is never composed: the sender sends nothing then.</summary>
    [Fact]
    public void Nothing_waiting_is_not_a_pointer() => Assert.Throws<ArgumentOutOfRangeException>(() => NotificationComposer.Compose(0, 0));

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(1, -1)]
    public void A_negative_count_is_not_a_pointer(int approvals, int decisions) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationComposer.Compose(approvals, decisions));
}
