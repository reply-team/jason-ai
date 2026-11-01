using Jason.Contracts.Operations;
using Jason.Runtime.Notifications.Desktop;
using Jason.Runtime.Notifications.Plugins;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Notifications;

/// <summary>
/// The channels the settings in force name: the toast whenever notifications are on — answering "off" when it is
/// turned off, so the line says so — and each enabled notification plugin. Resolved at send time, so an edit of the
/// settings applies to the next notification. A plugin not named in the settings is not a channel.
/// </summary>
/// <param name="timing">How a plugin channel matches its schemas' patterns: the runtime's one default, which a test replaces.</param>
public sealed class NotificationChannels(ToastChannel toast, PluginRegistry registry, IServiceScopeFactory scopes, PatternTiming? timing = null) : INotificationChannels
{
    public IReadOnlyList<INotificationChannel> Resolve(NotificationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return
        [
            options.Toast ? toast : ToastChannel.Off,
            .. options.Plugins
                .Where(plugin => plugin.Value.Enabled)
                .OrderBy(plugin => plugin.Key, StringComparer.Ordinal)
                .Select(plugin => new PluginNotificationChannel(plugin.Key, plugin.Value, registry, scopes, timing)),
        ];
    }
}
