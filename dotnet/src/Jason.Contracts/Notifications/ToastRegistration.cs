namespace Jason.Contracts.Notifications;

/// <summary>
/// What showing a toast on Windows leaves in the account, named once: the runtime's child writes the first key before
/// its first toast, Windows writes the other two at that toast, and <c>jason uninstall</c> removes all three.
/// </summary>
/// <remarks>
/// The per-user key alone is enough for an unpackaged program's toast to appear, as a banner and in the notification
/// center, with no shortcut and no icon: the toast shows <see cref="DisplayName"/>. Everything here is under
/// <c>HKEY_CURRENT_USER</c>, written and removed from an ordinary prompt.
/// </remarks>
public static class ToastRegistration
{
    /// <summary>The AppUserModelID a toast is shown under, and the only one the toast host will register.</summary>
    public const string Aumid = "ai.jason.runtime";

    /// <summary>What a toast says it is from.</summary>
    public const string DisplayName = "Jason";

    /// <summary>The value under <see cref="Key"/> that holds <see cref="DisplayName"/>.</summary>
    public const string DisplayNameValue = "DisplayName";

    /// <summary>The registration itself, written by the toast host before a toast.</summary>
    public const string Key = @"Software\Classes\AppUserModelId\" + Aumid;

    /// <summary>Written by Windows at the first toast: the person's settings for Jason's notifications.</summary>
    public const string WindowsSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + Aumid;

    /// <summary>Written by Windows at the first toast, beside the settings.</summary>
    public const string BackupKey = @"Software\Microsoft\Windows\CurrentVersion\PushNotifications\Backup\" + Aumid;

    /// <summary>The three, in the order an uninstall removes them.</summary>
    public static IReadOnlyList<string> Keys { get; } = [Key, WindowsSettingsKey, BackupKey];
}
