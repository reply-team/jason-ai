using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Installation;
using Jason.Contracts.Discovery;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.Cli.Uninstall;

/// <summary>
/// <c>jason uninstall</c>: taking this installation off the machine, by receipt.
/// </summary>
/// <remarks>
/// <para>
/// A CLI verb rather than an API operation, and not a noun either. Nothing about removing Jason from a machine
/// is the runtime's to perform or be asked about — a logon registration, an executable, a PATH entry, the
/// files a deployment recorded — so <c>Operations</c> carries no <c>uninstall.*</c> and never will.
/// </para>
/// <para>
/// <b>Skills go only by receipt.</b> Each root a deployment wrote into carries a record listing every path
/// written there; this verb removes those skills and nothing else. A verb that deleted by pattern would
/// eventually delete somebody's own file, and a verb that deletes by receipt cannot. The rest of the
/// installation — a registration, a PATH entry, an executable — has no receipt and goes by rule, each rule
/// written where its step is.
/// </para>
/// <para>
/// <b>And the data directory is the operator's.</b> It holds the database, the settings, the plugins, the logs
/// and the work directories — the record of what they did. It is kept unless <c>--purge-data</c> says
/// otherwise, and the verb says in one line that it kept it and where.
/// </para>
/// </remarks>
public static class UninstallCommand
{
    /// <summary>
    /// What this verb will and will not touch, printed in help. A destructive verb whose boundaries are only
    /// in a design note is one somebody runs without knowing them.
    /// </summary>
    private static readonly string Boundaries =
        $"""
        Removed, in this order, each step reported:
          1. the logon registration, first, so a logon part-way through cannot start what is going: this
             account's, on Windows including the task an earlier version registered as Jason. On macOS it
             comes just after step 2: taking the job out of launchd ends the runtime launchd started, with
             no drain, and the job starts no runtime that stopped. On Linux from the deb it comes just after
             the package, below, so a package that stays leaves it as it was; one that cannot be taken off
             then is named among the problems, with the commands that take it off by hand, and the rest goes
          2. the runtime — and if it will not stop, nothing further is removed. Before the runtime is looked
             for and before anything is removed, a runtime that is still starting is waited for, up to {StartInFlight.Seconds(StartInFlight.Bound)},
             and then stopped; a dry run never waits. Where another jason of this installation still runs
             (jason_running), nothing is removed at that bound, and nothing further is removed where one still
             runs {StartInFlight.Seconds(StartInFlight.Settle)} after the stop, a runtime started again since the stop included; one
             more look comes just before the executable, or on macOS the application, is deleted, and one
             found then keeps it. Such a jason may be a runtime still starting or migrating its database, a
             set-up or an update finishing, or a call to a provider a stopped runtime left running: wait until
             it ends, then run this again. On a computer more than one account uses, it may be Jason running
             under another account from this same application, which that account quits. Windows never
             waits or looks again: where the application carries this jason, it refuses one running from its
             folder at once, as below
          3. on Windows, the notification registration: the AppUserModelID key the toast was shown under, the
             two entries Windows made for it, and the jason: link registration where its command names this
             executable
          4. every skill a deployment recorded, by the paths its record names
          5. the PATH entry, only where the installer wrote it and its directory holds nothing but Jason — the
             executable, its two notice files and what an upgrade left beside it: the marked block in a login
             profile, and in ~/.bashrc or ~/.zshrc beside it; on Windows, the entry in this account's Path
          6. the executable, its notice files (jason-LICENSE, jason-THIRD-PARTY-NOTICES), its install
             directory, the native libraries it unpacked on first run, and on Windows those every other
             build of this program unpacked beside them — a running copy holds its own, and each build
             unpacks again on its next start. Elsewhere a running copy holds nothing, so those are counted
             and left, for you to remove once nothing runs from them; and with DOTNET_BUNDLE_EXTRACT_BASE_DIR
             set they are not looked for. On Windows what a build of Jason unpacked under another temporary
             folder - the folder its own temporary folder is numbered inside, where it is one, and that
             folder's numbered folders, or its own numbered folders where it is none - is named, with that
             folder, and left: this removal takes only what stands under its own temporary folder, and what
             it names is safe to remove once nothing runs from it. Then, on Windows, each
             jason-retired-<id> folder an install, an
             update or a move renamed a running jason.exe into - in the temporary folder, and beside the
             folder of the default command-line install and of the one this removal takes - once that
             process has ended; one it still runs is named, for you to remove once it has ended

        A file another program holds open is tried again for up to {InstallationRemovers.HeldFiles.WaitInWords} before it is reported;
        elsewhere than Windows an open file does not stop the removal.

        Kept unless --purge-data:  the data directory, the desktop's own files in it included: the transcripts
                                   of the chat's turns on a provider's key are kept there, under
                                   desktop/claude-keyed/ and desktop/codex-keyed/. --purge-data removes what
                                   Jason keeps there (a setup/ folder only where a layout.json or
                                   layout-migration.json beside it says Jason made it), and the
                                   directory once nothing else is in it; it prints what will be deleted and
                                   asks before anything at all is removed, unless --yes. A directory holding a
                                   file-system root, your profile, the temporary directory or the installation
                                   is refused, and so is a folder of the harness root or one inside it, and one
                                   holding such a folder directly in it or anywhere in a folder the purge would
                                   delete, each known by its .jason/marker.json, or holding a folder there that
                                   cannot be looked into.
        Never touched:             a skill there is no receipt for, any other skill in an agent harness,
                                   any provider CLI, any model credential, a PATH entry the installer did
                                   not write, another account's logon registration, the notifications
                                   already in the notification center, which are the person's to dismiss,
                                   the transcripts the agent hosts keep of the desktop's chat in their own
                                   folders, which are the hosts' own and stay where each host keeps them,
                                   a jason: link registration another program owns, and one whose command is
                                   other than this installation's: it may be another installation's or a
                                   spelling of this one.
                                   And the harness root (Harness:Root, ~/Jason by default) with every
                                   folder and file in it, --purge-data included: the purge empties the
                                   data directory, never the root. A link in the data directory is
                                   removed as itself: what it points to is never touched.
        Installed by a package manager (Homebrew, winget): steps 5 and 6 are the manager's. The executable, its
        directory and the PATH entry that reaches it are left to it, and the plan names the command that takes
        them off: brew uninstall jason, or winget uninstall Reply.Jason. The runtime is still stopped first.
        Run as the jason Jason Desktop carries (a version.json beside the executable), on macOS, Windows and Linux,
        this takes Jason Desktop off as well, one uninstall from either door, the data directory kept unless
        --purge-data, the harness root never:
          - refused before anything is removed while Jason Desktop is open (desktop_running): quit it
            first. --wait-for <pid> waits up to two minutes for that process to end before anything is read.
          - on Windows, from a terminal, refused before anything is removed while another process runs this
            jason, in any session (jason_running), at once and with no wait: a runtime still starting, a
            runtime of another data directory, a journal watch, a call of an agent host opened by hand. The
            application's uninstaller removes nothing while one does. The runtime this stops, and every
            process it started (its plugin hosts, its agents' calls), are not counted.
          - on Linux from the deb, just after the runtime and before anything of this account's is removed: the package
            jason-desktop, purged with apt-get, which asks for an administrator's password once, through sudo where
            there is a terminal, else through the desktop's own dialog (pkexec), and through that dialog started by
            the user manager (systemd-run --user) where this process cannot gain privileges, as under a Jason
            Desktop its update or a switch of mode started again; its scripts take back what it placed for every
            account, /usr/bin/jason among them. Where it does not go - no password given, no session to ask in, the
            package system held, apt failing - nothing of this account's is removed, the runtime's start at
            logon included, the runtime this stopped is started again, and the verb exits 1 naming sudo apt purge
            jason-desktop as the way by hand.
          - just after the runtime, so a runtime that will not stop leaves it in place: Open Jason at Login,
            where it starts this application; on Linux the XDG autostart entry ai.jasonharness.desktop.desktop under
            XDG_CONFIG_HOME (~/.config by default). On macOS, with --keep-application, its file goes and its job is not unloaded, since the application kept
            may be that job's own window: the output says the job stays loaded until the next login.
          - with the runtime: the runtime an earlier version's demo left running on its own data directory.
          - with the jason: link: the jason-desktop link registration, where it starts this application.
          - in place of steps 5 and 6: the terminal command jason install placed, by the rules it placed it by:
            on macOS and Linux ~/.local/bin/jason and jason-desktop where each is the application's wrapper running this
            jason (or a jason that is gone, or for an AppImage another version's copy beside it; from the deb
            /usr/bin/jason is the package's), then install.sh's lines for ~/.local/bin where it then holds nothing
            but Jason, a profile file removed only where jason install recorded that it created it and nothing
            but whitespace is left, then the folder once empty; on Windows the application's jason and jason-desktop command
            files in WindowsApps, and the entry in this account's Path naming the application's resources\bundle,
            where that folder holds nothing but what the application carries there. Then the native libraries
            this jason's build unpacked, as step 6 takes them: this jason goes with the application. An older command-line install
            of Jason, found where jason install looks for one, goes with its notice files, its PATH entry or
            profile block and its folder. Then the folders an earlier version's demo made, each only where it
            carries the demo's mark, and the updater's cache (on macOS also ~/Library/Caches/
            ai.jasonharness.desktop.ShipIt; on Linux under XDG_CACHE_HOME, ~/.cache by default). Then, on Windows,
            the jason-retired-<id> folders, as step 6 says,
            beside the older command-line install's folder too.
          - macOS, just before the application and only where it goes: its own state under ~/Library, each
            where found as itself and named in the plan: Preferences/ai.jasonharness.desktop.plist (through
            defaults delete, then the file), Saved Application State/ai.jasonharness.desktop.savedState,
            Caches/ai.jasonharness.desktop (Squirrel's), HTTPStorages/ai.jasonharness.desktop and its
            .binarycookies, WebKit/ai.jasonharness.desktop and Application Support/ai.jasonharness.desktop
            (jason install's record of the profiles it created). Application Support/Jason and Logs/Jason,
            named after the application and not its identifier, are named and left, --purge-data or not:
            nothing in them proves them Jason Desktop's, which keeps its own in the data directory.
            --keep-application keeps all of it. Where the home directory is / or not the account's own,
            nothing under ~/Library is looked at, and the plan says so.
          - Linux, an AppImage, just before the file and only where it goes: its menu entry, applications/
            jason-desktop.desktop under XDG_DATA_HOME (~/.local/share by default) where it starts that file, its icons
            icons/hicolor/<size>/apps/jason-desktop-app.png there, the copies of its jason under
            <data>/desktop/bundle/, the copy an update kept under <data>/desktop/rollback/, <data>/desktop/
            appimage.json, and ai.jasonharness.desktop there (jason install's record of the profiles it created).
            --keep-application keeps all of it.
          - with the data kept: run/start-owed.json, the note that a start is owed, since with this
            installation taken off none is; and desktop/machine-setup.json, the record of the set-up Jason
            Desktop's start ran, since what it set up is taken off and the next start sets the machine up
            again.
          - last, after the data directory: the application. macOS: the Jason.app it is inside, deleted.
            Windows: a waiter is started, which once this command has ended runs the application's own
            uninstaller silently on its folder, so the folder goes after this ends; the report names the
            waiter's process. Once that process has ended, and only if Jason is then still listed in
            Installed apps in Settings, uninstall Jason there; do not start that uninstall before then,
            since two at once can leave part of the folder behind.
            --keep-application leaves it: the Windows uninstaller in Installed apps calls this so, and removes
            the folder itself next. --keep-application with --purge-data is refused (uninstall_refused).
            macOS: the bundle is deleted only where its Contents/Info.plist names ai.jasonharness.desktop. If
            macOS refuses the delete, the report says what is left and the verb exits 1: move the bundle it
            names, wherever it is, to the Trash. Linux: the AppImage file, deleted; from the deb nothing here,
            the package having gone first.
        Where no system's places are known, the jason the application carries refuses
        (uninstall_carried_by_desktop) before anything is read or removed, and the message names the way the
        system removes the application.
        This account's, not this installation's: the logon registration, the notification registration and the
        agent harnesses. Uninstalling a second installation on one account takes the first one's registrations
        and harness skills too. The jason: link goes only where its command names this executable.

        Exit codes: 0 when everything it set out to remove is gone. 1 when it did not get there: it refused
        (a runtime that would not stop), or something it set out to remove is still there (a record it cannot
        read, a file it could not remove), or it stopped part-way. Every step that did happen is listed
        either way — in the report, or on stderr where the report itself could not be written.
        """;

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var command = new Command("uninstall", "Take this installation off the machine, by receipt.")
        {
            Description = "Take this installation off the machine, by receipt."
                + Environment.NewLine + Environment.NewLine + Boundaries,
        };

