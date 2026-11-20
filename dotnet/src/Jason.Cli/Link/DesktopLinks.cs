using System.Security;
using Jason.Cli.Process;
using Jason.Contracts.Notifications;

namespace Jason.Cli.Link;

/// <summary>
/// Where Jason Desktop registered its own link scheme for this account: the program the registration starts for a
/// <c>jason-desktop:</c> address, by the path it names, or null where there is none in the desktop's own form. Only ever
/// read — the eighth seam whose default is the refusal, for the reason <see cref="CliEnvironment.Opener"/> gives: a test
/// that read the real registration would answer with whatever the person running the suite has installed.
/// </summary>
public interface IDesktopRegistration
{
    /// <summary>The registered program, or null where the desktop registered none in its own form or it cannot be read.</summary>
    string? Program();
}

/// <summary>
/// This account's own registration on Windows, read and never written:
/// <c>HKEY_CURRENT_USER\Software\Classes\jason-desktop\shell\open\command</c>, which the desktop writes at each start of
/// its packaged build (<c>desktop/src/deep-link.ts</c>). A machine-wide class is not read.
/// </summary>
public sealed class WindowsDesktopRegistration : IDesktopRegistration
{
    /// <summary>The command key under this account's classes.</summary>
    public const string CommandKey = @"Software\Classes\" + DesktopLinks.Scheme + @"\shell\open\command";

    public string? Program() => DesktopLinks.WindowsProgram(Command());

    private static string? Command()
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
/// On Linux, the menu entry that declares the scheme, <c>MimeType=x-scheme-handler/jason-desktop;</c>, in the deb's form:
/// <c>Exec=&lt;program&gt; -- %U</c>, the program by a bare full path. That is the form xdg-open starts in every mode: outside
/// GNOME and KDE it takes the first word of <c>Exec</c>, quotes and all, as the program, and when that is no program it hands
/// the address to a list of browsers. An AppImage's entry declares no scheme, so it takes no link.
/// </summary>
/// <param name="applicationFolders">The <c>applications</c> folders searched, in order.</param>
public sealed class LinuxDesktopRegistration(IReadOnlyList<string> applicationFolders) : IDesktopRegistration
{
    private const string Declared = "x-scheme-handler/" + DesktopLinks.Scheme;

    private const string Tail = " -- %U";

    /// <summary>
    /// This account's folders: <c>applications</c> under <c>XDG_DATA_HOME</c> (<c>~/.local/share</c>), then under each of
    /// <c>XDG_DATA_DIRS</c> (<c>/usr/local/share:/usr/share</c>), a relative entry left out as the specification says.
    /// </summary>
    public static LinuxDesktopRegistration ForThisAccount()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { } set && Path.IsPathFullyQualified(set) ? set : Path.Combine(home, ".local", "share");
        var dataDirs = (Environment.GetEnvironmentVariable("XDG_DATA_DIRS") is { Length: > 0 } dirs ? dirs : "/usr/local/share:/usr/share")
            .Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Where(Path.IsPathFullyQualified);
        return new([.. new[] { dataHome }.Concat(dataDirs).Select(folder => Path.Combine(folder, "applications"))]);
    }

    public string? Program()
    {
        foreach (var folder in applicationFolders)
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder, "*.desktop").Order(StringComparer.Ordinal)] : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                if (ProgramOf(entry) is { } program)
                {
                    return program;
                }
            }
        }

        return null;
    }

    /// <summary>The program an entry starts for the scheme, read from its <c>[Desktop Entry]</c> group alone, or null.</summary>
    private static string? ProgramOf(string entry)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(entry);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        string? exec = null;
        var declares = false;
        var inEntry = false;
        foreach (var line in lines)
        {
            if (line.StartsWith('['))
            {
                inEntry = line == "[Desktop Entry]";
                continue;
            }

            if (!inEntry)
            {
                continue;
            }

            if (line.StartsWith("Exec=", StringComparison.Ordinal))
            {
                exec = line["Exec=".Length..];
            }
            else if (line.StartsWith("MimeType=", StringComparison.Ordinal))
            {
                declares = line["MimeType=".Length..].Split(';').Contains(Declared, StringComparer.Ordinal);
            }
        }

        if (!declares || exec is null || !exec.EndsWith(Tail, StringComparison.Ordinal))
        {
            return null;
        }

        var program = exec[..^Tail.Length];
        return program.Length > 0 && !program.Contains(' ', StringComparison.Ordinal) && !program.Contains('"', StringComparison.Ordinal) ? program : null;
    }
}

