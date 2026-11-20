using System.Runtime.InteropServices;

namespace Jason.Cli.Installation;

/// <summary>
/// macOS: the account's home as the account database names it, read with <c>getpwuid(getuid())</c>, where <c>HOME</c> may
/// name another folder (a shell under <c>sudo</c>, a <c>HOME</c> reset to <c>/</c>). Read only; null where it cannot be read
/// and on every other system.
/// </summary>
internal static class AccountDatabase
{
    private const string LibSystem = "/usr/lib/libSystem.B.dylib";

    /// <summary>The home directory the account database names for this process's real user, or null.</summary>
    public static string? Home()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            var entry = GetPasswordEntry(GetUserId());
            return entry == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Marshal.PtrToStructure<PasswordEntry>(entry).Directory);
        }
        catch (Exception unread) when (unread is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>This process's real user id on macOS, which names the account's launchd domains; null on every other system.</summary>
    public static uint? UserId()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            return GetUserId();
        }
        catch (Exception unread) when (unread is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>macOS's <c>struct passwd</c>, up to the home directory.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct PasswordEntry
    {
        public IntPtr Name;
        public IntPtr Password;
        public uint UserId;
        public uint GroupId;
        public long Change;
        public IntPtr Class;
        public IntPtr Gecos;
        public IntPtr Directory;
        public IntPtr Shell;
        public long Expire;
    }

    [DllImport(LibSystem, EntryPoint = "getuid")]
    private static extern uint GetUserId();

    [DllImport(LibSystem, EntryPoint = "getpwuid")]
    private static extern IntPtr GetPasswordEntry(uint userId);
}
