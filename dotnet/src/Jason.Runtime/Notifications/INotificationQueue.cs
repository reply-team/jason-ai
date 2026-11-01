namespace Jason.Runtime.Notifications;

/// <summary>
/// Where the notifier hands a notification it has decided to send. Handing over is all a scan does: the sending
/// happens off the scan, and whoever sends releases the notifier's in-flight mark when it is done.
/// </summary>
public interface INotificationQueue
{
    void Enqueue(NotificationRequest request);
}

/// <summary>
/// One notification to send: its id, the journal line it covers through (a public <c>jrn_</c> id), the last time a
/// notification reached anybody, the burst it is about, and the line the last notification that reached anybody
/// covered through — where its count of failed protective work starts. It carries no counts: those are read at send
/// time.
/// </summary>
public sealed record NotificationRequest(string NotificationId, string? CoveredThrough, DateTime? LastDeliveredAt, Burst Burst, string? DeliveredThrough = null);
