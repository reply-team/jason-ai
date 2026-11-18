using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Notifications;
using Jason.Runtime.Notifications.Desktop;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// Telling a person that work is waiting for them. What it did is a journal line and a section of <c>system.info</c>, and
/// what governs it is the <c>Notifications</c> section of the settings; its two operations turn one channel of that section
/// on or off through the runtime's one writer of the settings file.
/// </summary>
public static class NotificationsModule
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The same guarded seam the dispatcher reads its section through: the last value that validated stays in
        // force, and the complaint about an edit that did not is said once.
        services.AddSingleton(DispatcherModule.Seam<NotificationsOptions>(NotificationsOptions.Section));

        // What the notifier knows between scans lives as long as the process; the scan's step itself is scoped,
        // like every other step, and registered with them.
        services.AddSingleton<NotifierState>();
        services.AddSingleton<NotificationStatus>();
        services.AddSingleton<INotificationChannels, NotificationChannels>();

        // Whether this runtime has a desktop is decided from the facts of the process it runs in: its session and its
        // environment, which do not change while it runs.
        services.AddSingleton(_ => DesktopFacts.Current());
        services.AddSingleton(provider => DesktopPresence.Find(provider.GetRequiredService<DesktopFacts>()));
        services.AddSingleton<IToastLauncher, ProcessToastLauncher>();
        services.AddSingleton<ToastChannel>();

        // One sender, which is both the queue the notifier hands over to and the service that sends off the scan.
        services.AddSingleton<NotificationSender>();
        services.AddSingleton<INotificationQueue>(provider => provider.GetRequiredService<NotificationSender>());
        services.AddScoped<NotificationSwitch>();
        return services.AddHostedService(provider => provider.GetRequiredService<NotificationSender>());
    }

    public static void MapNotificationOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<NotificationSwitch, NotificationSwitchRequest, NotificationsInfo>(
            Operations.NotificationEnable, (service, request, cancellationToken) => service.EnableAsync(request, cancellationToken));
        app.MapOperation<NotificationSwitch, NotificationSwitchRequest, NotificationsInfo>(
            Operations.NotificationDisable, (service, request, cancellationToken) => service.DisableAsync(request, cancellationToken));
    }
}
