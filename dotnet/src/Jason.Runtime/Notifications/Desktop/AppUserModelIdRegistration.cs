using System.Globalization;
using System.Runtime.Versioning;
using System.Security;
using Jason.Contracts.Notifications;
using Microsoft.Win32;

namespace Jason.Runtime.Notifications.Desktop;

/// <summary>The part of the current user's registry the toast's registration reads and writes.</summary>
public interface IUserRegistry
{
    /// <summary>A string value under <c>HKEY_CURRENT_USER</c>, or null where the key or the value is absent.</summary>
    string? ReadString(string key, string name);

    /// <summary>Writes a string value, creating the key if needed.</summary>
    void WriteString(string key, string name, string value);
}

/// <summary>
/// The per-user key a toast is shown under: <c>HKCU\Software\Classes\AppUserModelId\ai.jason.runtime</c> with
/// <c>DisplayName</c> = <c>Jason</c>, which is all an unpackaged program's toast needs to appear. Written only where it
/// is absent or says something else, and never with an icon or an activator: a pointer opens nothing.
/// </summary>
public static class AppUserModelIdRegistration
{
    public const string Present = "present";

    public const string Written = "written";

    public const string Failed = "failed";

    /// <returns>
    /// <c>present</c>, <c>written</c>, or <c>failed</c> with the write's HRESULT: a refused write is an answer, not an
    /// exception.
    /// </returns>
    public static (string State, string? Hr) Ensure(IUserRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        try
        {
            if (string.Equals(registry.ReadString(ToastRegistration.Key, ToastRegistration.DisplayNameValue), ToastRegistration.DisplayName, StringComparison.Ordinal))
            {
                return (Present, null);
            }

            registry.WriteString(ToastRegistration.Key, ToastRegistration.DisplayNameValue, ToastRegistration.DisplayName);
            return (Written, null);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return (Failed, "0x" + ex.HResult.ToString("X8", CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>The current user's registry, for the one production caller: the toast host's own entry.</summary>
[SupportedOSPlatform("windows")]
public sealed class RegistryUserRegistry : IUserRegistry
{
    public string? ReadString(string key, string name)
    {
        using var opened = Registry.CurrentUser.OpenSubKey(key, writable: false);
        return opened?.GetValue(name) as string;
    }

    public void WriteString(string key, string name, string value)
    {
        using var created = Registry.CurrentUser.CreateSubKey(key, writable: true);
        created.SetValue(name, value, RegistryValueKind.String);
    }
}
