using System.Security;
using Jason.Cli.Process;
using Jason.Contracts.Notifications;
using Jason.Contracts.Programs;

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

    /// <summary>
    /// Where Jason Desktop is installed and the system would still hand a <c>jason-desktop:</c> address to something else:
    /// why, in words a person can act on. Null where it would not, and where no desktop is installed at all.
    /// </summary>
    DesktopRefusal? Elsewhere() => null;
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

/// <summary>The XDG places xdg-utils reads an account's links by, each list in its order of precedence.</summary>
/// <param name="CurrentDesktop">The names in <c>XDG_CURRENT_DESKTOP</c>, each giving a <c>&lt;name&gt;-mimeapps.list</c> read first.</param>
/// <param name="MenuPrefix"><c>XDG_MENU_PREFIX</c>, giving a <c>&lt;prefix&gt;defaults.list</c> and cache read first; empty for none.</param>
public sealed record XdgPlaces(string ConfigHome, IReadOnlyList<string> ConfigDirs, string DataHome, IReadOnlyList<string> DataDirs, IReadOnlyList<string> CurrentDesktop, string MenuPrefix)
{
    /// <summary>This account's, from its environment, with the specification's defaults; a relative entry is left out as it says.</summary>
    public static XdgPlaces ForThisAccount()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static string? Set(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
        static IReadOnlyList<string> Dirs(string? value, string fallback) =>
            [.. (value ?? fallback).Split(':', StringSplitOptions.RemoveEmptyEntries).Where(Path.IsPathFullyQualified)];
        static string Home(string? value, string fallback) => value is not null && Path.IsPathFullyQualified(value) ? value : fallback;
        return new(
            Home(Set("XDG_CONFIG_HOME"), Path.Combine(home, ".config")),
            Dirs(Set("XDG_CONFIG_DIRS"), "/etc/xdg"),
            Home(Set("XDG_DATA_HOME"), Path.Combine(home, ".local", "share")),
            Dirs(Set("XDG_DATA_DIRS"), "/usr/local/share:/usr/share"),
            [.. (Set("XDG_CURRENT_DESKTOP") ?? string.Empty).Split(':', StringSplitOptions.RemoveEmptyEntries)],
            Set("XDG_MENU_PREFIX") ?? string.Empty);
    }

    /// <summary>The data home, then the data dirs.</summary>
    public IEnumerable<string> Data => new[] { DataHome }.Concat(DataDirs);
}

/// <summary>
/// On Linux, Jason Desktop takes its links where xdg-open would run it for a <c>jason-desktop:</c> address in its generic
/// mode, the mode outside GNOME and KDE and the strictest: the entry <c>xdg-mime query default
/// x-scheme-handler/jason-desktop</c> names, found and started as xdg-open finds and starts it, and that entry in the
/// desktop's own form, <c>MimeType</c> declaring the scheme and <c>Exec=&lt;program&gt; -- %U</c> with the program by a
/// bare full path that is there. Read as xdg-utils 1.2.1 reads it (<c>defapp_generic</c>, <c>check_mimeapps_list</c>,
/// <c>defapp_fallback</c>, <c>open_generic_xdg_mime</c>, <c>search_desktop_file</c>), and never written.
/// </summary>
/// <remarks>
/// xdg-mime's default is (1) the first <c>[Default Applications]</c> entry for the type in a <c>mimeapps.list</c>, each
/// <c>&lt;desktop&gt;-mimeapps.list</c> before it, under the config home, the config dirs, then <c>applications/</c> of the
/// data home and dirs, taken only where its program is there; else (2) the first line for the type in a
/// <c>defaults.list</c> or <c>mimeinfo.cache</c> of the data home and dirs, taken unchecked; else (3) the entry declaring the
/// type with the highest <c>InitialPreference</c>. xdg-open runs the first entry of that name whose program it finds, and
/// where it runs none it hands the address to a browser. So a scan of the entries alone is wrong where the account's own
/// default names another program, or a stale cache line names an entry that is gone: both are refused in their own words
/// (<see cref="Elsewhere"/>). xdg-open takes the first word of <c>Exec</c> as the program, quotes and all: a quoted path is
/// never started.
/// </remarks>
/// <param name="search">The search path a bare program name in another entry's <c>Exec</c> is looked for on.</param>
public sealed class LinuxDesktopRegistration(XdgPlaces places, ProgramSearchPath search) : IDesktopRegistration
{
    private const string Type = "x-scheme-handler/" + DesktopLinks.Scheme;

    private const string Tail = " -- %U";

    /// <summary>This account's places and search path.</summary>
    public static LinuxDesktopRegistration ForThisAccount() =>
        new(XdgPlaces.ForThisAccount(), new ProgramSearchPath(Environment.GetEnvironmentVariable("PATH"), null));

