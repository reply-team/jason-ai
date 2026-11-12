using System.Security;
using Jason.Cli.Process;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Link;

/// <summary>
/// Where the Jason desktop registered its own link scheme for this account: the command Windows would run for a
/// <c>jason-desktop:</c> address, as text, or null where there is none. Only ever read — the eighth seam whose default
/// is the refusal, for the reason <see cref="CliEnvironment.Opener"/> gives: a test that read the real registry would
/// answer with whatever the person running the suite has installed.
/// </summary>
public interface IDesktopRegistration
{
    /// <summary>The registered command, or null where the desktop registered none or it cannot be read.</summary>
    string? Command();
}

/// <summary>
/// This account's own registration, read and never written:
/// <c>HKEY_CURRENT_USER\Software\Classes\jason-desktop\shell\open\command</c>, which the desktop writes at each start of
/// its packaged build on Windows (<c>desktop/src/deep-link.ts</c>). A machine-wide class is not read.
/// </summary>
public sealed class WindowsDesktopRegistration : IDesktopRegistration
{
    /// <summary>The command key under this account's classes.</summary>
    public const string CommandKey = @"Software\Classes\" + DesktopLinks.Scheme + @"\shell\open\command";

    public string? Command()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(CommandKey, writable: false);
            return key?.GetValue(null) as string;
        }
        catch (Exception exception) when (exception is SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>
/// A toast's click that opens a page opens the desktop where it is installed, and the browser UI otherwise. The CLI
/// starts nothing by a path it read: the registration only says whether the desktop is there, and the desktop's own
/// address goes to the same opener the browser UI's address goes to, so the system resolves the program.
/// </summary>
/// <remarks>
/// The address is made from the parsed link, by the table that writes a toast's links — the list, or one item by its
/// checked id — and never from the text that arrived. The desktop's own table takes the same two forms
/// (<c>desktop/src/deep-link.ts</c>) and opens Needs you on them.
/// </remarks>
public static class DesktopLinks
{
    /// <summary>The desktop's own scheme, the same on every system.</summary>
    public const string Scheme = "jason-desktop";

    /// <summary>How long the opener is given to hand the address on before the browser UI opens instead.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>The tail of the command the desktop registers after its program's path: <c>--</c> and the address.</summary>
    private const string Tail = " -- \"%1\"";

    /// <summary>
    /// The desktop's address for a link that opens a page — the list or one item — or null for any other act, which
    /// opens no page.
    /// </summary>
    public static string? Address(AttentionTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var link = target.Act switch
        {
            AttentionAct.List => AttentionLinks.List,
            AttentionAct.Open => AttentionLinks.Item(target.Id),
            _ => null,
        };
        return link is null ? null : Scheme + link[AttentionLinks.Scheme.Length..];
    }

    /// <summary>
    /// Whether the desktop is installed for this account: a registration in the form the desktop writes, naming by a
    /// full path a file that is there. Anything else — no registration, another form, a relative path, a file that is
    /// gone — is no desktop.
    /// </summary>
    public static bool Present(IDesktopRegistration? registration)
    {
        if (registration?.Command() is not { Length: > 0 } command)
        {
            return false;
        }

        string program;
        string rest;
        if (command[0] == '"')
        {
            var close = command.IndexOf('"', 1);
            if (close < 0)
            {
                return false;
            }

            program = command[1..close];
            rest = command[(close + 1)..];
        }
        else
        {
            var space = command.IndexOf(' ', StringComparison.Ordinal);
            if (space < 0)
            {
                return false;
            }

            program = command[..space];
            rest = command[space..];
        }

        return string.Equals(rest, Tail, StringComparison.Ordinal)
            && program.Length > 0
            && Path.IsPathFullyQualified(program)
            && File.Exists(program);
    }

    /// <summary>The code a failed hand-on is said by.</summary>
    public static string Code(UrlHandOff handOff) => handOff switch
    {
        UrlHandOff.NotStarted => "opener_not_started",
        UrlHandOff.TimedOut => "opener_timed_out",
        _ => "opener_failed",
    };
}