        var human = VerbOptions.Human();
        var dryRun = new Option<bool>("--dry-run") { Description = "Print exactly what would be removed, and change nothing." };
        var purgeData = new Option<bool>("--purge-data") { Description = "Also remove the data directory. Without it, it is kept and the verb says where." };
        var yes = new Option<bool>("--yes") { Description = "Do not ask before removing the data directory." };
        var force = new Option<bool>("--force") { Description = "Remove a recorded file whose bytes you have since changed. Without it such a file is reported and kept." };
        var keepApplication = new Option<bool>("--keep-application") { Description = "Run as the jason Jason Desktop carries: leave the application's own folder, which the Windows uninstaller that runs this removes next." };
        var waitFor = new Option<int?>("--wait-for") { Description = "Run as the jason Jason Desktop carries: wait up to two minutes for this process to end before anything is read, as the application's Uninstall Jason item asks before it quits." };

        foreach (var option in new Option[] { human, dryRun, purgeData, yes, force, keepApplication, waitFor })
        {
            command.Options.Add(option);
        }

        command.SetAction((parse, cancellationToken) => RunAsync(
            env,
            new UninstallOptions(
                parse.GetValue(human),
                parse.GetValue(dryRun),
                parse.GetValue(purgeData),
                parse.GetValue(yes),
                parse.GetValue(force),
                KeepApplication: parse.GetValue(keepApplication),
                WaitFor: parse.GetValue(waitFor)),
            cancellationToken));

