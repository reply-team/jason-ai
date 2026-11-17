using System.Security;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Status;

/// <summary>Whether Windows shows Jason's toast now, as this account's settings say it at this moment.</summary>
public enum ToastSwitch
{
    /// <summary>None of the three switches below is off.</summary>
    NotOff,

    /// <summary>The runtime's own entry in Windows' notification settings is switched off.</summary>
    OffForJason,

    /// <summary>Windows' notifications are switched off for every app.</summary>
    OffForEveryApp,

    /// <summary>A group policy switches the toasts of every app off.</summary>
    OffByPolicy,
}

/// <summary>
/// Windows' notification settings for the runtime's toast as they stand now, read when <c>jason status</c> runs. The
/// runtime reads Windows' answer only when it sends a toast, so what it reports is the setting at its last send; a person
/// who switched Jason off or on since is told the switch as it is now. Only ever read — the ninth seam whose default is
/// the refusal, for the reason <see cref="CliEnvironment.Desktop"/> gives: a test that read the real registry would answer
/// with the settings of whoever runs the suite.
/// </summary>
public interface IToastSetting
{
    /// <summary>The switch as it stands now, or null where it cannot be read.</summary>
    ToastSwitch? Read();
}

/// <summary>
/// This account's settings, read and never written, from the three values Windows' own Settings and a group policy write,
/// in the order Windows weighs them: the policy first, then the switch for every app, then the runtime's own entry.
/// </summary>
public sealed class WindowsToastSetting : IToastSetting
{
    /// <summary>Where the policy "Turn off toast notifications" is written for this account.</summary>
    public const string PolicyKey = @"Software\Policies\Microsoft\Windows\CurrentVersion\PushNotifications";

    /// <summary>The policy's value: 1 switches the toasts of every app off.</summary>
    public const string PolicyValue = "NoToastApplicationNotification";

    /// <summary>Where Settings writes its switch for every app.</summary>
    public const string EveryAppKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";

    /// <summary>The switch for every app: 0 is off.</summary>
    public const string EveryAppValue = "ToastEnabled";

    /// <summary>The runtime's own entry's switch, under <see cref="ToastRegistration.WindowsSettingsKey"/>: 0 is off.</summary>
    public const string EntryValue = "Enabled";

    public ToastSwitch? Read()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            return From(Dword(PolicyKey, PolicyValue), Dword(EveryAppKey, EveryAppValue), Dword(ToastRegistration.WindowsSettingsKey, EntryValue));
        }
        catch (Exception exception) when (exception is SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        static int? Dword(string key, string name)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            using var opened = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, writable: false);
            return opened?.GetValue(name) is int value ? value : null;
        }
    }

    /// <summary>
    /// The switch the three values make, a value absent being null: the policy at 1 is off by policy, else the switch for
    /// every app at 0 is off for every app, else the entry at 0 is off for Jason, else none is off. An absent value is
    /// Windows' default, which switches nothing off.
    /// </summary>
    public static ToastSwitch From(int? policy, int? everyApp, int? entry) =>
        policy == 1 ? ToastSwitch.OffByPolicy
        : everyApp == 0 ? ToastSwitch.OffForEveryApp
        : entry == 0 ? ToastSwitch.OffForJason
        : ToastSwitch.NotOff;
}
