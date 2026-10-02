using System.Collections.Concurrent;
using Jason.Contracts.Api;
using Jason.Contracts.Ids;
using Jason.Contracts.Notifications;
using Jason.Runtime.Decisions;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications;
using Jason.Runtime.Persistence;
using Jason.Runtime.Tests.Approvals;
using Jason.Runtime.Tests.Dispatch;

namespace Jason.Runtime.Tests.Notifications;

/// <summary>
/// A channel whose answers the test writes, one per attempt, and which remembers every message it was given. It
/// can be told to wait until it is cancelled, which is what a channel looks like to a runtime that is stopping.
/// </summary>
internal sealed class FakeChannel(string name, string kind, params ChannelOutcome[] answers) : INotificationChannel
{
    private readonly ConcurrentQueue<NotificationMessage> _messages = new();
    private int _calls;

    public string Name => name;

    public string Kind => kind;

    public IReadOnlyList<NotificationMessage> Messages => [.. _messages];

    public int Calls => Volatile.Read(ref _calls);

    public bool WaitUntilCancelled { get; init; }

    public bool Throws { get; init; }

    public static FakeChannel Delivering(string name = "toast", string kind = ChannelKinds.Toast) =>
        new(name, kind, ChannelOutcome.Delivered(name, kind));

    public static FakeChannel Skipped(string reason, string name = "toast", string kind = ChannelKinds.Toast) =>
        new(name, kind, ChannelOutcome.NotAttempted(name, kind, reason));

    public static FakeChannel Failing(string code, Jason.Contracts.Plugins.FailureClass @class, string name = "ntfy") =>
        new(name, ChannelKinds.Plugin, ChannelOutcome.Failed(name, ChannelKinds.Plugin, code, @class));

    public async Task<ChannelOutcome> DeliverAsync(NotificationMessage message, string notificationId, CancellationToken cancellationToken)
    {
        _messages.Enqueue(message);
        var call = Interlocked.Increment(ref _calls);
        if (Throws)
        {
            throw new InvalidOperationException("A channel that throws is a defect in that channel.");
        }

        if (WaitUntilCancelled)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return answers[Math.Min(call, answers.Length) - 1];
    }
}

/// <summary>The channels a test hands the sender, in place of the ones the settings would resolve to.</summary>
internal sealed class FakeChannels(params INotificationChannel[] channels) : INotificationChannels
{
    public IReadOnlyList<INotificationChannel> Resolve(NotificationsOptions options) => channels;
}

/// <summary>Things that wait for a person, written the way the runtime writes them, with the lines that say so.</summary>
internal static class Waiting
{
    /// <summary>Parked approvals, each with the <c>approval_requested</c> line a claim writes beside it.</summary>
    public static async Task ApprovalsAsync(DispatchHarness harness, int count, CancellationToken ct)
    {
        for (var index = 0; index < count; index++)
        {
            await using var db = harness.Open();
            await ParkedWork.WriteAsync(db, ct, hash: $"sha256:subject-{index}");
            await harness.WriteLineAsync(JournalKinds.ApprovalRequested, ct);
        }
    }

    /// <summary>A question raised through the service a role's verb reaches, which writes its own line.</summary>
    public static async Task<DecisionDto> DecisionAsync(DispatchHarness harness, CancellationToken ct)
    {
        await using var db = harness.Open();
        var campaign = WorkItemFactory.NewCampaign(now: harness.Clock.Now.UtcDateTime);
        var item = WorkItemFactory.NewAiRole(campaign, now: harness.Clock.Now.UtcDateTime);
        item.Status = WorkItemStatus.Processing;
        var attempt = new Attempt
        {
            PublicId = PublicId.New("att"),
            WorkItem = item,
            Number = 1,
            Status = AttemptStatus.Running,
            StartedAt = harness.Clock.Now.UtcDateTime,
            LockUntil = harness.Clock.Now.UtcDateTime.AddMinutes(30),
        };

        db.Campaigns.Add(campaign);
        db.WorkItems.Add(item);
        db.Attempts.Add(attempt);
        await db.SaveChangesAsync(ct);

        return await new DecisionService(db, new JournalWriter(harness.Clock), harness.Clock).RaiseAsync(
            new DecisionRaiseRequest(item.PublicId, attempt.PublicId, "do we keep calling this account?", null, null, null),
            ct);
    }

    /// <summary>Everything that waits is answered, the way a person's verbs would leave the rows.</summary>
    public static async Task AnswerEverythingAsync(DispatchHarness harness, CancellationToken ct)
    {
        await using var db = harness.Open();
        foreach (var approval in db.Approvals.Where(a => a.Status == ApprovalStatus.Pending))
        {
            approval.Status = ApprovalStatus.Approved;
            approval.DecidedAt = harness.Clock.Now.UtcDateTime;
            approval.DecidedByType = ActorType.Human;
        }

        foreach (var decision in db.Decisions.Where(d => d.Status == DecisionStatus.Pending))
        {
            decision.Status = DecisionStatus.Answered;
            decision.Answer = "stop";
            decision.AnsweredAt = harness.Clock.Now.UtcDateTime;
            decision.AnsweredByType = ActorType.Human;
        }

        await db.SaveChangesAsync(ct);
    }
}