    public string? Program() => Resolve() is { Runs: { } entry } ? Ours(entry) : null;

    public DesktopRefusal? Elsewhere()
    {
        var resolved = Resolve();
        if (resolved.Id is not { } id || (resolved.Runs is { } runs && Ours(runs) is not null) || Declared() is not { } jason)
        {
            return null;
        }

        return resolved.Runs is not null
            ? new(CliErrors.DesktopLinksElsewhere, $"Nothing was opened: this account opens jason-desktop: links with {id} ({resolved.Source}), not with Jason Desktop; xdg-mime default {jason} {Type} gives them back to it.")
            : new(CliErrors.DesktopLinksElsewhere, $"Nothing was opened: {resolved.Source} names {id} for jason-desktop: links, and no menu entry of that name starts a program here; " + (Path.GetFileName(resolved.Source) is "mimeinfo.cache" ? $"update-desktop-database {Path.GetDirectoryName(resolved.Source)} writes it again." : $"xdg-mime default {jason} {Type} gives them to Jason Desktop."));
    }

    /// <summary>What xdg-mime names for the type and where it read it, and the entry xdg-open then runs, or null where it runs none.</summary>
    private sealed record Resolution(string? Id, string? Source, string? Runs);

    private Resolution Resolve()
    {
        var (id, source) = Default();
        return new(id, source, id is null ? null : Started(id));
    }

    /// <summary><c>xdg-mime query default</c> in its generic mode: the entry's name, and the file it was read from.</summary>
    private (string? Id, string? Source) Default()
    {
        var prefixes = places.CurrentDesktop.Select(desktop => desktop.ToLowerInvariant() + "-").Append(string.Empty).ToList();
        foreach (var folder in new[] { places.ConfigHome }.Concat(places.ConfigDirs).Concat(places.Data.Select(data => Path.Combine(data, "applications"))))
        {
            foreach (var prefix in prefixes)
            {
                var list = Path.Combine(folder, prefix + "mimeapps.list");
                foreach (var app in DefaultsIn(list))
                {
                    if (ProgramOf(EntryNamed(app)) is not null)
                    {
                        return (app, list);
                    }
                }
            }
        }

        foreach (var data in places.Data)
        {
            foreach (var prefix in new[] { places.MenuPrefix, string.Empty })
            {
                foreach (var file in new[] { "defaults.list", "mimeinfo.cache" }.Select(name => Path.Combine(data, "applications", prefix + name)))
                {
                    var line = Lines(file).FirstOrDefault(line => line.Contains(Type + "=", StringComparison.Ordinal));
                    if (line is not null && line[(line.IndexOf('=', StringComparison.Ordinal) + 1)..].Split(';')[0] is { Length: > 0 } app)
                    {
                        return (app, file);
                    }
                }
            }
        }

        string? best = null;
        var preference = -1;
        foreach (var data in places.Data)
        {
            foreach (var entry in Entries(Path.Combine(data, "applications")).Where(entry => (Text(entry) ?? string.Empty).Contains(Type + ";", StringComparison.Ordinal)))
            {
                var initial = Lines(entry).Select(line => line.Split('=')).FirstOrDefault(parts => parts.Length > 1 && parts[0].EndsWith("InitialPreference", StringComparison.Ordinal)) is { } parts
                    && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
                if (initial > preference)
                {
                    preference = initial;
                    best = entry;
                }
            }
        }

        return best is null ? (null, null) : (Path.GetFileName(best), Path.GetDirectoryName(best));
    }

    /// <summary>The <c>[Default Applications]</c> entries for the type in one list, in their order; none where it is not there.</summary>
    private static IEnumerable<string> DefaultsIn(string list)
    {
        var inDefaults = false;
        foreach (var line in Lines(list))
        {
            if (line.StartsWith('['))
            {
                inDefaults = line.StartsWith("[Default Applications]", StringComparison.Ordinal);
            }
            else if (inDefaults && line.StartsWith(Type + "=", StringComparison.Ordinal))
            {
                return line[(Type.Length + 1)..].Split(';', StringSplitOptions.RemoveEmptyEntries);
            }
        }

        return [];
    }

    /// <summary>The entry xdg-open runs for a name: the first found under the data home and dirs whose program is there.</summary>
    private string? Started(string id) => places.Data.SelectMany(data => Named(Path.Combine(data, "applications"), id)).FirstOrDefault(entry => ProgramOf(entry) is not null);

    /// <summary>The first entry of a name under the data home and dirs, as xdg-mime looks one up to ask whether its program is there.</summary>
    private string? EntryNamed(string id) => places.Data.SelectMany(data => Named(Path.Combine(data, "applications"), id)).FirstOrDefault();

