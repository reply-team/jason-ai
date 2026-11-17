using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Notifications;
using Jason.Runtime.Configuration;
using Jason.Runtime.Domain;
using Jason.Runtime.Journal;
using Jason.Runtime.Notifications.Desktop;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Notifications;

/// <summary>
/// <c>notification.enable</c> and <c>notification.disable</c>: one notification channel — the toast, or a notification
/// plugin by its id — turned on or off through the runtime rather than by a hand edit of the settings file. Each writes the
/// file through the runtime's one writer of it (<see cref="SettingsWriter"/>), which keeps every byte it does not own, and
/// answers once the channel is as asked in the settings the notifier reads, with the notification section of
/// <c>system.info</c> as it then stands.
/// </summary>
/// <remarks>
/// <para>
/// <b>One channel, never all.</b> Off writes <c>false</c> to <c>Notifications:Toast</c> or to
/// <c>Notifications:Plugins:&lt;id&gt;:Enabled</c>; on takes that <c>false</c> away, since a channel is on unless written off,
/// and takes away a <c>Notifications</c> object it leaves empty. Every notification as a whole is held off by
/// <c>Notifications:Enabled</c>, a line of the settings file this act never writes: while it holds them off a channel is
/// not turned on (<c>notifications_off</c>), and turning one off is still written. What the operating system itself holds
/// off — a person's own switch in its settings — is not Jason's to turn, and is only said.
/// </para>
/// <para>
/// <b>A channel that could deliver.</b> A plugin is a channel only where the settings name it, with the binding that says
/// where it delivers, which this act cannot write; one installed and not named is refused, and so is turning on a named one
/// that a notification would not be tried on — the checks are the ones a notification meets before anything is launched
/// (<see cref="Plugins.PluginNotificationChannel.BeforeLaunch"/>), so the refusal says what is missing. A name that is
/// neither the toast nor a plugin installed here is no channel.
/// </para>
/// <para>
/// <b>In force, or put back — <c>profile.set_default</c>'s order.</b> The section is read live, so nothing is reloaded: the
/// write loads the file and tells every section at once. Where the section in force does not then say what was asked —
/// another of its settings a person broke by hand keeps the runtime on the last Notifications settings that validated — the
/// act is refused as <c>setting_not_in_force</c> and the file is put back. Every act that changes the channel in force is
/// journaled as <c>notification_channel_updated</c>, the line saved inside the writer's act, so a save that fails puts back
/// a file this act wrote.
/// </para>
/// </remarks>
public sealed class NotificationSwitch(
    JasonDbContext db,
    JournalWriter journal,
    ReloadGate gate,
    SettingsWriter writer,
    SettingsFile settingsFile,
    LiveSettings<NotificationsOptions> settings,
    PluginRegistry registry,
    JasonPaths paths,
    NotificationStatus status,
    Presence presence)
{
    /// <summary>The toast's name as a channel: the name <c>system.info</c> gives it.</summary>
    public const string Toast = "toast";

    public Task<NotificationsInfo> EnableAsync(NotificationSwitchRequest request, CancellationToken cancellationToken) =>
        SwitchAsync(request, on: true, cancellationToken);

    public Task<NotificationsInfo> DisableAsync(NotificationSwitchRequest request, CancellationToken cancellationToken) =>
        SwitchAsync(request, on: false, cancellationToken);

    /// <summary>The setting that turns <paramref name="channel"/> on and off, as the configuration system spells its path.</summary>
    public static string Setting(string channel) =>
        channel == Toast
            ? NotificationsOptions.Section + ":" + nameof(NotificationsOptions.Toast)
            : $"{NotificationsOptions.Section}:{nameof(NotificationsOptions.Plugins)}:{channel}:{nameof(NotificationPluginEntry.Enabled)}";

    private async Task<NotificationsInfo> SwitchAsync(NotificationSwitchRequest request, bool on, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var actor = Actors.Resolve(request.Actor);
        var channel = request.Channel?.Trim();

        var errors = new ValidationErrors();
        if (string.IsNullOrEmpty(channel))
        {
            errors.Add("channel", "required", "channel is required: toast, or the id of a notification plugin named in the settings.");
        }
        else if (channel != Toast && !ManifestRules.IsPluginId(channel))
        {
            errors.Add("channel", "invalid", "channel is toast, or a plugin's id: a lowercase letter, then lowercase letters, digits or dashes.");
        }

        if (request.Reason is not null && request.Reason.Trim().Length > PluginService.MaxReasonLength)
        {
            errors.Add("reason", "too_long", string.Create(CultureInfo.InvariantCulture, $"reason must be at most {PluginService.MaxReasonLength} characters."));
        }

        errors.ThrowIfAny();
        await ActorVerification.VerifyAsync(db, actor, cancellationToken).ConfigureAwait(false);
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
        var setting = Setting(channel!);

        await gate.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var options = settings.Current;
            Check(channel!, on, options);
            var before = InForce(options, channel!);
            return await writer.WriteAsync(
                file => Edits(file, setting, on),
                [setting],
                NotificationsOptions.Section,
                async (wrote, token) =>
                {
                    if (!wrote && before == on)
                    {
                        return Report();
                    }

                    // A write has just loaded the file under the settings lock; a file left as it was is read now, as
                    // profile.set_default reads it, since its watch may not yet have told the runtime of a hand edit.
                    if (!wrote)
                    {
                        settingsFile.Read();
                    }

                    var now = InForce(settings.Current, channel!);
                    if (now != on)
                    {
                        throw DomainErrors.SettingNotInForce(setting, NotificationsOptions.Section);
                    }

                    if (before != now)
                    {
                        journal.Append(
                            db,
                            actor,
                            JournalKinds.NotificationChannelUpdated,
                            campaign: null,
                            key: setting,
                            old: before is { } was ? JsonValue.Create(was) : null,
                            updated: JsonValue.Create(on),
                            reason: reason);
                        await db.SaveChangesAsync(token).ConfigureAwait(false);
                    }

                    return Report();
                },
                (refused, putBack, _) => PutBackSaid(refused, putBack),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Semaphore.Release();
        }
    }

    /// <summary>
    /// What refuses before anything is written: a name that is no channel, an installed plugin nothing names, every
    /// notification held off as a whole, and a named channel that a notification would not be tried on.
    /// </summary>
    private void Check(string channel, bool on, NotificationsOptions options)
    {
        if (channel != Toast && !options.Plugins.ContainsKey(channel))
        {
            if (registry.Snapshot.Find(channel) is null && !Directory.Exists(paths.PluginPackageDirectory(channel)))
            {
                throw DomainErrors.NotificationChannelNotFound(channel);
            }

            // What the plugin is, read by the same check a named channel's readiness is, with nothing named: a plugin
            // that could be a channel reads ready or binding_invalid, and is told to be named; any other is told why not.
            var (what, said) = NotificationStatus.Readiness(channel, new NotificationPluginEntry(), registry.Snapshot);
            throw DomainErrors.NotificationChannelNotNamed(
                channel,
                what is NotAttemptedReasons.Unavailable or NotAttemptedReasons.NotDeclared ? what : "not_named",
                said);
        }

        if (!on)
        {
            return;
        }

        if (!options.Enabled)
        {
            throw DomainErrors.NotificationsOff();
        }

        if (channel != Toast && NotificationStatus.Readiness(channel, options.Plugins[channel], registry.Snapshot) is var (word, detail) && word != ChannelReadiness.Ready)
        {
            throw DomainErrors.NotificationChannelNotConfigured(channel, word, detail);
        }
    }

    /// <summary>Whether the settings in force have the channel on, apart from the switch for every notification; null for a plugin no longer named.</summary>
    private static bool? InForce(NotificationsOptions options, string channel) =>
        channel == Toast ? options.Toast : options.Plugins.TryGetValue(channel, out var entry) ? entry.Enabled : null;

    /// <summary>
    /// Off writes <c>false</c>. On takes away a <c>false</c> the file holds — a value written <c>true</c> is left as the person
    /// wrote it — and for the toast a <c>Notifications</c> object that leaves empty; a plugin's own object stays, since it
    /// is what names the plugin a channel.
    /// </summary>
    private static IReadOnlyList<SettingsEdit> Edits(IReadOnlyDictionary<string, string?> file, string setting, bool on)
    {
        if (!on)
        {
            return [SettingsEdit.Set(setting, JsonValue.Create(false))];
        }

        if (!file.TryGetValue(setting, out var written) || !bool.TryParse(written, out var value) || value)
        {
            return [];
        }

        return setting == Setting(Toast)
            ? [SettingsEdit.Remove(setting), SettingsEdit.RemoveIfEmpty(NotificationsOptions.Section)]
            : [SettingsEdit.Remove(setting)];
    }

    private NotificationsInfo Report() => status.Info(settings.Current, presence, registry.Snapshot);

    /// <summary>A refusal after the write, with what became of the file said in one more detail under the settings file's own field.</summary>
    private static DomainException PutBackSaid(DomainException refused, SettingsPutBack putBack)
    {
        var detail = putBack switch
        {
            SettingsPutBack.PutBack => "The settings file was put back as it was; the channel in force is unchanged.",
            SettingsPutBack.ChangedSince => "The settings file was changed by another writer after this one wrote it, so it was left as that writer left it; the channel in force is unchanged.",
            SettingsPutBack.Held => "Another program held the settings file open, so it was not put back: it holds the switch this wrote, which is not in force. Mend the Notifications section, or put the switch back by hand.",
            _ => "The settings file could not be written again, so it was not put back: it holds the switch this wrote, which is not in force. Mend the Notifications section, or put the switch back by hand.",
        };
        return new DomainException(
            refused.StatusCode,
            refused.Code,
            refused.Message,
            refused.Retryable,
            [.. refused.Details ?? [], new ErrorDetail(PluginLoader.SettingsFileField, SettingsWriter.Code(putBack), detail)]);
    }
}
