using System.Text.Json.Nodes;
using Jason.Runtime.Configuration;
using Microsoft.Extensions.Configuration;

namespace Jason.Runtime.Notifications;

/// <summary>
/// The <c>Notifications</c> section: whether a runtime says anything when work starts waiting for a person, how
/// long a burst is given to settle, how often a person may be told, and on which channels. Read through
/// <c>IOptionsMonitor</c> at every scan, so an edit applies at the next one.
/// </summary>
/// <remarks>
/// The scalars are bound. <see cref="Plugins"/> is filled in two halves for the reason routes are read rather
/// than bound: the binder makes each entry and its <see cref="NotificationPluginEntry.Enabled"/>, and cannot make
/// a <see cref="JsonNode"/>, so each entry's binding is read afterwards by <see cref="FillBindings"/>.
/// </remarks>
public sealed class NotificationsOptions
{
    public const string Section = "Notifications";

    /// <summary>The ranges the validator holds the three numbers to; the page prints them from here.</summary>
    public const int QuietSecondsMin = 1;

    public const int QuietSecondsMax = 3600;

    public const int MaxWaitSecondsMax = 86400;

    public const int MinIntervalMinutesMin = 0;

    public const int MinIntervalMinutesMax = 1440;

    /// <summary>Off means the notifier reads nothing and writes nothing, whatever the channels say.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The operating system's own notification, shown by a runtime that has a desktop to show it on.</summary>
    public bool Toast { get; set; } = true;

    /// <summary>How long the newest line of a burst must have been quiet before the burst is said.</summary>
    public int QuietSeconds { get; set; } = 60;

    /// <summary>How long the oldest line of a burst that never goes quiet may wait before it is said anyway.</summary>
    public int MaxWaitSeconds { get; set; } = 600;

    /// <summary>The least time between two notifications that reached anybody. Zero is no floor.</summary>
    public int MinIntervalMinutes { get; set; } = 30;

    /// <summary>The notification plugins an operator named, by plugin id. A plugin not named here is not a channel.</summary>
    public Dictionary<string, NotificationPluginEntry> Plugins { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Whether any channel is on: when none is, nothing is read, nothing is sent and nothing is written.</summary>
    public bool AnyChannel => Enabled && (Toast || Plugins.Values.Any(plugin => plugin.Enabled));

    /// <summary>Each named plugin's binding, read from the section as JSON, the way a route's binding is read.</summary>
    internal static void FillBindings(IConfiguration section, NotificationsOptions options)
    {
        var plugins = section.GetSection(nameof(Plugins));
        foreach (var (id, entry) in options.Plugins)
        {
            entry.Binding = RoutesOptions.Json(plugins.GetSection(id).GetSection(nameof(NotificationPluginEntry.Binding)));
        }
    }
}

/// <summary>One notification plugin an operator named: whether it is on, and the account it delivers through.</summary>
public sealed class NotificationPluginEntry
{
    /// <summary>On unless written <c>false</c>: naming a plugin is turning it on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// What the plugin is told about where to deliver, checked against the plugin's own schema before anything is
    /// launched. Never a credential: a secret is the plugin's to read from its environment.
    /// </summary>
    public JsonNode? Binding { get; internal set; }
}
