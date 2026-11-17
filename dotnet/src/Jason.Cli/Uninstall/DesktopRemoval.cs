using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Cli.Commands;
using Jason.Cli.Installation;
using Jason.Cli.Moving;
using Jason.Cli.Process;
using Jason.Contracts.Discovery;
using Microsoft.Win32;

namespace Jason.Cli.Uninstall;

/// <summary>The words the plan's <c>application_fate</c> takes.</summary>
public static class ApplicationFates
{
    /// <summary>Left where it is: the Windows uninstaller that ran this removes it next (<c>--keep-application</c>).</summary>
    public const string Kept = "kept";

    /// <summary>Windows: handed to the application's own uninstaller, which removes the folder once this process has ended.</summary>
    public const string AfterExit = "after_exit";

    /// <summary>macOS: the bundle deleted, as the last act.</summary>
    public const string Deleted = "deleted";

    /// <summary>The carried jason is not inside a folder shaped as the application's, so no application is planned.</summary>
    public const string NotFound = "not_found";
}

/// <summary>
/// What an uninstall run by the jason Jason Desktop carries takes off besides what any installation's does: the
/// application's own placements, read before anything is removed.
/// </summary>
/// <param name="Application">The application's folder (<c>Jason.app</c>, or the installed folder on Windows), or null where the carried jason is not inside one.</param>
/// <param name="ApplicationFate">What becomes of it (<see cref="ApplicationFates"/>).</param>
/// <param name="OpenAtLogin">
/// "Open Jason at Login" where it starts this application: the LaunchAgent file on macOS, the values of this account's
/// <c>Run</c> key on Windows.
/// </param>
/// <param name="DesktopLink">The <c>jason-desktop</c> link registration on Windows, where its command starts this application.</param>
/// <param name="CommandFiles">
/// The terminal command's files the application or <c>jason install</c> placed: the wrapper on macOS, the WindowsApps
/// command files on Windows.
/// </param>
/// <param name="CommandFolder">
/// The folder a new terminal finds this jason through: <c>~/.local/bin</c> on macOS, whose profile lines go where it then
/// holds nothing but Jason; on Windows the application's <c>resources\bundle</c>, whose Path entry goes.
/// </param>
/// <param name="CommandLineInstall">An older command-line install of Jason, found by the rule <c>jason install</c> retires one by.</param>
/// <param name="Demo">The two folders an earlier version's demo made, and what a start over left beside them, each carrying the demo's mark.</param>
/// <param name="Caches">The updater's cache, and on macOS Squirrel's ShipIt state.</param>
/// <param name="Left">What was found under the application's names and is not its own, each with why it stays.</param>
public sealed record DesktopPlan(
    string? Application,
    string ApplicationFate,
    IReadOnlyList<string> OpenAtLogin,
    string? DesktopLink,
    IReadOnlyList<string> CommandFiles,
    string CommandFolder,
    string? CommandLineInstall,
    IReadOnlyList<string> Demo,
    IReadOnlyList<string> Caches,
    IReadOnlyList<string> Left)
{
    /// <summary>
    /// macOS: what the application of this bundle identifier leaves under <c>~/Library</c> as its own state, found there,
    /// each going with the application, just before it: its preferences, saved window state, caches, HTTP and WebKit
    /// storage and its support folder. Never where the application stays, nor where the home is <c>/</c> or not the
    /// account's own.
    /// </summary>
    public IReadOnlyList<string> ApplicationState { get; init; } = [];
}

/// <summary>
/// The application's steps, in the uninstall the jason Jason Desktop carries runs: the one routine both doors reach — a
/// terminal, and on Windows the uninstaller in Installed apps, whose start of the application calls this with
/// <c>--keep-application</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything <c>jason install</c> placed is taken back by the rule it was placed by</b>: the wrapper and the WindowsApps
/// files by <see cref="Installation.CommandFiles"/>'s shapes, the profile lines and the Path entry by <see cref="PathEntry"/>,
/// an older command-line install by <see cref="CommandLineInstalls"/>, the skills by their records. And the application's
/// own placements by the shapes <c>desktop/src</c> writes them in, which a test holds to that source: the LaunchAgent and the
/// login item, the <c>jason-desktop</c> link, the demo's marked folders, the updater's caches.
/// </para>
/// <para>
/// <b>The application goes last</b>, after the data directory: on macOS the bundle is deleted by this process, whose own
/// image is in it and stays mapped; on Windows a process the uninstaller would find running from the folder ends it
/// (<c>installer/uninstall.nsh</c>), so a waiter started last hands the folder to the uninstaller once this process has ended.
/// </para>
/// </remarks>
public static partial class DesktopRemoval
{
    /// <summary>What the uninstaller is told when this routine has already taken off what the application placed.</summary>
    public const string UninstalledByJasonFlag = "--uninstalled-by-jason";

