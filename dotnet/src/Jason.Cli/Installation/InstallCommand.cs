using System.CommandLine;
using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Process;
using Jason.Cli.Update;
using Jason.Contracts.Json;

namespace Jason.Cli.Installation;

/// <summary>
/// <c>jason install</c>: setting this machine up for the jason Jason Desktop carries — the other half of
/// <c>jason uninstall</c>.
/// </summary>
/// <remarks>
/// <para>
/// A CLI verb rather than an API operation, and not a noun either, for <c>uninstall</c>'s reason: a terminal command, a
/// Path entry, a profile's lines and the skills in an agent host's folder are none of them the runtime's to perform or be
/// asked about. <c>Operations</c> carries no <c>install.*</c> and never will.
/// </para>
/// <para>
/// <b>Its contract is its help</b>, which the installers and the application call it by: the flags, the document's
/// members, the exit codes and which caller passes what.
/// </para>
/// </remarks>
public static class InstallCommand
{
    /// <summary>What this verb does and never does, printed in help: the contract its callers are written against.</summary>
    private static readonly string Contract =
        """
        Run by the jason Jason Desktop carries (a version.json beside the executable); any other jason is refused
        (install_not_carried), and so is a system other than macOS and Windows (install_unsupported). It is
        idempotent: a second run changes nothing and says so. It starts no process that outlives it, and prints
        one JSON document (--human: lines), always: a step that fails part-way is named under problems and the
        steps after it still run.

        Done, in this order, each step reported:
          1. A command-line install of Jason, where one is found, has its runtime drained and stopped: the
             executable --retire names, else the one the last `jason update apply` recorded, else the
             installer's default place (~/.local/bin/jason on macOS, %LOCALAPPDATA%\Programs\jason\jason.exe
             on Windows), only where it is Jason's: a jason-LICENSE or jason-THIRD-PARTY-NOTICES beside it,
             the record, or on Windows that folder. --retire takes only a regular file named jason (jason.exe
             on Windows) with one of those signs; anything else is named and left. Only a runtime running from
             that executable is stopped, and no runtime is started: `jason runtime start` starts one from this
             jason. Just before it drains and stops one it writes run/start-owed.json in the data directory,
             the moment in it, {"stopped_at":"<ISO-8601 UTC>"}: a start is owed. A runtime's own start removes
             it, and so do any stop through jason runtime stop and an uninstall that keeps the data; jason
             status names it while no runtime answers, and Jason Desktop starts one when it finds it standing;
             a run killed or timed out during the stop's wait leaves it standing. Where nothing is to be
             retired, no runtime is asked anything. A runtime that will not stop is resumed, its note taken
             back, and leaves that install as it is, and so does a runtime that does not name the executable it runs from. Where the data
             directory's layout.json names a layout newer than this jason runs (layout_newer) or cannot be
             read (layout_unreadable), no runtime is stopped, no registration moved and nothing of that
             install retired, with --roles or without.
          2. The terminal command.
             macOS: ~/.local/bin/jason, the application's own two-line sh wrapper running this jason (naming
             JASON_DATA_DIR where that is set), written over an earlier wrapper, the retired executable, or a
             link to an application's jason, and over no other file; then, only where the PATH a terminal has
             does not name ~/.local/bin already (install.sh's own test, case ":$PATH:" in *":$INSTALL_DIR:"*),
             install.sh's two lines,
               # added by jason install
               export PATH="$HOME/.local/bin:$PATH"   (the home spelled out)
             appended to the login profile of the account's shell and to ~/.zshrc or ~/.bashrc beside it,
             each only where the line is not already a line of that file. The PATH a terminal has is the one
             --terminal-path names, and without it this process's own, which is a terminal's when a script
             runs this. --terminal-path with the empty value says the login shell gave no answer: the folder
             counts as not there, and the lines are written. A home whose folder holds a line break, '"', '$',
             a backtick or '\' takes no line, as install.sh refuses it; a profile that is a link is appended
             through only to a regular file. Either is named with the line to add by hand. A home whose folder
             holds ':', which separates PATH's entries, takes no line either, since none could name it. And 0.48's
             ~/.local/bin/jason-desktop wrapper taken back, only where it is the application's shape.
             Windows: one entry in this account's Path naming this jason's folder, only where it is not there,
             the value's kind kept and running programs told (a Path of a kind that is not a string is
             refused and left); and the jason and jason-desktop command files Jason Desktop placed in
             %LOCALAPPDATA%\Microsoft\WindowsApps taken back, each only where it is one of the application's
             shapes.
             The command files go, and step 3 runs, only once the terminal command reads back as placed: the
             wrapper and its lines, or the Path entry. Otherwise the old install stays: where it sat at
             ~/.local/bin/jason the wrapper has taken its executable's place, and elsewhere it still answers.
             A terminal already open finds neither: it runs the path the document gives as open_shell.
          3. The rest of that command-line install: its logon registration, where it names that executable and
             this data directory, or no data directory (and then it names none again), registered again naming
             this jason; on macOS the file is moved and launchd loads it at the next logon, so nothing starts
             now, and until then the old job stays loaded and idle. Its notice files, and on Windows
             jason.previous.exe, removed. Windows: the executable removed, or, while a running process holds it, renamed into a jason-retired-<id> folder on its
             own volume and left there, with nothing started to wait for that process, until a later jason install,
             jason update apply --finish, move or jason uninstall finds that process ended and removes the folder; its Path entry taken
             off where its folder then holds nothing but what install.ps1 wrote there before 0.49; the folder once empty.
             macOS: the wrapper has replaced it at the default place; an executable elsewhere is removed, with
             its profile block where its folder then holds nothing but Jason.
          4. The harness skills, both packs, from the skills beside this jason (read where they stand, nothing
             downloaded), into the folder of each of two hosts found by its home folder, and of no other:
             Claude Code (~/.claude -> ~/.claude/skills) and Codex (~/.codex -> ~/.agents/skills, which
             Copilot, Cursor and Gemini CLI read too); another host takes them from
             `jason skills install --host <host>`. Each is recorded in .jason-skills.json. A skill folder
             there under a name Jason ships that no record of Jason's names is yours: left as it is, and
             named; one whose files equal this jason's, byte for byte, is one an earlier deployment wrote
             before its record, and is adopted.
          5. With --roles, the role skills into the data directory, as `jason skills install --roles-only`.

        Written under the data directory: nothing without --roles, so an application's first start still opens
        on the Demo; only an install being retired, which has data already, may have its runtime stopped, the
        note that a start is owed written, and its logon registration made again.

        Callers: the Windows installer, at every install and update, silent or not, with no flags; the agent's
        install script on macOS, once the application is in place, with --roles and no --terminal-path; the
        application's start, where this last ran for an older version, with --terminal-path naming the PATH its
        login shell printed (never the application's own PATH, which it adds places to), or the empty value
        where the shell gave none; the move of a command-line install, with --retire naming the executable it
        moves from.

        The document: version, executable (this jason); command {runs (what a new terminal finds: the wrapper on
        macOS, the folder on the Path on Windows), placed (whether a new terminal finds this jason, as read back;
        the install scripts end non-zero where it is false), open_shell, changed, profiles, taken_back}; skills [{root,
        hosts, written, unchanged, left_alone}]; roles (with --roles, else null); retired {executable, aside,
        removed, reregistered} or null; runtime (not_running, running, stopped, would_not_stop); done, left,
        problems (sentences); completed.

        One at a time with an update: where the data directory has an update folder, the setup holds the
        update's lock, <data>/update/update.lock, while it runs, and names itself beside it. While an update,
        its finish or a rollback holds it, the setup refuses with update_in_progress, having set nothing up:
        an update's own finish sets the machine up itself. While another jason install holds it, it refuses
        with install_in_progress, and so does an update begun then. The lock is the open file, which a killed
        holder lets go. A setup the holder runs as its waited child, handed the holder's nonce in
        JASON_UPDATE_LOCK, goes on under that hold instead; one handed another nonce is refused. Where there
        is no update folder, or no data directory at all, it takes no lock and makes none.

        Inside a move of a command-line install into Jason Desktop: the setup the move runs is given JASON_MOVE,
        naming the move's record. A jason install that finds it, while that record in <data>/update/move.json
        stands not done, does nothing - nothing placed, retired or stopped - says so in its document and exits 0:
        the move sets the machine up itself once the installer has returned. A JASON_MOVE naming no such record is
        ignored.

        Exit codes: 0 when everything it set out to do is done; 1 when it refused, or a step did not get there
        (problems names each); 2 for a usage error.
        """;

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var command = new Command("install", "Set this machine up for the jason Jason Desktop carries: the terminal command, the skills of Claude Code and Codex where each is found, and an older command-line install retired.")
        {
            Description = "Set this machine up for the jason Jason Desktop carries: the terminal command, the skills of Claude Code and Codex where each is found, and an older command-line install retired."
                + Environment.NewLine + Environment.NewLine + Contract,
        };