        return command;
    }

    /// <summary>
    /// The verb itself. Public, and taking its options as a record, for the reason <c>RuntimeStopCommand</c>
    /// is: a test has to shorten the wait for a runtime to go, and the repository has exactly one friend
    /// declaration, for a test that could not be written any other way. This one can.
    /// </summary>
    public static async Task<int> RunAsync(CliEnvironment env, UninstallOptions options, CancellationToken cancellationToken)
    {
        // Before anything at all, a dry run included: the executable this would delete last is the desktop
        // application's, and a plan that named it under what is removed would be a plan nobody should follow.
        var carried = CarriedExecutable.Of(env);
        if (carried is not null && env.Setup is null)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.UninstallCarriedByDesktop,
                $"This jason is the one the Jason desktop application carries, at {carried.Executable}, so `jason uninstall` "
                + $"would delete it out of the application. Nothing was read or removed. {RemovingTheApplication}"
                + $"The data directory at '{env.Paths.Root}' is kept either way.");
        }

        // Before anything is read, let alone removed. The seam that touches the machine is the one an
        // environment can forget to name, and a destructive verb that treated "nobody named one" as "there is
        // nothing to do" would report a clean uninstall it never performed.
        if (env.Removes is null)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.RemoverUnsupported,
                "This environment does not remove anything from this machine, so nothing was removed. "
                + $"The data directory at '{env.Paths.Root}' is untouched, as it is unless --purge-data says otherwise.");
        }

        // Before anything is read. --keep-application is the Windows uninstaller's own call, made from inside the application's
        // start, and it skips the look for an open window; a purge never runs from inside the application.
        if (options.KeepApplication && options.PurgeData)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.UninstallRefused,
                "--keep-application and --purge-data together were refused, and nothing has been read or removed: "
                + "--keep-application is the call the Windows uninstaller makes from inside the application, and the data "
                + "directory is never purged from there. Quit Jason, then run `jason uninstall --purge-data` in a terminal.");
        }

        // Before anything is read, let alone removed. In the shape a person is reading, --purge-data asks;
        // in the shape a script reads, there is nobody to ask, and a verb that blocked on a question nobody
        // could see would hang for ever. So the word has to be said in advance, and this refuses with the
        // whole installation still in place rather than with everything but the data gone.
        // A dry run is not asked: it deletes nothing, so there is nothing to have said in advance.
        if (options.PurgeData && !options.Yes && !options.Human && !options.DryRun)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.UninstallRefused,
                $"--purge-data deletes what Jason keeps in '{env.Paths.Root}'. Nothing has been removed. "
                + "Add --yes to say so in advance, or run with --human to be asked.");
        }

        // And a data directory that is not one is refused before anything is read. JASON_DATA_DIR may name any
        // directory at all, and the purge used to delete the one it named, recursively, whatever it was. An AppImage's jason
        // runs as a copy under this data directory's desktop/bundle, which is the data directory's own and goes with it, so
        // that copy is no installation the data directory holds.
        var running = env.InstallPath ?? SelfExecutable.InstalledImage;
        var ownCopy = running is not null && env.Setup is { Platform: SetupSystem.Linux } && !SetupPlaces.CarriedByDeb(running)
            && string.Equals(Path.GetFullPath(Update.AppImageCopy.BundlesOf(running)), Path.GetFullPath(Update.AppImageCopy.BundlesUnder(env.Paths.Root)), StringComparison.Ordinal);
        if (options.PurgeData
            && DataDirectoryBelt.Refusal(
                env.Paths.Root,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.GetTempPath(),
                ownCopy ? null : Path.GetDirectoryName(running ?? string.Empty)) is { } refusal)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.UninstallRefused,
                $"--purge-data was refused and nothing has been removed: {refusal} Point JASON_DATA_DIR at the data "
                + "directory itself, or run this without --purge-data to keep it.");
        }

        DesktopPlan? desktop = null;
        StartWait startWait;
        if (carried is not null && env.Setup is { } places)
        {
            // The plan is read after the application that asked has quit, so it is the machine as the uninstall finds it.
            await WaitForAsync(env, options, cancellationToken).ConfigureAwait(false);
            desktop = DesktopRemoval.Read(env, carried, places, options.KeepApplication);
            if (DesktopOpen(env, desktop, places, options) is { } open)
            {
                return Refuse(env, options.Human, CliErrors.DesktopRunning, open);
            }

            // A refusal that needs no waiting is said first; then the wait for a runtime still starting; then the look the
            // application's uninstaller makes, at what the wait left.
            (startWait, var starting) = await StartWaitAsync(env, options, desktop.Demo, cancellationToken).ConfigureAwait(false);
            if (starting is not null)
            {
                return Refuse(env, options.Human, CliErrors.JasonRunning, starting);
            }

            if (OthersRunning(env, carried, desktop, options) is { } others)
            {
                return Refuse(env, options.Human, CliErrors.JasonRunning, others);
            }
        }
        else
        {
            (startWait, var starting) = await StartWaitAsync(env, options, [], cancellationToken).ConfigureAwait(false);
            if (starting is not null)
            {
                return Refuse(env, options.Human, CliErrors.JasonRunning, starting);
            }
        }

        // Read after the wait, so a runtime that was starting and has come up is the plan's runtime, stopped as any is.
        var plan = UninstallReader.Read(env, options.PurgeData, desktop);

        // A person is shown the plan before a byte of it is acted on, because a plan nobody saw is one
        // nobody could have stopped. A caller reading the machine shape gets one document at the end
        // instead, carrying the plan and what became of it: two documents on one stdout is worse than
        // late, and a caller that wants to look before anything happens has --dry-run for exactly that.
        if (options.Human)
        {
            Describe(env, plan, options);
        }

        // The warm-up below is the one line here no unit test holds: the suite runs in a test host whose
        // assemblies never move, so taking it out stays green. CI's step that uninstalls the published executable
        // from its own image is what fails without it -- measured, by taking it out.
        var report = options.DryRun
            ? new UninstallReport(plan, [], ["--dry-run: nothing was removed."], [], null, null)
            : await UninstallRunner
                .RunAsync(
                    env,
                    plan,
                    options,
                    cancellationToken,
                    beforeTheImageGoes: soFar => Report(env with { Out = TextWriter.Null, Error = TextWriter.Null }, soFar, options),
                    startWait: startWait)
                .ConfigureAwait(false);

        return Reported(env, report, options) && report.Completed ? ExitCodes.Success : ExitCodes.ApiError;
    }

    /// <summary>
    /// The report, or — where it cannot be written — every line of it on stderr, in words, and false.
    /// </summary>
    /// <remarks>
    /// By the time the report is written this verb has removed things. A report that failed to render used to
    /// leave the process with one sentence about a type initializer and nothing about what was already gone;
    /// the lines below need nothing a published build has not long since loaded, so they are what a caller gets
    /// instead. And the exit code is 1, because the document a script reads is not on stdout.
    /// </remarks>
    private static bool Reported(CliEnvironment env, UninstallReport report, UninstallOptions options)
    {
        try
        {
            Report(env, report, options);
            return true;
        }
        catch (Exception unexpected) when (unexpected is not OperationCanceledException)
        {
            env.Error.WriteLine($"The report could not be written: {Causes.Line(unexpected)}");

            env.Error.WriteLine("What this uninstall did:");
            foreach (var line in report.Done)
            {
                env.Error.WriteLine($"  done: {line}");
            }

            foreach (var line in report.Kept)
            {
                env.Error.WriteLine($"  kept: {line}");
            }

            foreach (var line in report.Problems.Append(report.Refusal).OfType<string>())
            {
                env.Error.WriteLine($"  problem: {line}");
            }

            return false;
        }
    }

    /// <summary>What happened, in the shape the caller asked for.</summary>
    private static void Report(CliEnvironment env, UninstallReport report, UninstallOptions options)
    {
        if (!options.Human)
        {
            env.Out.WriteLine(JsonSerializer.Serialize(report, JasonJson.Options));
            return;
        }

        foreach (var line in report.Done)
        {
            env.Out.WriteLine($"  {line}");
        }

        foreach (var line in report.Kept)
        {
            env.Out.WriteLine($"  {line}");
        }

        foreach (var problem in report.Problems)
        {
            env.Error.WriteLine(problem);
        }

        if (report.Refusal is { } refusal)
        {
            env.Error.WriteLine(refusal);
        }
    }

    /// <summary>
    /// The plan, in the shape the caller asked for: the compact JSON an agent reads, or the lines a person
    /// does. Both name every root, including the ones nothing may guess at.
    /// </summary>
    private static void Describe(CliEnvironment env, UninstallPlan plan, UninstallOptions options)
    {
        if (!options.Human)
        {
            env.Out.WriteLine(JsonSerializer.Serialize(plan, JasonJson.Options));
            return;
        }

        env.Out.WriteLine(options.DryRun ? "This would be removed:" : "Removing:");

        if (plan.AutostartRegistered)
        {
            env.Out.WriteLine("  the logon registration" + (plan.AutostartArtifact is { } document ? $" ({document})" : string.Empty));
        }

        // Nothing reads the task 0.1.0 registered before the removal does, so the plan names it conditionally.
        if (RuntimeAutostartCommands.Registrar(env).Platform is Autostart.AutostartPlatform.Windows)
        {
            env.Out.WriteLine($"  a logon task named {Autostart.AutostartArtifacts.LegacyTaskName}, if an earlier version registered one for this account");
        }

        if (plan.RuntimePid is { } pid)
        {
            env.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  the running runtime, pid {pid}"));
        }

        foreach (var key in plan.NotificationKeys ?? [])
        {
            env.Out.WriteLine($@"  the notification registration at HKCU\{key}");
        }

        if (plan.NotificationKeysUnreadable is { } unreadable)
        {
            env.Out.WriteLine($"  the notification registration, which could not be read: {unreadable}");
        }

        if (plan.Link is { Fate: LinkRegistrationFate.Removed })
        {
            env.Out.WriteLine($@"  the jason: link registration at HKCU\{Jason.Contracts.Notifications.LinkRegistration.Key}");
        }

        if (plan.LinkUnreadable is { } linkUnreadable)
        {
            env.Out.WriteLine($"  the jason: link registration, which could not be read: {linkUnreadable}");
        }

        foreach (var root in plan.Roots)
        {
            env.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {root.Files.Count(file => file.Present && !file.Edited)} file(s) of {string.Join(", ", root.Packs)} in {root.Root}"));
        }

        if (plan.PathEntry is { Ours: true } entry)
        {
            env.Out.WriteLine($"  '{entry.Directory}' off the PATH");
        }

        if (plan.Desktop is { } desktop)
        {
            DescribeDesktop(env, desktop);
        }

        if (plan.Executable is { } executable)
        {
            env.Out.WriteLine($"  the executable at {executable}");
        }
        else if (plan.LeftTo is null && plan.Desktop is null)
        {
            // Said here as well as in the report, because a dry run never reaches the report: the plan is the
            // whole of what a person sees, and a list with no executable in it reads as one that forgot. It sat
            // under the unpacked libraries' branch, so a build that had unpacked none printed it beside the
            // executable it had just named.
            env.Out.WriteLine(
                "  no executable: this Jason is not a published single file - it is running through `dotnet`, or "
                + "from a build's own output - so there is no one file to remove. The build it runs from is where "
                + "it lives.");
        }

        foreach (var notice in plan.Notices ?? [])
        {
            env.Out.WriteLine(Path.GetFileName(notice) == ReleaseAssets.NoticeFileName("LICENSE")
                ? $"  the licence notice at {notice}"
                : $"  the third-party notices at {notice}");
        }

        if (plan.PreviousExecutable is { } previous)
        {
            env.Out.WriteLine($"  the executable an earlier install replaced, at {previous}");
        }

        if (plan.ExtractedLibraries is { } extracted)
        {
            env.Out.WriteLine($"  the native libraries it unpacked, at {extracted}");
        }

        foreach (var other in plan.OtherExtractedLibraries ?? [])
        {
            env.Out.WriteLine($"  the native libraries another build of this program unpacked, at {other}");
        }

        if (plan.OtherExtractedLibrariesLeft is { } left)
        {
            env.Out.WriteLine($"  NOT touched: {left}");
        }

        foreach (var elsewhere in plan.ExtractedLibrariesElsewhere ?? [])
        {
            env.Out.WriteLine($"  NOT touched: {UninstallRunner.LeftElsewhere(elsewhere)}");
        }

        if (plan.Link is { Fate: LinkRegistrationFate.Kept } kept)
        {
            env.Out.WriteLine($@"  NOT touched: the jason: link registration at HKCU\{Jason.Contracts.Notifications.LinkRegistration.Key}, which holds a command other than this installation's: {kept.Command}");
        }
        else if (plan.Link is { Fate: LinkRegistrationFate.Untouched })
        {
            env.Out.WriteLine($@"  NOT touched: the jason: link registration at HKCU\{Jason.Contracts.Notifications.LinkRegistration.Key}, which is another program's");
        }

        foreach (var unknown in plan.Unknown)
        {
            env.Out.WriteLine($"  NOT touched: {unknown.Root} — {unknown.Reason}");
        }

        if (plan.LeftTo is { } owner)
        {
            // The runtime is stopped by this verb whoever installed it, and before the manager is asked: a runtime left
            // running from a file the manager then deletes is how a machine ends up with neither.
            env.Out.WriteLine($"Left to {owner.Manager}, which installed them, and not removed here: {owner.Executable}, the directory it is in, and the PATH entry that reaches it.");
            env.Out.WriteLine($"  This uninstall stops the runtime first, as it always does; then `{owner.UninstallCommand}` takes them off.");
        }

        if (plan.Edited > 0)
        {
            env.Out.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  kept: {plan.Edited} file(s) whose contents are no longer what was installed. --force removes them."));
        }

        env.Out.WriteLine(plan.PurgesData
            ? $"  what Jason keeps in the data directory at {plan.DataDirectory}, and the directory once nothing else is in it"
            : $"The data directory at {plan.DataDirectory} is kept. --purge-data removes it.");
    }

    /// <summary>The application's own lines of the plan, for a person.</summary>
    private static void DescribeDesktop(CliEnvironment env, DesktopPlan desktop)
    {
        foreach (var item in desktop.OpenAtLogin)
        {
            env.Out.WriteLine($"  Open Jason at Login: {item}");
        }

        if (desktop.DesktopLink is { } link)
        {
            env.Out.WriteLine($"  the jason-desktop link registration at {link}");
        }

        foreach (var file in desktop.CommandFiles)
        {
            env.Out.WriteLine($"  the terminal command {file}");
        }

        env.Out.WriteLine($"  the PATH entry or profile lines for {desktop.CommandFolder}, where that folder holds nothing but Jason");
        if (desktop.CommandLineInstall is { } old)
        {
            env.Out.WriteLine($"  the command-line install at {old}, with its notice files, its PATH entry and its folder");
        }

        foreach (var folder in desktop.Demo)
        {
            env.Out.WriteLine($"  an earlier version's demo folder, {folder}");
        }

        foreach (var cache in desktop.Caches)
        {
            env.Out.WriteLine($"  the updater's cache, {cache}");
        }

        foreach (var place in desktop.ApplicationState)
        {
            env.Out.WriteLine($"  the application's own state, {place}, just before the application");
        }

        foreach (var line in desktop.Left)
        {
            env.Out.WriteLine($"  NOT touched: {line}");
        }

        env.Out.WriteLine(desktop.ApplicationFate switch
        {
            ApplicationFates.Deleted => $"  last, the application, {desktop.Application}",
            ApplicationFates.AfterExit => $"  last, the application, {desktop.Application}: its own uninstaller removes it once this command has ended",
            ApplicationFates.Kept => $"  NOT touched: the application, {desktop.Application}, which the uninstaller that asked for this removes next",
            ApplicationFates.Purged => $"  first, once the runtime has stopped: the package {DesktopRemoval.DebPackage}, {desktop.Application}, taken off by apt, which asks for an administrator's password",
            _ => "  no application: this jason is not inside one",
        });
    }

    /// <summary>How long <c>--wait-for</c> waits for the application to quit.</summary>
    private static readonly TimeSpan QuitWait = TimeSpan.FromMinutes(2);

    /// <summary>Waits, up to <see cref="QuitWait"/>, for the process <c>--wait-for</c> names to end: the application that asked.</summary>
    private static async Task WaitForAsync(CliEnvironment env, UninstallOptions options, CancellationToken cancellationToken)
    {
        if (options.WaitFor is { } pid)
        {
            var clock = env.Clock ?? TimeProvider.System;
            var processes = env.Processes ?? Process.RuntimeProcessControl.Instance;
            var until = clock.GetUtcNow() + QuitWait;
            while (processes.IsRunning(pid) && clock.GetUtcNow() < until)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), clock, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Windows, the terminal door: why the application's uninstaller would refuse the folder, or null. Another process
    /// running the carried jason ends that uninstaller with nothing removed, after this verb has ended and said the folder
    /// goes, so it is refused here first, naming each. A dry run removes nothing and is never refused for it.
    /// </summary>
    private static string? OthersRunning(CliEnvironment env, CarriedExecutable carried, DesktopPlan desktop, UninstallOptions options)
    {
        if (options.DryRun || desktop.ApplicationFate != ApplicationFates.AfterExit)
        {
            return null;
        }

        var others = DesktopRemoval.OthersRunningTheCarriedJason(env, carried, desktop);
        return others.Count == 0
            ? null
            : StartInFlight.JasonRunning(
                env,
                carried.Executable,
                $"Another jason runs from the application's folder ({StartInFlight.Processes(others)}), and the application's uninstaller "
                + "removes nothing while one does, so nothing was removed.",
                afterTheStop: false);
    }

    /// <summary>
    /// The wait for a runtime of this installation that is still starting (<see cref="StartInFlight"/>), and the refusal it
    /// ends with where one runs on at its bound, before anything is removed, the logon registration included. A dry run
    /// removes nothing and never waits; nor does the Windows uninstaller's own call, which looks at the folder itself first;
    /// nor does Windows at all, where <see cref="OthersRunning"/> refuses such a process at once, as it always has.
    /// </summary>
    private static async Task<(StartWait Outcome, string? Refusal)> StartWaitAsync(
        CliEnvironment env,
        UninstallOptions options,
        IReadOnlyList<string> demo,
        CancellationToken cancellationToken)
    {
        if (options.DryRun || options.KeepApplication || StartInFlight.Windows(env) || StartInFlight.Image(env) is not { } image)
        {
            return (StartWait.None, null);
        }

        var (outcome, left) = await StartInFlight.WaitAsync(env, image, demo, cancellationToken).ConfigureAwait(false);
        return outcome is StartWait.Refused
            ? (outcome, StartInFlight.JasonRunning(
                env,
                image,
                $"Another jason runs from this installation ({StartInFlight.Processes(left)}), and no runtime of {env.Paths.Root} "
                + $"answered within {StartInFlight.Seconds(StartInFlight.Bound)}, so nothing was removed.",
                afterTheStop: false))
            : (outcome, null);
    }

    /// <summary>
    /// Why the application cannot be removed now, or null: Jason Desktop open. A dry run removes nothing and is never refused
    /// for it, and the Windows uninstaller's own call (<c>--keep-application</c>) is made by the application's start it
    /// waits on.
    /// </summary>
    private static string? DesktopOpen(CliEnvironment env, DesktopPlan desktop, SetupPlaces places, UninstallOptions options)
    {
        if (options.DryRun || desktop.Application is not { } application || desktop.ApplicationFate == ApplicationFates.Kept)
        {
            return null;
        }

        var running = env.Removes!.RunningFrom(DesktopRemoval.ApplicationExecutable(application, places.Platform));
        return running.Count == 0
            ? null
            : $"Jason Desktop is open (process {string.Join(", ", running.Select(id => id.ToString(CultureInfo.InvariantCulture)))}), and this would remove it from under itself. "
              + (places.Platform switch
              {
                  SetupSystem.Windows => "Quit it from its icon in the notification area",
                  SetupSystem.Linux => "Quit it from its icon in the tray, or close its window where your desktop shows no tray",
                  _ => "Quit it from the Jason menu",
              })
              + ", then run this again. Nothing was removed; the data directory is kept either way unless --purge-data says otherwise.";
    }

    /// <summary>
    /// How the application is removed where the refusal that names it is reached: only where no system's places are known, and
    /// on a real machine they are known on Windows, macOS and Linux (<see cref="SetupPlaces.ForThisMachine"/>), where this verb
    /// is the application's one uninstall. So the sentence names no system's own way.
    /// </summary>
    internal const string RemovingTheApplication =
        "Quit it, then remove it the way this system removes an application.";

    /// <summary>
    /// The one shape a refusal takes: the error envelope on stdout by default, a sentence on stderr under
    /// <c>--human</c>, and exit 1 — understood, and declined.
    /// </summary>
    /// <remarks>
    /// On stderr for a person, as every other refusal and problem of this verb's report is: the up-front
    /// refusals printed their sentence on stdout, so the same verb put a refusal in two places depending on how
    /// early it came.
    /// </remarks>
    private static int Refuse(CliEnvironment env, bool human, string code, string message)
    {
        if (human)
        {
            env.Error.WriteLine(message);
        }
        else
        {
            env.Out.WriteLine(CliErrors.Serialize(code, message, retryable: false));
        }

        return ExitCodes.ApiError;
    }
}

/// <summary>What was asked of this verb, as parsed.</summary>
/// <param name="PurgeData">The explicit word for the data directory. Nothing else stands for it.</param>
/// <param name="Force">Remove a recorded file whose bytes no longer match the record's digest.</param>
/// <param name="StopTimeout">How long the runtime is given to go. Null is the stop verb's own wait.</param>
/// <param name="StopPoll">How often it is asked. Null is the stop verb's own interval.</param>
/// <param name="KeepApplication">The application's own folder is left: the Windows uninstaller that asked removes it next.</param>
/// <param name="WaitFor">A process to wait for before anything is read: the application that started this and quits.</param>
public sealed record UninstallOptions(
    bool Human,
    bool DryRun,
    bool PurgeData,
    bool Yes,
    bool Force,
    TimeSpan? StopTimeout = null,
    TimeSpan? StopPoll = null,
    bool KeepApplication = false,
    int? WaitFor = null);
