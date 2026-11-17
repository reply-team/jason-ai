using System.Runtime.InteropServices;

namespace Jason.Cli.Installation;

/// <summary>
/// Whether this process runs above what its account ordinarily has, in the sense a carried update refuses
/// (<c>update_elevated</c>): on Windows a token elevated above the account's ordinary one, or an account other than the one
/// whose session it runs in; on macOS root.
/// </summary>
/// <remarks>
/// An administrator whose Windows has no split token - User Account Control off, as on a hosted build machine - runs every
/// program with the one token it has: that is the account's ordinary token, and it is not refused. With User Account Control
/// on, an administrator's elevated terminal holds the full half of a split token, and is. A terminal started as another
/// account - over the shoulder, or with runas - is refused whatever its token, where the session's account can be read; the
/// session a service runs in has none, and nothing is refused there for it.
/// </remarks>
public static class Elevation
{
    /// <summary>What Windows says of a process's token (<c>TOKEN_ELEVATION_TYPE</c>).</summary>
    public enum TokenKind
    {
        /// <summary>No split token: the account's only one, elevated or not.</summary>
        Default = 1,

        /// <summary>The full half of a split token: elevated above the account's ordinary one.</summary>
        Full = 2,

        /// <summary>The limited half of a split token: the account's ordinary one.</summary>
        Limited = 3,
    }

    /// <summary>This process's, read from the system.</summary>
    public static bool OfThisProcess()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Environment.IsPrivilegedProcess;
        }

        var (sessionUser, sessionDomain) = SessionAccount();
        return Decide(TokenKindOfThisProcess(), Environment.UserName, Environment.UserDomainName, sessionUser, sessionDomain, Environment.IsPrivilegedProcess);
    }

    /// <summary>
    /// The rule on Windows: a full token, or another account than the session's where that is known. Where the token's kind
    /// cannot be read, whether the process is privileged at all decides.
    /// </summary>
    public static bool Decide(TokenKind? kind, string? processUser, string? processDomain, string? sessionUser, string? sessionDomain, bool privileged)
    {
        if (kind is null)
        {
            return privileged;
        }

        if (kind == TokenKind.Full)
        {
            return true;
        }

        return sessionUser is { Length: > 0 }
            && processUser is { Length: > 0 }
            && !(string.Equals(processUser, sessionUser, StringComparison.OrdinalIgnoreCase)
                && (sessionDomain is not { Length: > 0 } || processDomain is not { Length: > 0 } || string.Equals(processDomain, sessionDomain, StringComparison.OrdinalIgnoreCase)));
    }

    private const uint TokenQuery = 0x0008;
    private const int TokenElevationTypeClass = 18;
    private const int CurrentSession = -1;
    private const int WtsUserName = 5;
    private const int WtsDomainName = 7;

    private static TokenKind? TokenKindOfThisProcess()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var token))
        {
            return null;
        }

        try
        {
            return GetTokenInformation(token, TokenElevationTypeClass, out var kind, sizeof(int), out _) && Enum.IsDefined((TokenKind)kind)
                ? (TokenKind)kind
                : null;
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static (string? User, string? Domain) SessionAccount() => (SessionString(WtsUserName), SessionString(WtsDomainName));

    private static string? SessionString(int what)
    {
        try
        {
            if (!WTSQuerySessionInformation(IntPtr.Zero, CurrentSession, what, out var buffer, out _))
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                WTSFreeMemory(buffer);
            }
        }
        catch (Exception unavailable) when (unavailable is DllNotFoundException or EntryPointNotFoundException)
        {
            // A Windows without the session service's library: no session account to compare with.
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, out int information, int length, out int returned);

    [DllImport("wtsapi32.dll", SetLastError = true, EntryPoint = "WTSQuerySessionInformationW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int what, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
