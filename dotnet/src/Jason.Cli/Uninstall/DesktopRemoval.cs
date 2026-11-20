using System.Globalization;
using System.Text.RegularExpressions;
using Jason.Cli.Commands;
using Jason.Cli.Installation;
using Jason.Cli.Moving;
using Jason.Cli.Process;
using Jason.Cli.Update;
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

    /// <summary>macOS: the bundle deleted, as the last act; Linux: the AppImage file deleted, as the last act.</summary>
    public const string Deleted = "deleted";

    /// <summary>Linux, the deb: the package purged by apt, once the runtime has stopped and before anything of this account's goes.</summary>
    public const string Purged = "purged";

    /// <summary>The carried jason is not inside a folder shaped as the application's, so no application is planned.</summary>
    public const string NotFound = "not_found";
}

/// <summary>
/// What an uninstall run by the jason Jason Desktop carries takes off besides what any installation's does: the
/// application's own placements, read before anything is removed.
/// </summary>
/// <param name="Application">
/// The application's folder (<c>Jason.app</c>, the installed folder on Windows, the deb's <c>/opt/Jason</c>) or an AppImage's
/// file, or null where the carried jason is not inside one.
/// </param>
/// <param name="ApplicationFate">What becomes of it (<see cref="ApplicationFates"/>).</param>
/// <param name="OpenAtLogin">
/// "Open Jason at Login" where it starts this application: the LaunchAgent file on macOS, the values of this account's
/// <c>Run</c> key on Windows, the XDG autostart entry on Linux.
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
    /// account's own. Linux, an AppImage: its files of this account's, likewise: its menu entry and icons, the copies of its
    /// jason and the one an update kept, the record of where the file is, and its support folder.
    /// </summary>
    public IReadOnlyList<string> ApplicationState { get; init; } = [];

    /// <summary>
    /// macOS: the launchd job Squirrel submits for ShipIt on an update (<see cref="DesktopRemoval.ShipItLabel"/>), where
    /// launchd says it is loaded when the plan is read: taken out at its step where it is not running then. Null where it
    /// is not loaded, or launchd could not be asked, which its step says.
    /// </summary>
    public string? UpdaterJob { get; init; }

    /// <summary>The ShipIt job as the plan read it, for the look before anything is removed; never written out.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    internal Autostart.LaunchdJob ShipIt { get; init; } = Autostart.LaunchdJob.Absent;
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

    /// <summary>
    /// The label of the launchd job Squirrel.Mac submits for ShipIt on an update the application installs: the bundle
    /// identifier and <c>.ShipIt</c>, as its cache is named.
    /// </summary>
    public const string ShipItLabel = Identifier + ".ShipIt";

    /// <summary>Linux: the name dpkg knows the deb by, which electron-builder gives it.</summary>
    public const string DebPackage = "jason-desktop";

    /// <summary>Linux: the application's own executable, at the top of the deb's folder and of an AppImage.</summary>
    public const string LinuxLauncher = "jason-desktop-app";

    /// <summary>Linux: an AppImage's menu entry under the account's <c>applications</c> folder, as the install line writes it.</summary>
    public const string MenuEntryName = "jason-desktop.desktop";

    /// <summary>Linux: an AppImage's icon, at each size under the account's <c>icons/hicolor</c>.</summary>
    public const string IconName = LinuxLauncher + ".png";

    /// <summary>How long the package's purge is given, the password asked included: the move's own bound for its install.</summary>
    public static readonly TimeSpan PurgeWait = MoveSystem.AptWait;

    /// <summary>The application's folder that carries <paramref name="carried"/>, or null where it is not inside one.</summary>
    public static string? ApplicationOf(CarriedExecutable carried, SetupPlaces places)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(places);

        if (places.Platform is SetupSystem.Linux)
        {
            return LinuxApplicationOf(carried, places);
        }

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
    /// Linux: the deb's folder, where this jason is the package's (<see cref="SetupPlaces.CarriedByDeb"/>) at
    /// <c>resources/bundle/jason</c> in the folder the package installs to, and only there: a deb unpacked elsewhere is
    /// nobody's package. Else the AppImage file whose copy this jason is, a copy under <c>&lt;data&gt;/desktop/bundle/&lt;version&gt;/</c>
    /// (<see cref="AppImageCopy.FileOf"/>).
    /// </summary>
    private static string? LinuxApplicationOf(CarriedExecutable carried, SetupPlaces places)
    {
        var folder = Path.GetDirectoryName(carried.Executable);
        var above = folder is null ? null : Path.GetDirectoryName(folder);
        if (folder is null || above is null)
        {
            return null;
        }

        if (SetupPlaces.CarriedByDeb(carried.Executable))
        {
            var application = Path.GetDirectoryName(above);
            return application is not null && Named(folder, "bundle") && Named(above, "resources") && SamePath(application, places.DebApplication)
                ? application
                : null;
        }

        return Named(above, "bundle") && Path.GetDirectoryName(above) is { } desktop && Named(desktop, "desktop")
            ? AppImageCopy.FileOf(carried.Executable, places.Home)
            : null;
    }

    private static bool SamePath(string one, string other)
    {
        try
        {
            return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(one)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(other)), StringComparison.Ordinal);
        }
        catch (Exception unreadable) when (unreadable is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

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

        return StartInFlight.Others(env, carried.Executable, plan.Demo);
    }

    /// <summary>The application's own executable inside its folder; on Linux the deb's launcher, or the AppImage file itself.</summary>
    public static string ApplicationExecutable(string application, SetupSystem platform) => platform switch
    {
        SetupSystem.Windows => Path.Combine(application, "Jason.exe"),
        SetupSystem.Linux => Directory.Exists(application) ? Path.Combine(application, LinuxLauncher) : application,
        _ => Path.Combine(application, "Contents", "MacOS", "Jason"),
    };

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
            : places.Platform switch
            {
                SetupSystem.Windows => ApplicationFates.AfterExit,
                SetupSystem.Linux when SetupPlaces.CarriedByDeb(carried.Executable) => ApplicationFates.Purged,
                _ => ApplicationFates.Deleted,
            };
        var windows = places.Platform is SetupSystem.Windows;
        var bundle = Path.GetDirectoryName(carried.Executable)!;
        var shipIt = places.Platform is SetupSystem.MacOs ? env.Removes!.ReadLaunchdJob(ShipItLabel) : Autostart.LaunchdJob.Absent;

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
            ApplicationState = fate != ApplicationFates.Deleted ? []
                : places.Platform is SetupSystem.Linux ? AppImageFiles(places, carried, application!, left)
                : ApplicationStateOf(places, left),
            UpdaterJob = shipIt.State is Autostart.LaunchdJobState.Loaded or Autostart.LaunchdJobState.Running ? ShipItLabel : null,
            ShipIt = shipIt,
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

        if (places.Platform is SetupSystem.Linux)
        {
            var entry = AutostartEntryFile(places);
            if (!Present(entry))
            {
                return [];
            }

            var starts = Installation.CommandFiles.ReadSmall(entry) is { } text ? DesktopEntryProgram(text) : null;
            if (starts is not null && SamePath(starts, executable))
            {
                return [entry];
            }

            left.Add($"{entry}, under Jason's name, starts {starts ?? "nothing it names"}, not this application, so it was left: deleting it takes it off.");
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

            // A wrapper that runs this jason, or one whose jason is gone, or on Linux another version's copy beside this AppImage's;
            // one that runs another application's is that one's.
            if (carried.Is(target.Executable) || !File.Exists(target.Executable) || AnotherCopy(places, carried, target.Executable))
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

    /// <summary>Linux, an AppImage: whether <paramref name="executable"/> is another version's copy in this jason's copies folder.</summary>
    private static bool AnotherCopy(SetupPlaces places, CarriedExecutable carried, string executable) =>
        places.Platform is SetupSystem.Linux && !SetupPlaces.CarriedByDeb(carried.Executable)
        && Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(executable))) is { } copies
        && SamePath(copies, AppImageCopy.BundlesOf(carried.Executable));

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
        var caches = places.Platform switch
        {
            SetupSystem.Windows => [Path.Combine(places.LocalApplicationData ?? places.Home, UpdaterCacheName)],
            SetupSystem.Linux => [Path.Combine(places.CacheHome ?? Path.Combine(places.Home, ".cache"), UpdaterCacheName)],
            _ => new[] { Path.Combine(places.Home, "Library", "Caches", UpdaterCacheName), Path.Combine(places.Home, "Library", "Caches", ShipItCacheName) },
        };
        return [.. caches.Where(Directory.Exists)];
    }

    /// <summary>
    /// Linux, an AppImage, going with the file just before it: the menu entry where it starts that file, the icons, the
    /// copies of its jason under <c>&lt;data&gt;/desktop/bundle/</c>, the one an update kept under
    /// <c>&lt;data&gt;/desktop/rollback/</c>, <c>&lt;data&gt;/desktop/appimage.json</c>, and the support folder; each found as
    /// itself, never through a link.
    /// </summary>
    private static IReadOnlyList<string> AppImageFiles(SetupPlaces places, CarriedExecutable carried, string file, List<string> left)
    {
        var found = new List<string>();
        var menu = Path.Combine(places.Share, "applications", MenuEntryName);
        if (Present(menu))
        {
            var text = Installation.CommandFiles.ReadSmall(menu);
            if (text is not null && ((DesktopEntryValue(text, "TryExec") is { } tried && SamePath(tried, file)) || (DesktopEntryProgram(text) is { } starts && SamePath(starts, file))))
            {
                found.Add(menu);
            }
            else
            {
                left.Add($"{menu}, under Jason Desktop's name, does not start {file}, so it was left: deleting it takes it off the menu.");
            }
        }

        var hicolor = Path.Combine(places.Share, "icons", "hicolor");
        try
        {
            if (Directory.Exists(hicolor))
            {
                found.AddRange(Directory.EnumerateDirectories(hicolor).Order(StringComparer.Ordinal).Select(size => Path.Combine(size, "apps", IconName)).Where(Present));
            }
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            left.Add($"{hicolor} could not be read ({unread.Message}), so Jason Desktop's icons there were left.");
        }

        var desktop = Path.GetDirectoryName(AppImageCopy.BundlesOf(carried.Executable))!;
        found.AddRange(new[] { Path.Combine(desktop, "bundle"), Path.Combine(desktop, "rollback"), Path.Combine(desktop, "appimage.json"), places.SupportFolder }.Where(Present));
        return found;
    }

    /// <summary>
    /// <c>&lt;data&gt;/desktop/machine-setup.json</c> under <paramref name="dataRoot"/>: the version and the place Jason Desktop's
    /// start last set the machine up for, which a start of that version from that place reads as done
    /// (<c>desktop/src/machine-setup.ts</c>). A file of the desktop's own folder, as <see cref="AppImageCopy.BundlesUnder"/> is.
    /// </summary>
    public static string SetupRecordUnder(string dataRoot) => Path.Combine(dataRoot, "desktop", "machine-setup.json");

    /// <summary>Linux: Open Jason at Login's XDG autostart entry, under <c>XDG_CONFIG_HOME</c>, <c>~/.config</c> by default (<c>desktop/src/login-item.ts</c>).</summary>
    public static string AutostartEntryFile(SetupPlaces places)
    {
        ArgumentNullException.ThrowIfNull(places);
        return Path.Combine(places.ConfigHome ?? Path.Combine(places.Home, ".config"), "autostart", Identifier + ".desktop");
    }

    /// <summary>A desktop entry's value for <paramref name="key"/>, the string value's own escapes undone; null where it has none.</summary>
    private static string? DesktopEntryValue(string text, string key)
    {
        if (text.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith(key + "=", StringComparison.Ordinal))?[(key.Length + 1)..] is not { } written)
        {
            return null;
        }

        var value = new System.Text.StringBuilder();
        for (var at = 0; at < written.Length; at++)
        {
            if (written[at] == '\\' && at + 1 < written.Length && written[at + 1] is '\\' or 's' or 'n' or 't' or 'r')
            {
                value.Append(written[++at] switch { 's' => ' ', 'n' => '\n', 't' => '\t', 'r' => '\r', var other => other });
            }
            else
            {
                value.Append(written[at]);
            }
        }

        return value.ToString();
    }

    /// <summary>
    /// A value as a desktop entry's string spells it, which <see cref="DesktopEntryValue"/> reads back: a backslash, a newline,
    /// a tab and a carriage return each as its escape (the Desktop Entry Specification, "Possible value types").
    /// </summary>
    internal static string DesktopEntryString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal);
    }

    /// <summary>
    /// A path as the first, quoted argument of an entry's <c>Exec</c>, which <see cref="DesktopEntryProgram"/> reads back (the
    /// specification, "The Exec key"): a double quote, a backtick, a dollar sign and a backslash each take a backslash, and a
    /// per-cent is doubled, since a launcher reads field codes before it reads quotes; then the string's own escapes
    /// (<see cref="DesktopEntryString"/>).
    /// </summary>
    internal static string DesktopEntryArgument(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var quoted = new System.Text.StringBuilder("\"");
        foreach (var character in path)
        {
            _ = character switch
            {
                '"' or '`' or '$' or '\\' => quoted.Append('\\').Append(character),
                '%' => quoted.Append("%%"),
                _ => quoted.Append(character),
            };
        }

        return DesktopEntryString(quoted.Append('"').ToString());
    }

    /// <summary>
    /// The program a desktop entry's <c>Exec</c> starts, its first argument: the string value's own escapes undone, then a
    /// quoted argument read with each backslash taking the character after it and a doubled per-cent read as one, as the
    /// Desktop Entry Specification reads it and <see cref="DesktopEntryArgument"/> and <c>autostartExec</c> write it; an
    /// unquoted one up to the first space.
    /// </summary>
    internal static string? DesktopEntryProgram(string text)
    {
        if (DesktopEntryValue(text, "Exec") is not { Length: > 0 } line)
        {
            return null;
        }

        if (!line.StartsWith('"'))
        {
            var end = line.IndexOf(' ', StringComparison.Ordinal);
            return end < 0 ? line : line[..end];
        }

        var program = new System.Text.StringBuilder();
        for (var at = 1; at < line.Length; at++)
        {
            switch (line[at])
            {
                case '\\' when at + 1 < line.Length:
                    program.Append(line[++at]);
                    break;
                case '%' when at + 1 < line.Length && line[at + 1] == '%':
                    program.Append(line[++at]);
                    break;
                case '"':
                    return program.ToString();
                default:
                    program.Append(line[at]);
                    break;
            }
        }

        return null;
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
                else if (places.Platform is SetupSystem.Linux)
                {
                    env.Removes!.RemoveFile(item);
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
            // What became of an executable held by a running process is said once, by the removal's sweep of the folder it was
            // renamed into, which comes after this: taken, or left with what to do.
            var (_, removed) = new CommandLineRemoval(places, kept, problems) { SaysAside = false }.Remove(old, replacedByWrapper: false);
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
                    done.Add(Installation.CreatedProfiles.WriteBackOrRemove(places, profile, without)
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

    /// <summary>
    /// macOS, before the updater's caches: the launchd job Squirrel.Mac submits for ShipIt when the application installs an
    /// update (<see cref="ShipItLabel"/>), which launchd keeps loaded once ShipIt has ended. Read at this step, and taken out
    /// of this account's login domain only where launchd says it is loaded and not running, then read again: gone, it is
    /// said done; still there, it is a problem with the command that takes it out. One running, or set to run, is an update
    /// being installed, begun since the uninstall's own look: it is left, never ended part-way, and so are ShipIt's cache,
    /// the application and its own state, which their steps keep and say kept; this says that an update was being
    /// installed, names what of those <paramref name="plan"/> holds, so only what was there and was left, and says what to
    /// do; it returns true. One launchd could not be asked about, in a login domain it says is there, is left as it was, and
    /// said. One not loaded is nothing to say, and nor is one whose login domain launchd says is not there, as an account
    /// with no login session has none (<see cref="Autostart.LaunchdJobs.Read"/>).
    /// </summary>
    public static bool TakeOutUpdaterJob(CliEnvironment env, SetupPlaces places, DesktopPlan plan, List<string> done, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(plan);
        if (places.Platform is not SetupSystem.MacOs)
        {
            return false;
        }

        var takeOut = $"`launchctl bootout gui/$(id -u)/{ShipItLabel}`";
        var job = env.Removes!.ReadLaunchdJob(ShipItLabel);
        switch (job.State)
        {
            case Autostart.LaunchdJobState.Absent:
                return false;
            case Autostart.LaunchdJobState.Running:
                var left = new List<string> { "the job" };
                if (plan.Caches.Any(IsShipItCache))
                {
                    left.Add("ShipIt's state");
                }

                if (plan.Application is not null && plan.ApplicationFate == ApplicationFates.Deleted)
                {
                    left.Add("the application");
                    if (plan.ApplicationState.Count > 0)
                    {
                        left.Add("its own state under ~/Library");
                    }
                }

                problems.Add($"{BeingInstalled(job)}, and an update is never ended part-way, so these were left: {Listed(left)}. {UninstallAgain(env)}");
                return true;
            case Autostart.LaunchdJobState.Unread:
                problems.Add($"launchd could not be asked about the job {ShipItLabel} ({job.Why?.TrimEnd('.')}), so it was left as it was: `launchctl print gui/$(id -u)/{ShipItLabel}` says whether it is loaded, and {takeOut} takes it out.");
                return false;
        }

        env.Removes!.BootOutLaunchdJob(ShipItLabel);
        if (env.Removes!.ReadLaunchdJob(ShipItLabel).State is Autostart.LaunchdJobState.Absent)
        {
            done.Add($"Took the launchd job {ShipItLabel}, which Squirrel submits for ShipIt when Jason Desktop installs an update, out of launchd.");
        }
        else
        {
            problems.Add($"The launchd job {ShipItLabel} is still loaded after `launchctl bootout`: {takeOut} takes it out.");
        }

        return false;
    }

    /// <summary>
    /// What a problem says of ShipIt found running: that an update was being installed, by which process of which job. In
    /// the past, as the report is read once the uninstall has ended, and the update may have ended by then.
    /// </summary>
    private static string BeingInstalled(Autostart.LaunchdJob job)
    {
        var process = job.Pid is { } pid ? string.Create(CultureInfo.InvariantCulture, $"process {pid}, ") : string.Empty;
        return $"An update of Jason Desktop was being installed by ShipIt ({process}the launchd job {ShipItLabel})";
    }

    /// <summary>A list as a sentence says it: "a", "a and b", "a, b and c".</summary>
    private static string Listed(IReadOnlyList<string> items) =>
        items.Count < 2 ? string.Concat(items) : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[^1]}";

    /// <summary>Whether a folder of the plan's caches is ShipIt's state.</summary>
    private static bool IsShipItCache(string folder) => string.Equals(Path.GetFileName(folder), ShipItCacheName, StringComparison.Ordinal);

    /// <summary>What to do once the update ShipIt was installing has ended: the one uninstall again, from either door.</summary>
    private static string UninstallAgain(CliEnvironment env) =>
        "Once the update has ended, uninstall again: from the Jason menu, reopen Jason and choose Uninstall Jason again, or run "
        + $"`\"{StartInFlight.Image(env) ?? "jason"}\" uninstall`; what is already gone is not an error the second time.";

    /// <summary>
    /// The demo's marked folders and the updater's caches; ShipIt's own state kept, and said kept, where ShipIt was found
    /// installing an update at the job's step (<paramref name="shipItInstalling"/>).
    /// </summary>
    public static void RemoveDemoAndCaches(CliEnvironment env, DesktopPlan plan, List<string> done, List<string> kept, List<string> problems, bool shipItInstalling = false)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var (folder, what) in plan.Demo.Select(folder => (folder, "an earlier version's demo folder")).Concat(plan.Caches.Select(folder => (folder, "the updater's cache"))))
        {
            if (shipItInstalling && IsShipItCache(folder))
            {
                kept.Add($"Kept {folder}, ShipIt's state: an update of Jason Desktop was being installed.");
                continue;
            }

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

    /// <summary>
    /// The last look, just before the application is deleted: on macOS, ShipIt installing an update of it, begun since the
    /// job's own step, which is never ended part-way; the window opened again, or a jason of this installation started since
    /// the runtime stopped, both of which run from what goes next. Any of them keeps the application, and its state under
    /// ~/Library or an AppImage's files, and is the problem this returns, with <paramref name="byHand"/> the way to remove it
    /// then; null where none runs. A job launchd could not be asked about keeps nothing here: its step said so.
    /// </summary>
    private static string? StartedSinceTheStop(CliEnvironment env, DesktopPlan plan, string application, SetupSystem platform, string byHand)
    {
        if (platform is SetupSystem.MacOs && env.Removes!.ReadLaunchdJob(ShipItLabel) is { State: Autostart.LaunchdJobState.Running } installing)
        {
            var state = plan.ApplicationState.Count > 0 ? " and its own state under ~/Library" : string.Empty;
            return $"{BeingInstalled(installing)} when the application was to go, and an update is never ended part-way, so these were left: "
                + $"the application at {application}{state}. {UninstallAgain(env)}";
        }

        var window = env.Removes!.RunningFrom(ApplicationExecutable(application, platform));
        if (StartInFlight.Image(env) is not { } image)
        {
            return null;
        }

        var late = StartInFlight.LastLook(env, image, plan.Demo);
        return window.Count == 0 && !late.Any
            ? null
            : StartInFlight.LateProblem(
                env,
                late,
                window,
                $"The application at {application} was not deleted",
                image,
                byHand);
    }

    /// <summary>
    /// The application itself, the last act: kept, handed to its uninstaller, or deleted; on macOS kept with its own state,
    /// and said kept, where ShipIt was found installing an update of it at the job's step (<paramref name="shipItInstalling"/>).
    /// </summary>
    public static void RemoveApplication(CliEnvironment env, DesktopPlan plan, List<string> done, List<string> kept, List<string> problems, bool shipItInstalling = false)
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
                        + "Once that process has ended, and only if Jason is then still listed in Installed apps in Settings, uninstall Jason there; do not start that uninstall before then, since two at once can leave part of the folder behind. "
                        + "Still listed then, a jason started from its folder in the meantime kept the uninstaller from removing it.");
                }
                else
                {
                    problems.Add($"The application's uninstaller could not be started, so the application at {application} was left: uninstall Jason from Installed apps in Settings.");
                }

                return;
            case ApplicationFates.Deleted when env.Setup?.Platform is SetupSystem.Linux:
                RemoveAppImage(env, plan, application, done, problems);
                return;
            case ApplicationFates.Deleted:
                if (shipItInstalling)
                {
                    var state = plan.ApplicationState.Count > 0 ? " and its own state under ~/Library" : string.Empty;
                    kept.Add($"Kept the application at {application}{state}: an update of it was being installed.");
                    return;
                }

                if (!IsJasonsBundle(application))
                {
                    problems.Add($"{application} no longer names Jason Desktop's bundle identifier, {Identifier}, in its Contents/Info.plist, so it was not deleted: it is not known to be Jason's.");
                    return;
                }

                if (StartedSinceTheStop(env, plan, application, SetupSystem.MacOs, $"move {application} to the Trash in the Finder") is { } started)
                {
                    problems.Add(started);
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
                    problems.Add($"The application could not be deleted: {Causes.Line(refused)} {left}. Move {application} to the Trash in the Finder to remove it.");
                }

                return;
        }
    }

    // --- Linux ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Linux, an AppImage, the last act: once nothing runs from it, its files of this account's (<see cref="DesktopPlan.ApplicationState"/>),
    /// then the file itself. The copy of the jason this process runs as goes with the copies, which Linux allows; what this
    /// process still needs was loaded before the executable's step.
    /// </summary>
    private static void RemoveAppImage(CliEnvironment env, DesktopPlan plan, string file, List<string> done, List<string> problems)
    {
        if (StartedSinceTheStop(env, plan, file, SetupSystem.Linux, $"delete {file}") is { } started)
        {
            problems.Add(started);
            return;
        }

        foreach (var place in plan.ApplicationState.Where(Path.Exists))
        {
            try
            {
                if (Directory.Exists(place))
                {
                    env.Removes!.RemoveTree(place);
                }
                else
                {
                    env.Removes!.RemoveFile(place);
                }

                done.Add($"Removed {place}, the AppImage's own.");
            }
            catch (Exception refused) when (refused is RemovalRefused or IOException or UnauthorizedAccessException)
            {
                problems.Add($"{place}, the AppImage's own, could not be removed: {refused.Message} Deleting it takes it off.");
            }
        }

        if (!File.Exists(file))
        {
            return;
        }

        try
        {
            env.Removes!.RemoveFile(file);
            done.Add($"Removed the application, {file}.");

            // The folder the install line made for it, ~/Applications, goes too once it holds nothing else; a folder of the
            // person's own choosing is never touched.
            var linePlace = env.Setup is { } places ? Path.Combine(places.Home, "Applications") : null;
            if (linePlace is not null
                && string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetFullPath(linePlace), StringComparison.Ordinal)
                && env.Removes.RemoveDirectoryIfEmpty(linePlace))
            {
                done.Add($"Removed {linePlace}, which held nothing else.");
            }
        }
        catch (Exception refused) when (refused is RemovalRefused or IOException or UnauthorizedAccessException)
        {
            problems.Add($"The application could not be deleted: {refused.Message} Deleting {file} removes it.");
        }
    }

    /// <summary>
    /// Linux, the deb: the package purged (<see cref="IInstallationRemover.PurgePackageAsync"/>), once the runtime has stopped and
    /// before anything of this account's is removed, the runtime's start at logon included, so a password not given removes
    /// nothing. Null where it went; else the refusal, which ends the uninstall: why, in the words of sudo, pkexec and apt, and
    /// the way by hand; and the runtime it stopped started again (<see cref="StartOwedAgainAsync"/>), except after a run still
    /// going at its bound, which may yet be taking off the files that runtime would run from.
    /// </summary>
    public static async Task<RemovalRefused?> PurgePackageAsync(CliEnvironment env, DesktopPlan plan, UninstallOptions options, List<string> done, List<string> problems, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(plan);

        var refused = await PurgeAsync(env, plan, done, cancellationToken).ConfigureAwait(false);
        if (refused is { Unended: false })
        {
            await StartOwedAgainAsync(env, options, done, problems, cancellationToken).ConfigureAwait(false);
        }

        return refused?.Refusal;
    }

    private static async Task<(RemovalRefused Refusal, bool Unended)?> PurgeAsync(CliEnvironment env, DesktopPlan plan, List<string> done, CancellationToken cancellationToken)
    {
        PackageRun run;
        try
        {
            run = await env.Removes!.PurgePackageAsync(DebPackage, PurgeWait, line => env.Error.WriteLine($"uninstall: {line}"), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception unstarted) when (unstarted is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return (PackageStays($"apt could not be started to take Jason Desktop's package off ({unstarted.Message}).", "Take it off with `sudo apt purge jason-desktop`, then delete what this account keeps by hand, or run `jason uninstall` again once apt runs."), false);
        }

        if (run.ExitCode == 0)
        {
            done.Add($"Took Jason Desktop's package, {DebPackage}, off with apt: {plan.Application}, the jason in every terminal and what the package placed for every account went with it.");
            return null;
        }

        if (run.Said.Trim() is { Length: > 0 } said)
        {
            env.Error.WriteLine(said);
        }

        const string ByHand = "or an administrator takes the package off with `sudo apt purge jason-desktop`, which leaves this account's own files, as the system's removal of any application does.";
        if (run.Refusal is AptRefusal.Unended)
        {
            return (PackageStays(
                string.Create(CultureInfo.InvariantCulture, $"apt had not ended after {PurgeWait.TotalMinutes:F0} minutes: the password was not given in that time, or apt was still waiting for another installation or still at work, and it may yet take Jason Desktop's package off, so the runtime this uninstall stopped was not started again from the files apt may be taking away."),
                "Once apt has ended: where the package stayed, run `jason uninstall` again, or an administrator takes it off with `sudo apt purge jason-desktop`, and Jason Desktop's next start or `jason runtime start` starts the runtime meanwhile; where apt took it off, `jason` went with it, and what this account keeps, which the plan above lists, is deleted by hand, or by installing Jason again and running `jason uninstall`."), true);
        }

        return (run.Refusal switch
        {
            AptRefusal.NoPassword => PackageStays("Jason Desktop is a system package here, and taking it off needs an administrator's password, which was not given.", "Run `jason uninstall` again to be asked again; where this account is not an administrator, an administrator of this computer can type theirs, " + ByHand),
            AptRefusal.NoSession => PackageStays("Jason Desktop is a system package here, which needs an administrator's password to take off, and this session cannot ask for one.", "Run `jason uninstall` in a terminal of your desktop, " + ByHand),
            AptRefusal.DialogFailed => PackageStays("The system's password dialog failed, so Jason Desktop's package was not taken off.", "Sign out and back in, or restart, then run `jason uninstall` again, " + ByHand),
            AptRefusal.NoNewPrivileges => PackageStays("Jason Desktop is a system package here, and this uninstall cannot ask for the password that takes it off: it runs under a Jason Desktop its own update or a switch of mode started again, which the system marks, with everything it starts, as unable to gain an administrator's rights.", "Quit Jason Desktop, open it again from your applications menu and uninstall from it, or run `jason uninstall` in a terminal you open from the menu, " + ByHand),
            AptRefusal.LockHeld => PackageStays("Another installation, the system's own updates likely, held the package system for ten minutes, so Jason Desktop's package was not taken off.", "Run `jason uninstall` again in a few minutes, " + ByHand),
            _ => PackageStays(string.Create(CultureInfo.InvariantCulture, $"apt did not take Jason Desktop's package off: it exited with {run.ExitCode}{(run.LastLine is { } last ? $" and said: {last}" : string.Empty)}."), "`sudo dpkg --configure -a` finishes a package left half configured, and `sudo apt purge jason-desktop` takes it off; then run `jason uninstall` again where it is still there."),
        }, false);
    }

    private static RemovalRefused PackageStays(string why, string what) =>
        new(CliErrors.UninstallRefused, $"{why} Nothing of this account's was removed, the runtime's start at logon included. {what}");

    /// <summary>How long a runtime started again is given to answer: <c>jason runtime start</c>'s own wait.</summary>
    private static readonly TimeSpan StartWait = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Linux, the deb, where the package stays: the runtime this uninstall stopped, started again from the application's
    /// jason by the rule a start owed is kept by, where the stop's note stands and no runtime answers. Said in
    /// <paramref name="done"/>; one that does not answer is a problem, and the note stands for Jason Desktop's next start.
    /// </summary>
    public static async Task StartOwedAgainAsync(CliEnvironment env, UninstallOptions options, List<string> done, List<string> problems, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(options);

        var processes = env.Processes ?? RuntimeProcessControl.Instance;
        if (!StartOwed.Read(env.Paths).Stands || DescriptorProcess.Live(env.Paths, processes) is not null)
        {
            return;
        }

        if (CarriedExecutable.Of(env) is not { } carried)
        {
            problems.Add("The runtime this stopped was not started again: the application's jason is not in place. `jason runtime start` starts it.");
            return;
        }

        IProcessHandle child;
        try
        {
            child = processes.Launch(env.Paths, [carried.Executable], null);
        }
        catch (Exception unstarted) when (unstarted is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            problems.Add($"The runtime this stopped could not be started again from {carried.Executable} ({unstarted.Message}); `jason runtime start` starts it.");
            return;
        }

        using (child)
        {
            var clock = env.Clock ?? TimeProvider.System;
            var until = clock.GetUtcNow() + (options.StopTimeout ?? StartWait);
            while (DescriptorProcess.Live(env.Paths, processes) is null)
            {
                if (child.HasExited || clock.GetUtcNow() >= until)
                {
                    problems.Add($"The runtime this stopped was started again from {carried.Executable} and did not answer; `jason runtime start` starts it, and so does Jason Desktop when it opens.");
                    return;
                }

                await Task.Delay(options.StopPoll ?? TimeSpan.FromMilliseconds(200), clock, cancellationToken).ConfigureAwait(false);
            }
        }

        done.Add($"Started the runtime this stopped again, from {carried.Executable}: the package stays, and this installation with it.");
    }
}
