namespace Jason.Contracts.Api;

/// <summary>
/// The body of <c>notification.enable</c> and <c>notification.disable</c>: one notification channel, the toast or a
/// notification plugin by its id, turned on or off in the settings file. There is no request for every channel at once:
/// notifications as a whole are switched by a line of the settings file, <c>Notifications:Enabled</c>. The answer is
/// <see cref="NotificationsInfo"/>, the notification section of <c>system.info</c> as it stands once the switch is in force.
/// </summary>
/// <param name="Channel"><c>toast</c>, or the id of a notification plugin named under <c>Notifications:Plugins</c>.</param>
public sealed record NotificationSwitchRequest(string? Channel, ActorRef? Actor, string? Reason);
