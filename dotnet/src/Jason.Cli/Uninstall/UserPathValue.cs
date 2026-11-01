using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Jason.Cli.Uninstall;

/// <summary>This account's <c>Path</c> value as the registry stores it: unexpanded, and of the kind it is.</summary>
/// <param name="Raw">The text, with every <c>%VARIABLE%</c> left as it was written.</param>
/// <param name="Kind"><c>ExpandString</c> for an ordinary account's Path, <c>String</c> for one somebody flattened.</param>
public sealed record StoredPath(string Raw, RegistryValueKind Kind);

/// <summary>
/// This account's <c>Path</c> value on Windows, read and written the way the registry holds it.
/// </summary>
/// <remarks>
/// <para>
/// Never through <see cref="Environment.GetEnvironmentVariable(string, EnvironmentVariableTarget)"/> and its
/// setter. That pair expands every <c>%VARIABLE%</c> on the way in and writes a plain string on the way out,
/// so one install and one uninstall turned an account's <c>REG_EXPAND_SZ</c> Path — <c>%USERPROFILE%\…</c>
/// entries and all, every entry and not only Jason's — into a <c>REG_SZ</c> of fixed paths, and nothing turned
/// it back. It was predicted in writing before a fresh account installed and removed Jason, and that run
/// measured it: the value's kind flipped at the install, flipped back when the SDK's first run rewrote it,
/// and flipped again at the uninstall.
/// </para>
/// <para>
/// The key is a parameter so that a test can point this at a key of its own. Nothing but a test names another:
/// the Path a suite could otherwise reach belongs to whoever ran it.
/// </para>
/// </remarks>
public sealed class UserPathValue(string subKey = UserPathValue.EnvironmentKey)
{
    /// <summary>Where Windows keeps an account's own environment, under <c>HKEY_CURRENT_USER</c>.</summary>
    public const string EnvironmentKey = "Environment";

    private const string Name = "Path";

    private const int HwndBroadcast = 0xffff;
    private const uint WmSettingChange = 0x001a;
    private const uint SmtoAbortIfHung = 0x0002;

    /// <summary>The value as stored and its kind, or null where this account has none.</summary>
    [SupportedOSPlatform("windows")]
    public StoredPath? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKey);
        if (key?.GetValue(Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string raw)
        {
            return null;
        }

        return new StoredPath(raw, key.GetValueKind(Name));
    }

    /// <summary>
    /// Writes the value back with the kind it had, and tells every running program that the environment changed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An empty value is removed rather than written empty: it is what a Path holding only the directory this
    /// took away was before the installer created it.
    /// </para>
    /// <para>
    /// The broadcast is what the setter this replaces did for free. Without it a terminal opened from the Start
    /// menu inherits Explorer's environment as it was at logon, and would go on finding a directory that is no
    /// longer on the Path until the person signed out.
    /// </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    public void Write(string raw, RegistryValueKind kind)
    {
        ArgumentNullException.ThrowIfNull(raw);

        using (var key = Registry.CurrentUser.CreateSubKey(subKey))
        {
            if (raw.Length == 0)
            {
                key.DeleteValue(Name, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(Name, raw, kind);
            }
        }

        // Nothing to do with an answer: a window that does not reply in time is skipped by the flag, and the
        // registry already holds what every program started from now on will read.
        _ = SendMessageTimeout(new IntPtr(HwndBroadcast), WmSettingChange, UIntPtr.Zero, EnvironmentKey, SmtoAbortIfHung, 5000, out _);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}