    /// <summary>A name in one <c>applications</c> folder: the file itself, its vendor folder's form, then each folder below, in order.</summary>
    private static IEnumerable<string> Named(string folder, string id)
    {
        if (id.Contains('/', StringComparison.Ordinal) || !Directory.Exists(folder))
        {
            yield break;
        }

        var direct = Path.Combine(folder, id);
        if (File.Exists(direct))
        {
            yield return direct;
        }
        else if (id.IndexOf('-', StringComparison.Ordinal) is > 0 and var dash && File.Exists(Path.Combine(folder, id[..dash], id[(dash + 1)..])))
        {
            yield return Path.Combine(folder, id[..dash], id[(dash + 1)..]);
        }

        foreach (var below in Folders(folder))
        {
            foreach (var found in Named(below, id))
            {
                yield return found;
            }
        }
    }

    /// <summary>The program xdg-open starts for an entry: <c>Exec</c>'s first word, quotes and all, by its path or on the search path; null where none is there.</summary>
    private string? ProgramOf(string? entry)
    {
        if (entry is null || Value(entry, "Exec") is not { Length: > 0 } exec)
        {
            return null;
        }

        var word = exec.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return ProgramLookup.IsPath(word)
            ? (Path.IsPathFullyQualified(word) && File.Exists(word) ? word : null)
            : ProgramLookup.FullPathOf(word, search);
    }

    /// <summary>The program an entry starts in the desktop's own form, or null: it declares the scheme, and its <c>Exec</c> is a bare full path and <c>-- %U</c>.</summary>
    private static string? Ours(string entry)
    {
        if (Value(entry, "MimeType") is not { } types || !types.Split(';').Contains(Type, StringComparer.Ordinal)
            || Value(entry, "Exec") is not { } exec || !exec.EndsWith(Tail, StringComparison.Ordinal))
        {
            return null;
        }

        var program = exec[..^Tail.Length];
        return program.Length > 0 && !program.Contains(' ', StringComparison.Ordinal) && !program.Contains('"', StringComparison.Ordinal) && Path.IsPathFullyQualified(program) && File.Exists(program) ? program : null;
    }

    /// <summary>Jason Desktop's entry by name, where one in its own form is in an <c>applications</c> folder at all; null where none is.</summary>
    private string? Declared() =>
        places.Data.SelectMany(data => Entries(Path.Combine(data, "applications"))).Where(entry => Ours(entry) is not null).Select(Path.GetFileName).FirstOrDefault();

    /// <summary>A key's value in an entry's <c>[Desktop Entry]</c> group, as written; null where the group has none.</summary>
    private static string? Value(string entry, string key)
    {
        var inEntry = false;
        foreach (var line in Lines(entry))
        {
            if (line.StartsWith('['))
            {
                inEntry = line == "[Desktop Entry]";
            }
            else if (inEntry && line.StartsWith(key + "=", StringComparison.Ordinal))
            {
                return line[(key.Length + 1)..];
            }
        }

        return null;
    }

    /// <summary>The <c>.desktop</c> files of a folder, then of each folder below, in order.</summary>
    private static IEnumerable<string> Entries(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }

        string[] here;
        try
        {
            here = [.. Directory.EnumerateFiles(folder, "*.desktop").Order(StringComparer.Ordinal)];
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            here = [];
        }

        return here.Concat(Folders(folder).SelectMany(Entries));
    }

    private static string[] Folders(string folder)
    {
        try
        {
            return [.. Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal)];
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string[] Lines(string file) => Text(file)?.Split('\n').Select(line => line.TrimEnd('\r')).ToArray() ?? [];

    private static string? Text(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
    /// Why Jason Desktop would not be opened for this account, or null where it takes links: the registration's own words
    /// where the system would hand the address to something else, else <see cref="CliErrors.DesktopNotInstalled"/>.
    /// </summary>
    public static DesktopRefusal? Absent(IDesktopRegistration? registration) =>
        Present(registration) ? null : registration?.Elsewhere() ?? new(CliErrors.DesktopNotInstalled, NotInstalled);

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
    /// refused where no desktop takes links (<see cref="Absent"/>); else handed to the system's
    /// opener, and refused by the opener's code where it did not start or failed. Nothing else is ever opened. A hand-on
    /// still running when the wait ends may yet open the desktop, so it is said on stderr and not refused, as a hand-on that
    /// completed is. The caller says a refusal in its own place.
    /// </summary>
    public static DesktopRefusal? Open(CliEnvironment env, string address, bool handOn)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentException.ThrowIfNullOrEmpty(address);
        if (Absent(env.Desktop) is { } absent)
        {
            return absent;
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
