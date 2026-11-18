using Jason.Cli.Uninstall;
using Microsoft.Win32;

namespace Jason.Cli.Installation;

/// <summary>The machine's own environment on Windows, as the registry stores it: read, never written.</summary>
internal static class MachineEnvironment
{
    /// <summary>Where Windows keeps the machine's environment, under <c>HKEY_LOCAL_MACHINE</c>.</summary>
    public const string Key = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";

    /// <summary>The machine's <c>Path</c>, unexpanded, and its kind; null where it cannot be read, and off Windows.</summary>
    public static StoredPath? ReadPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Key);
            return key?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) is string raw
                ? new StoredPath(raw, key.GetValueKind("Path"))
                : null;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
