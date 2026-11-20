using Jason.Cli.Process;
using Jason.Cli.Uninstall;

namespace Jason.Cli.Installation;

/// <summary>
/// An AppImage's menu entry, in the account's <c>applications</c> folder under <see cref="Name"/>: as the install line
/// writes it (<c>install/install.sh</c>, <c>install_appimage</c>), as the move writes it from the entry the file carries,
/// and as <c>jason install</c> brings an earlier one of Jason's to. It is hidden once the file is gone (<c>TryExec</c>),
/// declares the desktop's link scheme (<c>MimeType=x-scheme-handler/jason-desktop;</c>) and starts the file with
/// <c>--</c> before the address (<c>Exec=&lt;path&gt; -- %U</c>), so the system hands Jason Desktop its links.
/// </summary>
/// <remarks>
/// The path is written bare wherever nothing in it needs quoting: xdg-open outside GNOME and KDE takes the first word of
/// <c>Exec</c> as the program, quotes and all, and never starts a quoted one. A path that holds a space or another of the
/// Desktop Entry Specification's reserved characters is quoted as the specification reads it
/// (<see cref="DesktopRemoval.DesktopEntryArgument"/>), which GLib's launchers start and xdg-open's generic mode does not.
/// After the entry is written, <c>update-desktop-database</c> writes the folder's cache, without which GIO finds no program
/// for the scheme there.
/// </remarks>
public static class AppImageMenuEntry
{
    /// <summary>The entry's file name.</summary>
    public const string Name = DesktopRemoval.MenuEntryName;

    /// <summary>The type the entry declares, the desktop's link scheme.</summary>
    public const string Type = "x-scheme-handler/jason-desktop";

    /// <summary>The program that writes an <c>applications</c> folder's cache.</summary>
    public const string CacheTool = "update-desktop-database";

    /// <summary>The characters the Desktop Entry Specification reserves in <c>Exec</c>, and a per-cent, which starts a field code.</summary>
    private const string Reserved = "\"'\\><~|&;$*?#()`%";

    /// <summary>
    /// The entry made from another one's text, the entry the file carries or one written earlier, for <paramref name="file"/>:
    /// every group kept as it is but <c>[Desktop Entry]</c>, where <c>TryExec</c> and <c>MimeType</c> come first after the
    /// group's name and <c>Exec</c> is put in the form above.
    /// </summary>
    public static string Form(string text, string file)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(file);
        var entry = new List<string>();
        var inEntry = false;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (line.StartsWith('['))
            {
                inEntry = line == "[Desktop Entry]";
                entry.Add(line);
                if (inEntry)
                {
                    entry.Add($"TryExec={DesktopRemoval.DesktopEntryString(file)}");
                    entry.Add($"MimeType={Type};");
                }
            }
            else if (inEntry && (line.StartsWith("TryExec=", StringComparison.Ordinal) || line.StartsWith("MimeType=", StringComparison.Ordinal)))
            {
                continue;
            }
            else
            {
                entry.Add(inEntry && line.StartsWith("Exec=", StringComparison.Ordinal) ? $"Exec={Argument(file)} -- %U" : line);
            }
        }

        while (entry.Count > 0 && entry[^1].Length == 0)
        {
            entry.RemoveAt(entry.Count - 1);
        }

        return string.Join('\n', entry) + "\n";
    }

    /// <summary>The path as <c>Exec</c>'s first argument: bare where nothing in it needs quoting, else quoted as the specification reads it.</summary>
    public static string Argument(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Length > 0 && path.All(character => character > ' ' && character != '\u007f' && !Reserved.Contains(character, StringComparison.Ordinal))
            ? path
            : DesktopRemoval.DesktopEntryArgument(path);
    }

    /// <summary>
    /// Whether an entry is the one Jason wrote for <paramref name="file"/>, as the install line and the move write it: it is
    /// hidden once that file is gone, or it starts that file.
    /// </summary>
    public static bool Starts(string text, string file)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(file);
        return (DesktopRemoval.DesktopEntryValue(text, "TryExec") is { } tried && DesktopRemoval.SamePath(tried, file))
            || (DesktopRemoval.DesktopEntryProgram(text) is { } starts && DesktopRemoval.SamePath(starts, file));
    }

    /// <summary>
    /// <c>jason install</c> from an AppImage's copy: the entry Jason wrote for <paramref name="file"/> brought to the form
    /// above where it differs, and the folder's cache written again. An entry that is not there, or not Jason's for that
    /// file, is left as it is. Null where nothing was to do; else what was done, or why not.
    /// </summary>
    public static async Task<(bool Changed, string Said)?> BringUpToDateAsync(CliEnvironment env, string applications, string file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        var menu = Path.Combine(applications, Name);
        if (!File.Exists(menu) || CommandFiles.ReadSmall(menu) is not { } text || !Starts(text, file))
        {
            return null;
        }

        var current = Form(text, file);
        if (string.Equals(current, text, StringComparison.Ordinal))
        {
            return null;
        }

        var staged = menu + ".jason-new";
        try
        {
            File.WriteAllText(staged, current);
            File.Move(staged, menu, overwrite: true);
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            TryDelete(staged);
            return (false, $"Jason Desktop's menu entry {menu} could not be brought up to date ({refused.Message}), so the system may not hand it its links.");
        }

        var cache = await RefreshCacheAsync(env, applications, cancellationToken).ConfigureAwait(false);
        return (true, $"Brought Jason Desktop's menu entry {menu} up to date, so the system hands it its links{(cache ? string.Empty : $"; {CacheTool} is not here to write the folder's cache, so GNOME may not find it until something does")}.");
    }

    /// <summary>
    /// The folder's cache written again by <c>update-desktop-database</c>, found on the search path; true where it ran and
    /// ended 0. Bounded: a tool that hangs is a step that says so, never one that holds the rest.
    /// </summary>
    public static async Task<bool> RefreshCacheAsync(CliEnvironment env, string applications, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        if (env.Programs is not { } programs
            || ProgramRunners.FullPathOf(CacheTool, env.SearchPath ?? Environment.GetEnvironmentVariable("PATH"), null) is not { } tool)
        {
            return false;
        }

        var result = await programs.RunAsync(tool, [applications], null, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        return result is { NotStarted: false, TimedOut: false, ExitCode: 0 };
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception unremoved) when (unremoved is IOException or UnauthorizedAccessException)
        {
            // Left beside the entry, where it changes nothing a launcher reads.
        }
    }
}