    /// <summary>The uninstaller electron-builder writes into the application's folder on Windows.</summary>
    public const string UninstallerName = "Uninstall Jason.exe";

    /// <summary>The demo's marker file, as <c>desktop/src/demo-folders.ts</c> names it.</summary>
    public const string DemoMarkerName = ".jason-demo-folder";

    /// <summary>The marker's one line, exactly as the application of 0.44 and earlier wrote it.</summary>
    public const string DemoMarkerText = "This folder is Jason’s demo, made by Jason Desktop. Remove What Jason Placed removes it.\n";

    /// <summary>The LaunchAgent's label, and the application's bundle identifier.</summary>
    public const string Identifier = "ai.jasonharness.desktop";

    /// <summary>The application's own link scheme.</summary>
    public const string LinkScheme = "jason-desktop";

    /// <summary>electron-updater's cache folder, under <c>%LOCALAPPDATA%</c> or <c>~/Library/Caches</c>.</summary>
    public const string UpdaterCacheName = "jason-desktop-updater";

    /// <summary>Squirrel.Mac's ShipIt state under <c>~/Library/Caches</c>: the bundle identifier and <c>.ShipIt</c>.</summary>
    public const string ShipItCacheName = Identifier + ".ShipIt";

    /// <summary>The application's folder that carries <paramref name="carried"/>, or null where it is not inside one.</summary>
    public static string? ApplicationOf(CarriedExecutable carried, SetupPlaces places)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(places);

        var bundle = Path.GetDirectoryName(carried.Executable);
        var resources = bundle is null ? null : Path.GetDirectoryName(bundle);
        if (bundle is null || resources is null || !Named(bundle, "bundle") || !Named(resources, "Resources"))
        {
            return null;
        }

        if (places.Platform is SetupSystem.Windows)
        {
            var folder = Path.GetDirectoryName(resources);
            return folder is not null && File.Exists(Path.Combine(folder, "Jason.exe")) ? folder : null;
        }