        var human = VerbOptions.Human();
        var roles = new Option<bool>("--roles") { Description = "Also deploy the role skills into the data directory, which the runtime cannot work without. Without it nothing is written under the data directory." };
        var retire = new Option<string?>("--retire") { Description = "The executable of a command-line install to retire, where it is not in the installer's default place." };
        var terminalPath = new Option<string?>("--terminal-path")
        {
            Description = "macOS: the PATH a terminal of this account has, colon-separated, as its login shell printed it; the profile lines are written only where it does not name ~/.local/bin. The empty value says the shell gave no answer, and the lines are written. Without it, this process's own PATH. Windows: read by nothing.",
        };

        foreach (var option in new Option[] { human, roles, retire, terminalPath })
        {
            command.Options.Add(option);
        }

        command.SetAction((parse, cancellationToken) => RunAsync(env, new InstallOptions(parse.GetValue(human), parse.GetValue(roles), parse.GetValue(retire), TerminalPath: parse.GetValue(terminalPath)), cancellationToken));
        return command;
    }

    /// <summary>The verb itself; public, and taking its options as a record, so a test can shorten a runtime's waits.</summary>
    public static async Task<int> RunAsync(CliEnvironment env, InstallOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(options);

        if (env.Setup is not { } places)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.InstallUnsupported,
                "`jason install` sets up macOS and Windows for Jason Desktop, and this is neither, so nothing was read or written. "
                + "On Linux, install.sh places jason, and `jason skills install` deploys the skills.");
        }

        if (CarriedExecutable.Of(env) is not { } carried)
        {
            return Refuse(
                env,
                options.Human,
                CliErrors.InstallNotCarried,
                "This jason is not the one Jason Desktop carries (no version.json beside it), so nothing was read or written. "
                + "Run the jason inside the application: on macOS Jason.app/Contents/Resources/bundle/jason, on Windows "
                + @"%LOCALAPPDATA%\Programs\jason-desktop\resources\bundle\jason.exe.");
        }

        // One at a time with an update: the update's finish runs this same setup, and the installer's own `jason install`
        // can run while it does. The lock is taken only where an update could hold it, so none is made under a data directory
        // a first start must still find without one. A setup the lock's holder runs as its waited child is handed the
        // holder's nonce, as the update's own parts are, and goes on under that hold rather than refuse; read here whether a
        // lock is taken or not, so that nothing this setup starts carries it on.
        var handed = UpdateLock.Handed(env.Variables);
        var update = new UpdatePaths(env.Paths);

        // Inside a move: the installer the move runs is setting the application up, and the move sets the machine up itself
        // once the installer has returned. Only under the move's own marker, naming its record while that stands not done.
        if (MoveMarker.Standing(env.Variables, update) is { } moving)
        {
            return InsideAMove(env, options, carried, moving);
        }
        UpdateLock? held = null;
        if (Directory.Exists(update.Root))
        {
            try
            {
                held = UpdateLock.Take(update, "the machine setup", handed, UpdateLock.Holder.Install);
            }
            catch (UpdateException refused)
            {
                return Refuse(
                    env,
                    options.Human,
                    refused.Code,
                    refused.Code == UpdateCodes.InProgress
                        ? $"Another jason is updating this installation, or finishing or rolling back an update, and holds {update.Lock}, so nothing was set up: an update's own finish sets the machine up itself. Let that one end, then run `jason install` again; `jason update status` says where the update stands."
                        : refused.Message,
                    refused.Retryable);
            }
        }

        InstallReport report;
        using (held)
        {
            report = await MachineSetup.RunAsync(env, carried, places, options, cancellationToken).ConfigureAwait(false);
        }

        if (options.Human)
        {
            foreach (var line in report.Done.Concat(report.Left))
            {
                env.Out.WriteLine($"  {line}");
            }

            foreach (var problem in report.Problems)
            {
                env.Error.WriteLine(problem);
            }
        }
        else
        {
            env.Out.WriteLine(JsonSerializer.Serialize(report, JasonJson.Options));
        }

        return report.Completed ? ExitCodes.Success : ExitCodes.ApiError;
    }

    /// <summary>
    /// The installer's own setup inside a move: nothing read, written, stopped or retired; the one document says so, exit 0.
    /// </summary>
    private static int InsideAMove(CliEnvironment env, InstallOptions options, CarriedExecutable carried, MoveRecord moving)
    {
        var left = $"The move of the command-line install at {moving.From} into Jason Desktop ran this installer and sets this machine up itself once the installer has returned, so this setup did nothing: nothing was placed, retired or stopped.";
        var report = new InstallReport(
            carried.Version,
            carried.Executable,
            null,
            [],
            null,
            null,
            DescriptorProcess.Live(env.Paths, env.Processes ?? RuntimeProcessControl.Instance) is null ? MachineSetup.RuntimeStates.NotRunning : MachineSetup.RuntimeStates.Running,
            [],
            [left],
            []);
        if (options.Human)
        {
            env.Out.WriteLine($"  {left}");
        }
        else
        {
            env.Out.WriteLine(JsonSerializer.Serialize(report, JasonJson.Options));
        }

        return ExitCodes.Success;
    }

    private static int Refuse(CliEnvironment env, bool human, string code, string message, bool retryable = false)
    {
        if (human)
        {
            env.Error.WriteLine(message);
        }
        else
        {
            env.Out.WriteLine(CliErrors.Serialize(code, message, retryable));
        }

        return ExitCodes.ApiError;
    }
}
