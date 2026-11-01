namespace Jason.Runtime.Notifications;

/// <summary>
/// One way a notification can reach a person: the operating system's own, or a named notification plugin. A channel
/// is bounded by its own budget and answers with an outcome; it never throws, and one that does is a defect the sender
/// records as that channel's failure.
/// </summary>
public interface INotificationChannel
{
    /// <summary>The name a line records: <c>toast</c>, or a plugin's id.</summary>
    string Name { get; }

    /// <summary><c>toast</c> or <c>plugin</c>.</summary>
    string Kind { get; }

    /// <param name="notificationId">The notification's own id, which a plugin's invocation carries as its correlation id.</param>
    Task<ChannelOutcome> DeliverAsync(NotificationMessage message, string notificationId, CancellationToken cancellationToken);
}

/// <summary>The channels the settings in force name, resolved at send time so an edit applies to the next notification.</summary>
public interface INotificationChannels
{
    IReadOnlyList<INotificationChannel> Resolve(NotificationsOptions options);
}
