using System.CommandLine;
using System.Text.Json;
using Jason.Cli.Commands;
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
        one JSON document (--human: lines).

        Done, in this order, each step reported:
          1. A command-line install of Jason, where one is found, has its runtime drained and stopped: the
             executable --retire names, else the one the last `jason update apply` recorded, else the
             installer's default place (~/.local/bin/jason on macOS, %LOCALAPPDATA%\Programs\jason\jason.exe
             on Windows), only where it is Jason's: named by the flag or the record, a jason-LICENSE or
             jason-THIRD-PARTY-NOTICES beside it, or on Windows that folder. Only a runtime running from that
             executable is stopped, and no runtime is started: `jason runtime start` starts one from this
             jason. Where nothing is to be retired, no runtime is asked anything. A
             runtime that will not stop leaves that install as it is.
          2. The terminal command.
             macOS: ~/.local/bin/jason, the application's own two-line sh wrapper running this jason (naming
             JASON_DATA_DIR where that is set), written over an earlier wrapper, the retired executable, or a
             link to an application's jason, and over no other file; then install.sh's two lines,
               # added by jason install
               export PATH="$HOME/.local/bin:$PATH"   (the home spelled out)
             appended to the login profile of the account's shell and to ~/.zshrc or ~/.bashrc beside it,
             each only where the line is not already a line of that file.
             Windows: one entry in this account's Path naming this jason's folder, only where it is not there,
             the value's kind kept and running programs told; and the jason and jason-desktop command files
             Jason Desktop placed in %LOCALAPPDATA%\Microsoft\WindowsApps taken back, each only where it is
             one of the application's shapes.
             A terminal already open finds neither: it runs the path the document gives as open_shell.
          3. The rest of that command-line install: its logon registration, where it names that executable and
             this data directory, registered again naming this jason; its notice files, and on Windows
             jason.previous.exe, removed. Windows: the executable removed, or, while a running process holds
             it, renamed into a jason-retired-<id> folder on its own volume and left there, with nothing
             started to wait for that process; its Path entry taken off where its folder then holds nothing
             but what install.ps1 writes; the folder once empty. macOS: the wrapper has replaced it at the
             default place; an executable elsewhere is removed, with its profile block where its folder then
             holds nothing but Jason.
          4. The harness skills, both packs, from the skills beside this jason (read where they stand, nothing
             downloaded), into the folder of every agent host found by its home folder: Claude Code (~/.claude
             -> ~/.claude/skills) and Codex (~/.codex -> ~/.agents/skills, which Copilot, Cursor and Gemini
             CLIs read too), each recorded in .jason-skills.json. A skill folder there under a name Jason ships
             that no record of Jason's names is yours: left as it is, and named.
          5. With --roles, the role skills into the data directory, as `jason skills install --roles-only`.

        Written under the data directory: nothing without --roles, so an application's first start still opens
        on the Demo; only an install being retired, which has data already, may have its runtime stopped and its
        logon registration made again.

        Callers: the Windows installer, at every install and update, silent or not, with no flags; the agent's
        install script on macOS, once the application is in place, with --roles; the application's start, where
        this last ran for an older version, with no flags; the move of a command-line install, with --retire
        naming the executable it moves from.

        The document: version, executable (this jason); command {runs (what a new terminal finds: the wrapper on
        macOS, the folder on the Path on Windows), open_shell, changed, profiles, taken_back}; skills [{root,
        hosts, written, unchanged, left_alone}]; roles (with --roles, else null); retired {executable, aside,
        removed, reregistered} or null; runtime (not_running, running, stopped, would_not_stop); done, left,
        problems (sentences); completed.

        Exit codes: 0 when everything it set out to do is done; 1 when it refused, or a step did not get there
        (problems names each); 2 for a usage error.
        """;

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var command = new Command("install", "Set this machine up for the jason Jason Desktop carries: the terminal command, the skills of every agent host found, and an older command-line install retired.")
        {
            Description = "Set this machine up for the jason Jason Desktop carries: the terminal command, the skills of every agent host found, and an older command-line install retired."
                + Environment.NewLine + Environment.NewLine + Contract,
        };

        var human = VerbOptions.Human();
        var roles = new Option<bool>("--roles") { Description = "Also deploy the role skills into the data directory, which the runtime cannot work without. Without it nothing is written under the data directory." };
        var retire = new Option<string?>("--retire") { Description = "The executable of a command-line install to retire, where it is not in the installer's default place." };

        foreach (var option in new Option[] { human, roles, retire })
        {
            command.Options.Add(option);
        }

        command.SetAction((parse, cancellationToken) => RunAsync(env, new InstallOptions(parse.GetValue(human), parse.GetValue(roles), parse.GetValue(retire)), cancellationToken));
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

        var report = await MachineSetup.RunAsync(env, carried, places, options, cancellationToken).ConfigureAwait(false);
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
