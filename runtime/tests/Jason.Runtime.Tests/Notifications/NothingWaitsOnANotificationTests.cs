using Jason.Contracts.Api;
using Jason.Contracts.Notifications;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Execution;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Tests.Dispatch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// A notification is said about work; it is never something the work waits on. Whatever the notifier does — and
/// whatever goes wrong while it does it — the work a scan claimed is already handed out.
/// </summary>
public sealed class NothingWaitsOnANotificationTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Two runs over the same seed — a parked approval and a raised question — one with notifications off and one in
    /// which every channel fails or is skipped. The work, the approval and the decision come out of both the same,
    /// field by field, and the journal the same kind by kind: the second run has one line more, and it is the
    /// notification's own.
    /// </summary>
    [Fact]
    public async Task Every_channel_failing_changes_nothing_but_the_notification_line()
    {
        var off = await RunAsync(notify: false);
        var on = await RunAsync(notify: true);

        Assert.Equal(off.Items, on.Items);
        Assert.Equal(off.Approvals, on.Approvals);
        Assert.Equal(off.Decisions, on.Decisions);
        Assert.Equal(off.Lines, on.Lines.Where(line => line.Kind != JournalKinds.NotificationSent));
        Assert.Equal(off.Lines.Count + 1, on.Lines.Count);
        Assert.Equal(JournalKinds.NotificationSent, on.Lines[^1].Kind);
    }

    private static async Task<Seen> RunAsync(bool notify)
    {
        using var harness = new DispatchHarness(Start);
        harness.Status.RuntimeStartedAt = Start;
        if (notify)
        {
            harness.NotificationsOn();
        }

        await harness.Runner.ScanOnceAsync(Ct);
        await Waiting.ApprovalsAsync(harness, 1, Ct);
        await Waiting.DecisionAsync(harness, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.Runner.ScanOnceAsync(Ct);

        if (notify)
        {
            var sender = new NotificationSender(
                harness.Scopes,
                new FakeChannels(
                    FakeChannel.Failing("notification_rejected", FailureClass.Permanent),
                    FakeChannel.Skipped(NotAttemptedReasons.NoDesktop)),
                harness.NotifierState,
                harness.NotificationStatus,
                harness.Resolve<LiveSettings<NotificationsOptions>>(),
                harness.Clock,
                harness.Resolve<ILogger<NotificationSender>>());
            Assert.NotNull(await sender.SendAsync(Assert.Single(harness.Queue.Requests), Ct));
        }
        else
        {
            Assert.Empty(harness.Queue.Requests);
        }

        await using var db = harness.Open();
        var items = await db.WorkItems.AsNoTracking().OrderBy(w => w.Id).Select(w => new { w.Status, w.AttemptCount, w.Kind, w.DueAt, w.UpdatedAt }).ToListAsync(Ct);
        var approvals = await db.Approvals.AsNoTracking().OrderBy(a => a.Id).Select(a => new { a.Status, a.Reason, a.Operation, a.RequestedAt, a.DecidedAt }).ToListAsync(Ct);
        var decisions = await db.Decisions.AsNoTracking().OrderBy(d => d.Id).Select(d => new { d.Status, d.Question, d.RaisedAt, d.AnsweredAt }).ToListAsync(Ct);
        return new Seen(
            [.. items.Select(item => $"{item}")],
            [.. approvals.Select(approval => $"{approval}")],
            [.. decisions.Select(decision => $"{decision}")],
            [.. await db.Journal.AsNoTracking().OrderBy(e => e.Id).Select(e => new Line(e.Kind, e.Key)).ToListAsync(Ct)]);
    }

    private sealed record Line(string Kind, string? Key);

    private sealed record Seen(IReadOnlyList<string> Items, IReadOnlyList<string> Approvals, IReadOnlyList<string> Decisions, IReadOnlyList<Line> Lines)
    {
        public bool Equals(Seen? other) => other is not null
            && Items.SequenceEqual(other.Items) && Approvals.SequenceEqual(other.Approvals)
            && Decisions.SequenceEqual(other.Decisions) && Lines.SequenceEqual(other.Lines);

        public override int GetHashCode() => Items.Count;
    }

    [Fact]
    [Critical(Family.N3, "notifications never block work")]
    public async Task A_notifier_that_throws_still_leaves_the_claimed_work_dispatched()
    {
        var command = FakeCommand.Returning(new CommandOutcome.Completed(null));
        using var harness = new DispatchHarness(Start, o => o.RetryDelaySeconds = 0, commands: command);
        harness.NotificationsOn();
        await harness.Runner.ScanOnceAsync(Ct);

        await harness.WriteLineAsync(JournalKinds.ApprovalRequested, Ct);
        harness.Clock.Advance(TimeSpan.FromSeconds(60));
        await harness.SeedClaimableAsync(Ct);
        harness.Queue.Refuse = true;

        var report = await harness.Runner.ScanOnceAsync(Ct);

        Assert.Equal(1, report.Claimed);
        Assert.Equal(0, report.Notified);
        Assert.True(await DispatchHarness.EventuallyAsync(() => command.Contexts.Count == 1, Ct), "the claimed work never ran");
        Assert.Equal(["The notifier failed; the scan's work was already handed out"], harness.Logs.Warnings);
    }
}