/// <summary>
/// On macOS the bundle declares the scheme (<c>CFBundleURLTypes</c>), so the desktop takes links where its application
/// is: <c>Jason.app</c> in one of the folders its install uses, the account's own Applications first.
/// </summary>
/// <param name="applicationsFolders">The folders searched, in order.</param>
public sealed class MacDesktopRegistration(IReadOnlyList<string> applicationsFolders) : IDesktopRegistration
{
    /// <summary>This account's <c>~/Applications</c>, then <c>/Applications</c>.</summary>
    public static MacDesktopRegistration ForThisAccount() =>
        new([Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications"), "/Applications"]);

    public string? Program() =>
        applicationsFolders
            .Select(folder => Path.Combine(folder, "Jason.app", "Contents", "MacOS", "Jason"))
            .FirstOrDefault(File.Exists);
}

/// <summary>Why Jason Desktop was not opened: a code of the CLI's and its one sentence.</summary>
public sealed record DesktopRefusal(string Code, string Message);

/// <summary>The registration readers this build knows how to make.</summary>
public static class DesktopRegistrations
{
    /// <summary>This account's, on Windows, Linux and macOS; null anywhere else, which is no desktop.</summary>
    public static IDesktopRegistration? ForThisMachine() =>
        OperatingSystem.IsWindows() ? new WindowsDesktopRegistration()
        : OperatingSystem.IsLinux() ? LinuxDesktopRegistration.ForThisAccount()
        : OperatingSystem.IsMacOS() ? MacDesktopRegistration.ForThisAccount()
        : null;
}

/// <summary>
/// Every place Jason sends a person opens Jason Desktop at that place, by its own scheme, and never a browser:
/// <c>jason ui open</c> and a toast's click both come here. The CLI starts nothing by a path it read: the registration only
/// says whether the desktop is there, and its address goes to the system's opener, which resolves the program. Where no
/// desktop takes links, or the opener does not hand the address on, nothing else is opened, and the words say why.
/// </summary>
/// <remarks>
/// The address is made from a parsed, checked target, by the table that writes a toast's links — the list, or one item by
/// its checked id — and never from text that arrived. The desktop's own table takes the same two forms
/// (<c>desktop/src/deep-link.ts</c>) and opens Needs you on them.
/// </remarks>
public static class DesktopLinks
{
    /// <summary>The desktop's own scheme, the same on every system.</summary>
    public const string Scheme = "jason-desktop";

    /// <summary>The sentence a refusal for no desktop says, on stderr, where a person or a click's log reads it.</summary>
    public const string NotInstalled = "Nothing was opened: no Jason Desktop that takes its links is installed for this account.";

    /// <summary>How long the opener is given to hand the address on before the hand-on is said as not finished.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <summary>The tail of the command the desktop registers on Windows after its program's path: <c>--</c> and the address.</summary>
    private const string WindowsTail = " -- \"%1\"";

    /// <summary>
    /// The desktop's address for a target that opens a page — the list or one item — or null for any other act, which
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
    /// Whether the desktop takes links for this account: a registration in its own form naming, by a full path, a program
    /// that is there. Anything else — no registration, another form, a relative path, a file that is gone — is no desktop.
    /// </summary>
    public static bool Present(IDesktopRegistration? registration) =>
        registration?.Program() is { Length: > 0 } program && Path.IsPathFullyQualified(program) && File.Exists(program);

    /// <summary>
    /// The program a Windows registration's command names in the desktop's form — the path, quoted where it needs it, then
    /// <c>--</c> and the quoted address — or null for any other form.
    /// </summary>
    public static string? WindowsProgram(string? command)
    {
        if (command is not { Length: > 0 })
        {
            return null;
        }

        string program;
        string rest;
        if (command[0] == '"')
        {
            var close = command.IndexOf('"', 1);
            if (close < 0)
            {
                return null;
            }

            program = command[1..close];
            rest = command[(close + 1)..];
        }
        else
        {
            var space = command.IndexOf(' ', StringComparison.Ordinal);
            if (space < 0)
            {
                return null;
            }

            program = command[..space];
            rest = command[space..];
        }

        return string.Equals(rest, WindowsTail, StringComparison.Ordinal) && program.Length > 0 ? program : null;
    }

    /// <summary>
    /// Opens Jason Desktop at <paramref name="address"/>, or with <paramref name="handOn"/> false only checks that it would:
    /// refused with <see cref="CliErrors.DesktopNotInstalled"/> where no desktop takes links; else handed to the system's
    /// opener, and refused by the opener's code where it did not start or failed. Nothing else is ever opened. A hand-on
    /// still running when the wait ends may yet open the desktop, so it is said on stderr and not refused, as a hand-on that
    /// completed is. The caller says a refusal in its own place.
    /// </summary>
    public static DesktopRefusal? Open(CliEnvironment env, string address, bool handOn)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentException.ThrowIfNullOrEmpty(address);
        if (!Present(env.Desktop))
        {
            return new(CliErrors.DesktopNotInstalled, NotInstalled);
        }

        if (!handOn)
        {
            return null;
        }

        var handed = env.Opener is { } opener ? opener.HandOn(address, Wait) : UrlHandOff.NotStarted;
        switch (handed)
        {
            case UrlHandOff.Handed:
                env.Error.WriteLine("Opened in Jason Desktop.");
                return null;
            case UrlHandOff.TimedOut:
                env.Error.WriteLine($"Handed to Jason Desktop; the system's opener had not finished within {Wait.TotalSeconds:0} seconds ({Code(handed)}).");
                return null;
            default:
                return new(Code(handed), $"Jason Desktop was not opened: the system's opener did not hand it the address ({Code(handed)}). Nothing else was opened.");
        }
    }

    /// <summary>The code a hand-on that did not complete is said by.</summary>
    public static string Code(UrlHandOff handOff) => handOff switch
    {
        UrlHandOff.NotStarted => "opener_not_started",
        UrlHandOff.TimedOut => "opener_timed_out",
        _ => "opener_failed",
    };
}