        var contents = Path.GetDirectoryName(resources);
        var application = contents is null ? null : Path.GetDirectoryName(contents);
        return contents is not null && application is not null && Named(contents, "Contents")
            && application.EndsWith(".app", StringComparison.OrdinalIgnoreCase)
            ? application
            : null;
    }

    /// <summary>
    /// Whether a bundle on macOS is Jason Desktop's by its bundle identifier, read from <c>Contents/Info.plist</c>: the folder's
    /// shape alone is no proof before a recursive delete. electron-builder writes that file as XML (app-builder-lib's
    /// <c>savePlistFile</c> builds it with the <c>plist</c> package), from <c>appId</c>; a file that is not there, is a link, is
    /// larger than a property list is, or does not name <see cref="Identifier"/> is not proof.
    /// </summary>
    public static bool IsJasonsBundle(string application)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(application);
        try
        {
            var info = new FileInfo(Path.Combine(application, "Contents", "Info.plist"));
            if (!info.Exists || info.LinkTarget is not null || info.Length > 1024 * 1024)
            {
                return false;
            }

            var match = BundleIdentifier().Match(File.ReadAllText(info.FullName));
            return match.Success && string.Equals(match.Groups[1].Value.Trim(), Identifier, StringComparison.Ordinal);
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    [GeneratedRegex(@"<key>CFBundleIdentifier</key>\s*<string>([^<]*)</string>", RegexOptions.CultureInvariant)]
    private static partial Regex BundleIdentifier();

    /// <summary>
    /// Windows, the terminal door: the processes running the carried jason, in any session, besides this one, the runtime
    /// this uninstall stops, an earlier demo's runtime it stops, and every process any of those started, at any depth: a
    /// runtime's plugin hosts and an agent's <c>jason</c> calls for its work, which that stop ends. The application's
    /// uninstaller ends with nothing removed while any process runs from that file, whichever session it is in
    /// (<c>installer/uninstall.nsh</c>), after this verb has ended and can no longer say so, so the verb refuses first while
    /// one does. A process started after this look still blocks it; the report says what to do then.
    /// </summary>
    public static IReadOnlyList<int> OthersRunningTheCarriedJason(CliEnvironment env, CarriedExecutable carried, DesktopPlan plan)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(plan);

        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        var stopped = new HashSet<int> { Environment.ProcessId };
        foreach (var paths in plan.Demo.Select(folder => new JasonPaths(folder)).Prepend(env.Paths))
        {
            if (DescriptorProcess.Live(paths, processes) is { } runtime)
            {
                stopped.Add(runtime.Pid);
            }
        }

        var removes = env.Removes!;
        return [.. removes.RunningFromInAnySession(carried.Executable).Where(pid => !stopped.Contains(pid) && !Descends(removes, pid, stopped)).Order()];
    }

    /// <summary>The deepest a chain of processes is followed up to one this uninstall stops.</summary>
    private const int DeepestChain = 64;

    /// <summary>
    /// Whether <paramref name="pid"/> was started, through any number of processes, by one of <paramref name="ancestors"/>.
    /// A parent that cannot be told ends the chain, and the process is counted.
    /// </summary>
    private static bool Descends(IInstallationRemover removes, int pid, HashSet<int> ancestors)
    {
        var seen = new HashSet<int> { pid };
        var current = pid;
        for (var step = 0; step < DeepestChain; step++)
        {
            if (removes.ParentOf(current) is not { } parent || !seen.Add(parent))
            {
                return false;
            }

            if (ancestors.Contains(parent))
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>The application's own executable inside its folder.</summary>
    public static string ApplicationExecutable(string application, SetupSystem platform) =>
        platform is SetupSystem.Windows ? Path.Combine(application, "Jason.exe") : Path.Combine(application, "Contents", "MacOS", "Jason");

    private static bool Named(string folder, string name) => string.Equals(Path.GetFileName(folder), name, StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads what the application's steps will take off. It changes nothing.</summary>
    public static DesktopPlan Read(CliEnvironment env, CarriedExecutable carried, SetupPlaces places, bool keepApplication)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(places);

        var left = new List<string>();
        var application = ApplicationOf(carried, places);
        if (application is null)
        {
            left.Add($"{carried.Executable} is not inside a folder shaped as Jason Desktop's, so no application was looked for or is removed here.");
        }
        else if (places.Platform is SetupSystem.MacOs && !IsJasonsBundle(application))
        {
            left.Add($"{application} does not name Jason Desktop's bundle identifier, {Identifier}, in its Contents/Info.plist, so it is not known to be Jason's: no application was looked for or is removed here.");
            application = null;
        }

        var fate = application is null ? ApplicationFates.NotFound
            : keepApplication ? ApplicationFates.Kept
            : places.Platform is SetupSystem.Windows ? ApplicationFates.AfterExit
            : ApplicationFates.Deleted;
        var windows = places.Platform is SetupSystem.Windows;
        var bundle = Path.GetDirectoryName(carried.Executable)!;

        return new DesktopPlan(
            application,
            fate,
            application is null ? [] : OpenAtLogin(places, application, left),
            application is null || !windows ? null : DesktopLink(places, application, left),
            CommandFiles(places, carried, left),
            windows ? bundle : places.LocalBin,
            CommandLineInstalls.Find(env, places, null, []),
            Demo(env, places, left),
            Caches(places),
            left)
        {
            ApplicationState = fate == ApplicationFates.Deleted ? ApplicationStateOf(places, left) : [],
        };
    }

    /// <summary>
    /// The name Electron gives the application, <c>productName</c> in its <c>package.json</c>: the last part of its default
    /// profile folder (<c>app.getPath('userData')</c>, "the appData directory appended with your app's name") and of its
    /// default log folder (<c>~/Library/Logs/YourAppName</c>).
    /// </summary>
    public const string ElectronName = "Jason";

    /// <summary>
    /// The application's own state under <c>~/Library</c>, by its bundle identifier, each found as itself (never through a
    /// link). The application moves its profile and logs into the data directory before it is ready, so what is here is
    /// what macOS and the frameworks write by that identifier, and what an earlier start wrote before the move.
    /// </summary>
    public static IReadOnlyList<string> ApplicationStatePlaces(string home)
    {
        var library = Path.Combine(home, "Library");
        return
        [
            Path.Combine(library, "Preferences", Identifier + ".plist"),
            Path.Combine(library, "Saved Application State", Identifier + ".savedState"),
            Path.Combine(library, "Caches", Identifier),
            Path.Combine(library, "HTTPStorages", Identifier),
            Path.Combine(library, "HTTPStorages", Identifier + ".binarycookies"),
            Path.Combine(library, "WebKit", Identifier),
            Installation.CreatedProfiles.SupportFolder(home),
        ];
    }

    /// <summary>
    /// The two folders under <c>~/Library</c> Electron names by the application's name and not its identifier: its default
    /// profile and log folders. Jason Desktop has kept its profile and logs in the data directory since its first release
    /// (<c>app.setPath('userData')</c>, <c>setAppLogsPath</c>, before Electron is ready), so no file in them is one only Jason
    /// Desktop writes, and nothing proves them Jason's rather than another application's named Jason: they are named and
    /// left, never removed.
    /// </summary>
    public static IReadOnlyList<string> ElectronNamedPlaces(string home) =>
    [
        Path.Combine(home, "Library", "Application Support", ElectronName),
        Path.Combine(home, "Library", "Logs", ElectronName),
    ];

    /// <summary>
    /// Why the steps under <c>~/Library</c> are skipped, or null: a home of <c>/</c>, whose <c>Library</c> is the system's, and
    /// a home that is not this account's own, as the account database names it. An account home that cannot be read decides
    /// nothing.
    /// </summary>
    public static string? LibraryRefusal(string home, string? accountHome)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));
        if (string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full) ?? string.Empty), StringComparison.Ordinal))
        {
            return $"The home directory is {home}, whose Library is the system's, so nothing under {Path.Combine(home, "Library")} was looked at or removed.";
        }

        if (accountHome is not null && !string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetFullPath(accountHome)), StringComparison.Ordinal))
        {
            return $"The home directory is {home}, which is not this account's own ({accountHome}), so nothing under {Path.Combine(home, "Library")} was looked at or removed.";
        }

        return null;
    }

    /// <summary>The preferences file macOS keeps for this bundle identifier.</summary>
    public static string PreferencesFile(string home) => Path.Combine(home, "Library", "Preferences", Identifier + ".plist");

    private static IReadOnlyList<string> ApplicationStateOf(SetupPlaces places, List<string> left)
    {
        if (places.Platform is not SetupSystem.MacOs)
        {
            return [];
        }

        if (LibraryRefusal(places.Home, places.AccountHome()) is { } refusal)
        {
            left.Add(refusal);
            return [];
        }

        foreach (var named in ElectronNamedPlaces(places.Home).Where(Present))
        {
            left.Add($"{named} is named after the application, not its identifier, and holds no file only Jason Desktop writes (it keeps its own in the data directory), so it may be another application's: left as it is, --purge-data or not. Delete it yourself if it is Jason's.");
        }

        return [.. ApplicationStatePlaces(places.Home).Where(Present)];
    }

    /// <summary>There as itself: a file or a folder, never a link, which is left as it stands.</summary>
    private static bool Present(string path)
    {
        var info = new FileInfo(path);
        return (info.Exists || Directory.Exists(path)) && !info.Attributes.HasFlag(FileAttributes.ReparsePoint);
    }

    // --- reading -----------------------------------------------------------------------------------------------

    private static IReadOnlyList<string> OpenAtLogin(SetupPlaces places, string application, List<string> left)
    {
        var executable = ApplicationExecutable(application, places.Platform);
        if (places.Platform is SetupSystem.MacOs)
        {
            var file = LaunchAgentFile(places);
            if (!File.Exists(file))
            {
                return [];
            }

            var program = Installation.CommandFiles.ReadSmall(file) is { } text ? LaunchAgentProgram(text) : null;
            if (program is not null && Inside(application, program, ordinal: true))
            {
                return [file];
            }

            left.Add($"{file}, under Jason's name, starts {program ?? "nothing it names"}, not this application, so it was left: deleting it takes it off.");
            return [];
        }

        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(places.RunKey);
            var found = new List<string>();
            foreach (var name in key?.GetValueNames() ?? [])
            {
                if (key!.GetValue(name) is string command && Starts(command, executable))
                {
                    found.Add($@"HKCU\{places.RunKey}\{name}");
                }
            }

            return [.. found.Order(StringComparer.Ordinal)];
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            left.Add($@"HKCU\{places.RunKey} could not be read ({unread.Message}), so whether Jason opens at login could not be told.");
            return [];
        }
    }

    private static string? DesktopLink(SetupPlaces places, string application, List<string> left)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var key = $@"{places.ClassesKey}\{LinkScheme}";
        try
        {
            using var scheme = Registry.CurrentUser.OpenSubKey(key);
            if (scheme is null)
            {
                return null;
            }

            using var command = Registry.CurrentUser.OpenSubKey($@"{key}\shell\open\command");
            if (command?.GetValue(null) is string written && Starts(written, ApplicationExecutable(application, SetupSystem.Windows)))
            {
                return $@"HKCU\{key}";
            }

            left.Add($@"HKCU\{key} starts {(command?.GetValue(null) as string) ?? "nothing it names"}, not this application, so it was left.");
            return null;
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            left.Add($@"HKCU\{key} could not be read ({unread.Message}), so it was left.");
            return null;
        }
    }

    private static IReadOnlyList<string> CommandFiles(SetupPlaces places, CarriedExecutable carried, List<string> left)
    {
        var found = new List<string>();
        if (places.Platform is SetupSystem.Windows)
        {
            // jason install's own rule for taking them back: each where it is one of the application's shapes.
            foreach (var name in Installation.CommandFiles.Names)
            {
                foreach (var file in new[] { Path.Combine(places.WindowsApps, name + ".cmd"), Path.Combine(places.WindowsApps, name) })
                {
                    if (Installation.CommandFiles.IsRegularFile(file) && Installation.CommandFiles.ReadSmall(file) is { } text && Installation.CommandFiles.IsOurs(text))
                    {
                        found.Add(file);
                    }
                }
            }

            return found;
        }

        foreach (var name in Installation.CommandFiles.Names)
        {
            var file = Path.Combine(places.LocalBin, name);
            if (!Installation.CommandFiles.IsRegularFile(file) || Installation.CommandFiles.ReadSmall(file) is not { } text
                || Installation.CommandFiles.WrapperTarget(text) is not { } target)
            {
                continue;
            }

            // A wrapper that runs this jason, or one whose jason is gone; one that runs another application's is that one's.
            if (carried.Is(target.Executable) || !File.Exists(target.Executable))
            {
                found.Add(file);
            }
            else
            {
                left.Add($"{file} runs {target.Executable}, another application's jason, so it was left.");
            }
        }

        return found;
    }

    /// <summary>
    /// The data directories an earlier version's demo made, each only where it carries the demo's mark: where a runtime it
    /// left running holds the carried jason too. A carried update stops one before it steps out of the application's folder.
    /// </summary>
    public static IReadOnlyList<string> DemoFolders(CliEnvironment env, SetupPlaces places)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(places);
        return Demo(env, places, []);
    }

    private static IReadOnlyList<string> Demo(CliEnvironment env, SetupPlaces places, List<string> left)
    {
        var data = Path.TrimEndingDirectorySeparator(Path.GetFullPath(env.Paths.Root));
        var root = Path.TrimEndingDirectorySeparator(HarnessRoot(env, places));
        var found = new List<string>();
        foreach (var folder in new[] { data + "-demo", root + "-demo" })
        {
            if (Path.Exists(folder) && !Holds(folder, data) && !Holds(folder, root))
            {
                if (IsDemoFolder(folder))
                {
                    found.Add(folder);
                }
                else
                {
                    left.Add($"{folder} carries no mark of the demo's, so it is not a folder Jason Desktop made, and it was left.");
                }
            }

            // What a start over left beside it: removed only where it carries the mark.
            try
            {
                if (Path.GetDirectoryName(folder) is { } parent && Directory.Exists(parent))
                {
                    found.AddRange(Directory.EnumerateDirectories(parent, Path.GetFileName(folder) + ".removing-*").Where(IsDemoFolder).Order(StringComparer.Ordinal));
                }
            }
            catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
            {
                // Nothing of the demo's can be told there, and nothing there is removed.
            }
        }

        return found;
    }

    private static IReadOnlyList<string> Caches(SetupPlaces places)
    {
        var caches = places.Platform is SetupSystem.Windows
            ? [Path.Combine(places.LocalApplicationData ?? places.Home, UpdaterCacheName)]
            : new[] { Path.Combine(places.Home, "Library", "Caches", UpdaterCacheName), Path.Combine(places.Home, "Library", "Caches", ShipItCacheName) };
        return [.. caches.Where(Directory.Exists)];
    }

    /// <summary>The harness root as the runtime reads it: the variable, else the settings, else <c>Jason</c> in the home.</summary>
    private static string HarnessRoot(CliEnvironment env, SetupPlaces places)
    {
        try
        {
            if (InstallationPlaces.Variable(env) is { } variable && !string.IsNullOrWhiteSpace(variable))
            {
                return Path.GetFullPath(variable.Trim());
            }

            var settings = File.Exists(env.Paths.UserSettingsFile) ? File.ReadAllText(env.Paths.UserSettingsFile) : null;
            if (InstallationPlaces.SettingsRoot(settings) is { } written && !string.IsNullOrWhiteSpace(written))
            {
                return Path.GetFullPath(written.Trim());
            }
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            // The default, as the runtime falls back to it.
        }

        return Path.Combine(places.Home, "Jason");
    }

    private static bool IsDemoFolder(string folder)
    {
        try
        {
            var info = new DirectoryInfo(folder);
            var marker = new FileInfo(Path.Combine(folder, DemoMarkerName));
            return info.Exists && info.LinkTarget is null && marker.Exists && marker.LinkTarget is null
                && string.Equals(File.ReadAllText(marker.FullName), DemoMarkerText, StringComparison.Ordinal);
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Whether <paramref name="outer"/> is <paramref name="inner"/> or holds it.</summary>
    private static bool Holds(string outer, string inner)
    {
        var a = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outer)) + Path.DirectorySeparatorChar;
        var b = Path.TrimEndingDirectorySeparator(Path.GetFullPath(inner)) + Path.DirectorySeparatorChar;
        return b.StartsWith(a, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static bool Inside(string folder, string path, bool ordinal) =>
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar, ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a command line starts <paramref name="executable"/>: its first word, quoted or not, names that file.</summary>
    internal static bool Starts(string command, string executable)
    {
        var trimmed = command.TrimStart();
        string first;
        if (trimmed.StartsWith('"'))
        {
            var end = trimmed.IndexOf('"', 1);
            first = end < 0 ? trimmed[1..] : trimmed[1..end];
        }
        else
        {
            var end = trimmed.IndexOf(' ', StringComparison.Ordinal);
            first = end < 0 ? trimmed : trimmed[..end];
        }

        try
        {
            return first.Length > 0 && string.Equals(Path.GetFullPath(first), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception unreadable) when (unreadable is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>The LaunchAgent "Open Jason at Login" writes on macOS (<c>desktop/src/login-item.ts</c>).</summary>
    public static string LaunchAgentFile(SetupPlaces places)
    {
        ArgumentNullException.ThrowIfNull(places);
        return Path.Combine(places.Home, "Library", "LaunchAgents", Identifier + ".plist");
    }

    /// <summary>The program a LaunchAgent starts, its first <c>ProgramArguments</c> string, as <c>launchAgentProgram</c> reads it.</summary>
    public static string? LaunchAgentProgram(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var match = ProgramArguments().Match(text);
        return match.Success
            ? match.Groups[1].Value.Replace("&lt;", "<", StringComparison.Ordinal).Replace("&gt;", ">", StringComparison.Ordinal).Replace("&amp;", "&", StringComparison.Ordinal)
            : null;
    }

    [GeneratedRegex(@"<key>ProgramArguments</key>\s*<array>\s*<string>([^<]*)</string>", RegexOptions.CultureInvariant)]
    private static partial Regex ProgramArguments();

    // --- the steps -----------------------------------------------------------------------------------------------

    /// <summary>"Open Jason at Login", beside the runtime's own logon registration and before anything is stopped.</summary>
    public static void RemoveOpenAtLogin(CliEnvironment env, SetupPlaces places, DesktopPlan plan, List<string> done, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var item in plan.OpenAtLogin)
        {
            try
            {
                if (places.Platform is SetupSystem.MacOs)
                {
                    // Unloaded first, where the window was looked for and is not open: a loaded job would stay loaded until
                    // the person logs out. Where the application is kept, the window may be the job's own, and an unload
                    // would end it, so the file alone goes.
                    if (plan.ApplicationFate == ApplicationFates.Deleted)
                    {
                        env.Removes!.UnloadLaunchAgent(item, Identifier);
                        env.Removes!.RemoveFile(item);
                    }
                    else
                    {
                        env.Removes!.RemoveFile(item);
                        done.Add($"Open Jason at Login's job, {Identifier}, stays loaded until the next login: its file is gone, and the application kept here may be that job's own window, which an unload would end.");
                    }
                }
                else if (OperatingSystem.IsWindows())
                {
                    var name = item[(item.LastIndexOf('\\') + 1)..];
                    using (var run = Registry.CurrentUser.OpenSubKey(places.RunKey, writable: true))
                    {
                        run?.DeleteValue(name, throwOnMissingValue: false);
                    }

                    // Electron writes the value's state beside it, under the same name, and deletes both together.
                    using var approved = Registry.CurrentUser.OpenSubKey(places.StartupApprovedKey, writable: true);
                    approved?.DeleteValue(name, throwOnMissingValue: false);
                }

                done.Add($"Turned off Open Jason at Login: removed {item}.");
            }
            catch (Exception refused) when (refused is RemovalRefused or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                problems.Add($"Open Jason at Login, {item}, could not be removed: {refused.Message} Removing it by hand turns it off; the steps after this one still ran, and each is listed.");
            }
        }
    }

    /// <summary>
    /// The runtime an earlier version's demo may have left running on the demo's data directory, which holds this jason
    /// too: stopped through <c>jason runtime stop</c> on that directory, and one that will not stop ends the uninstall.
    /// </summary>
    public static async Task<RemovalRefused?> StopDemoRuntimeAsync(CliEnvironment env, DesktopPlan plan, UninstallOptions options, List<string> done, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(options);

        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        foreach (var folder in plan.Demo)
        {
            var paths = new JasonPaths(folder);
            if (DescriptorProcess.Live(paths, processes) is null)
            {
                continue;
            }

            using var said = new StringWriter();
            await RuntimeStopCommand.RunAsync(env with { Paths = paths, Out = said }, human: false, cancellationToken, options.StopTimeout, options.StopPoll).ConfigureAwait(false);
            if (DescriptorProcess.Live(paths, processes) is { } still)
            {
                return new RemovalRefused(
                    CliErrors.RuntimeStillRunning,
                    string.Create(CultureInfo.InvariantCulture, $"The demo's runtime, process {still.Pid}, on {folder}, did not stop, so nothing further was removed, except the logon registrations already taken away. ")
                    + $"Stop it with `jason runtime stop` with JASON_DATA_DIR set to {folder}, and run this again. It said: {said.ToString().Trim()}");
            }

            done.Add($"Stopped the runtime an earlier version's demo left running on {folder}.");
        }

        return null;
    }

    /// <summary>The <c>jason-desktop</c> link registration on Windows, where its command starts this application.</summary>
    public static void RemoveDesktopLink(DesktopPlan plan, List<string> done, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.DesktopLink is not { } link || !OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(link[@"HKCU\".Length..], throwOnMissingSubKey: false);
            done.Add($"Removed the registration of jason-desktop links to this application, {link}.");
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            problems.Add($"{link} could not be removed: {refused.Message} Removing that key by hand takes it off; the steps after this one still ran, and each is listed.");
        }
    }

    /// <summary>
    /// The terminal command, by the rules it was placed by: the command files, an older command-line install, then the
    /// Path entry or the profile lines of the folder a new terminal found this jason through, where it holds nothing but
    /// Jason's.
    /// </summary>
    public static void RemoveCommand(CliEnvironment env, SetupPlaces places, DesktopPlan plan, List<string> done, List<string> kept, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(plan);

        var tookFromFolder = false;
        foreach (var file in plan.CommandFiles)
        {
            try
            {
                env.Removes!.RemoveFile(file);
                tookFromFolder |= string.Equals(Path.GetDirectoryName(file), plan.CommandFolder, StringComparison.Ordinal);
                done.Add($"Removed {file}, which ran this jason as a terminal command.");
            }
            catch (Exception refused) when (refused is RemovalRefused or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{file} could not be removed: {refused.Message}");
            }
        }

        if (plan.CommandLineInstall is { } old)
        {
            // What became of an executable held by a running process is said by the routine itself, once.
            var (_, removed) = new CommandLineRemoval(places, kept, problems).Remove(old, replacedByWrapper: false);
            done.Add($"Took off the command-line install at {old}{(removed.Count > 0 ? ": removed " + string.Join(", ", removed) : string.Empty)}.");
        }

        if (places.Platform is SetupSystem.Windows)
        {
            RemoveBundleEntry(places, plan.CommandFolder, done, kept, problems);
            return;
        }

        RemoveProfileLines(places, plan.CommandFolder, done, kept, problems);
        if (tookFromFolder)
        {
            try
            {
                if (env.Removes!.RemoveDirectoryIfEmpty(plan.CommandFolder))
                {
                    done.Add($"Removed {plan.CommandFolder}, which held nothing else.");
                }
            }
            catch (Exception refused) when (refused is RemovalRefused or IOException or UnauthorizedAccessException)
            {
                kept.Add($"{plan.CommandFolder} is empty and was left: {refused.Message}");
            }
        }
    }

    private static void RemoveBundleEntry(SetupPlaces places, string bundle, List<string> done, List<string> kept, List<string> problems)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var value = new UserPathValue(places.PathKey);
            var stored = value.Read();
            var expand = PathEntry.ReadAs(stored?.Kind != RegistryValueKind.String);
            if (stored is null || !PathEntry.Carries(stored.Raw, bundle, expand))
            {
                kept.Add($"{bundle} was not on this account's Path, so there was nothing to take off it.");
                return;
            }

            if (!PathEntry.CarriedBundle(bundle, windows: true))
            {
                kept.Add($"'{bundle}' is on this account's Path, but it holds more than the application carries there, so the entry is not Jason's to take off: whatever else is in it is on the Path through the same entry.");
                return;
            }

            value.Write(PathEntry.WithoutDirectory(stored.Raw, bundle, expand), stored.Kind);
            done.Add($"Took {bundle} off this account's Path.");
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            problems.Add($"{bundle} could not be taken off this account's Path: {refused.Message} Take it off by hand; the steps after this one still ran, and each is listed.");
        }
    }

    private static void RemoveProfileLines(SetupPlaces places, string folder, List<string> done, List<string> kept, List<string> problems)
    {
        var plan = PathEntry.ReadProfiles(folder, places.Home, places.Shell);
        if (plan.Profiles.Count == 0)
        {
            return;
        }

        // Read after the wrapper went: the lines are Jason's only where the folder now holds nothing but what Jason writes.
        if (!plan.Ours && Directory.Exists(folder))
        {
            kept.Add(PathEntry.WhyLeft(plan, windows: false));
            return;
        }

        foreach (var profile in plan.Profiles)
        {
            try
            {
                var bytes = File.ReadAllBytes(profile);
                var without = PathEntry.WithoutEntry(bytes, folder);
                if (!ReferenceEquals(without, bytes))
                {
                    done.Add(Installation.CreatedProfiles.WriteBackOrRemove(places.Home, profile, without)
                        ? $"Took the lines that put {folder} on the PATH out of {profile}, which jason install created, and removed it: nothing else was in it."
                        : $"Took the lines that put {folder} on the PATH out of {profile}.");
                }
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{profile} could not be rewritten ({refused.Message}): take the line {PathEntry.ExportLine(folder)} and the line {PathEntry.Marker} above it out by hand.");
            }
        }
    }

    /// <summary>The demo's marked folders and the updater's caches.</summary>
    public static void RemoveDemoAndCaches(CliEnvironment env, DesktopPlan plan, List<string> done, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var (folder, what) in plan.Demo.Select(folder => (folder, "an earlier version's demo folder")).Concat(plan.Caches.Select(folder => (folder, "the updater's cache"))))
        {
            try
            {
                env.Removes!.RemoveTree(folder);
                done.Add($"Removed {folder}, {what}.");
            }
            catch (Exception refused) when (refused is RemovalRefused or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{folder}, {what}, could not be removed: {refused.Message} Deleting the folder takes it off.");
            }
        }
    }

    /// <summary>
    /// macOS, just before the bundle: the application's own state under <c>~/Library</c>. The preferences go through the
    /// preferences system first
    /// (<c>defaults delete</c>), which would otherwise write back a copy it holds, then as a file.
    /// </summary>
    private static void RemoveApplicationState(CliEnvironment env, DesktopPlan plan, List<string> done, List<string> problems)
    {
        const string what = "the application's own state";
        foreach (var place in plan.ApplicationState)
        {
            try
            {
                if (string.Equals(Path.GetFileName(place), Identifier + ".plist", StringComparison.Ordinal))
                {
                    env.Removes!.DeletePreferences(Identifier);
                }

                if (Directory.Exists(place))
                {
                    env.Removes!.RemoveTree(place);
                }
                else if (File.Exists(place))
                {
                    env.Removes!.RemoveFile(place);
                }

                done.Add($"Removed {place}, {what}.");
            }
            catch (Exception refused) when (refused is RemovalRefused or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{place}, {what}, could not be removed: {refused.Message} Deleting it takes it off.");
            }
        }
    }

    /// <summary>The application itself, the last act: kept, handed to its uninstaller, or deleted.</summary>
    public static void RemoveApplication(CliEnvironment env, DesktopPlan plan, List<string> done, List<string> kept, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Application is not { } application)
        {
            return;
        }

        switch (plan.ApplicationFate)
        {
            case ApplicationFates.Kept:
                kept.Add($"The application at {application} is kept here: the uninstaller that asked for this removes it next.");
                return;
            case ApplicationFates.AfterExit:
                if (!File.Exists(Path.Combine(application, UninstallerName)))
                {
                    problems.Add($"{Path.Combine(application, UninstallerName)} is not there, so the application at {application} was left: uninstall Jason from Installed apps in Settings, or delete that folder.");
                    return;
                }

                if (env.Removes!.HandToUninstaller(application, Environment.ProcessId, env.Paths.LogsDirectory) is { } waiter)
                {
                    done.Add(string.Create(CultureInfo.InvariantCulture, $"Handed the application at {application} to its own uninstaller, which removes it once this command has ended: process {waiter} waits for that. ")
                        + "If Jason is still listed in Installed apps in Settings afterwards, a jason started from its folder in the meantime kept the uninstaller from removing it: uninstall Jason there.");
                }
                else
                {
                    problems.Add($"The application's uninstaller could not be started, so the application at {application} was left: uninstall Jason from Installed apps in Settings.");
                }

                return;
            case ApplicationFates.Deleted:
                if (!IsJasonsBundle(application))
                {
                    problems.Add($"{application} no longer names Jason Desktop's bundle identifier, {Identifier}, in its Contents/Info.plist, so it was not deleted: it is not known to be Jason's.");
                    return;
                }

                RemoveApplicationState(env, plan, done, problems);

                try
                {
                    env.Removes!.RemoveTree(application);
                    done.Add($"Removed the application, {application}.");
                }
                catch (Exception refused) when (refused is not OperationCanceledException)
                {
                    // macOS may refuse a terminal the right to delete an application (Privacy & Security, App Management), and
                    // a delete refused part-way leaves part of the bundle. Never passed over: the verb ends 1 on it.
                    var left = Directory.Exists(application) ? $"{application} is still there, and may be only part of the application now" : $"{application} is gone";
                    problems.Add($"The application could not be deleted: {Causes.Line(refused)} {left}. Move {application} to the Bin (the Trash) in the Finder to remove it.");
                }

                return;
        }
    }
}
