using System.CommandLine;
using System.Text.Json;
using Jason.Cli.Commands;
using Jason.Cli.Human;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Status;

/// <summary>
/// <c>jason status</c>: can this installation start work? One question, one answer per check, and a boolean an
/// agent branches on.
/// </summary>
/// <remarks>
/// <para>
/// The one command in this CLI that is not one-to-one with an API operation, and it cannot be: half of what it
/// reports is not the runtime's to know — an executable on PATH, another vendor's CLI, files in a folder in
/// the operator's home directory. No API operation can answer "am I ready to work?", because the runtime is
/// not the authority on the machine it runs on.
/// </para>
/// <para>
/// It exits 0 when every required check passed and 1 when one did not, and <b>never 3</b>. Exit 3 means "I
/// could not ask the runtime", and this is the verb whose whole job is to answer when the runtime cannot be
/// asked. The exception is published where the exit-code table lives and in this command's own help, and the
/// body carries <c>ready</c> so that a caller reading it never has to know about any of this.
/// </para>
/// </remarks>
public static class StatusCommand
{
    /// <summary>
    /// What a check is allowed to be. Printed in help because an exit code means nothing until "required" is
    /// defined, and because a person reading a failure needs to know whether it is one.
    /// </summary>
    private const string Partition =
        """
        Required — a failure exits 1:   the runtime answers, and no update stopped part-way from its swap on
                                        (its repair is jason update apply) | the running runtime is the build
                                        this CLI is | migrations are applied | the plugin registry is alive
                                        (zero plugins is alive) | role skills are present for every role Jason
                                        ships (a role of your own is reported, never failed), named after their
                                        directories and within the runtime's live cap | 'jason' resolves on
                                        PATH, in this shell or in every shell started from now on
        Optional — absent never fails:  no update is waiting to be resumed | a provider plugin is installed |
                                        each plugin package this release publishes is the one installed |
                                        every plugin installed is available | a plugin is granted a journal
                                        kind | every plugin is granted something of each capability it
                                        asks for | a route is configured | a binding names an account | a
                                        provider CLI you name answers | the skill packs are deployed to an agent
                                        harness, and the files their record names are there | every enabled
                                        profile names an agent host detected on this machine | autostart is
                                        registered | notifications reach you | a UI is installed, and the
                                        default one is this release's | the harness root can take the first
                                        project or campaign

        The path check is ok in two cases. 'jason' resolves in this shell; or it does not, and this account
        keeps its directory for shells not yet started — its Path value on Windows, on macOS and Linux the one
        profile a login shell reads (~/.zprofile for zsh; for bash the first of ~/.bash_profile, ~/.bash_login
        and ~/.profile that exists; ~/.profile otherwise). The second case says so, and names the full path to
        type. It is an old shell, not a broken installation — but everything that shell started is as old:
        an agent host started before the install, and every command it runs, still fails a bare 'jason' while
        ready is true, until the host is restarted. Where neither holds the check fails, and its repair is a
        line for this machine's own shell — PowerShell on Windows, sh elsewhere — the same line the installer
        writes, into the same files — for bash and zsh, ~/.bashrc or ~/.zshrc as well, which a terminal opened
        without a login reads — and which changes nothing when typed twice. Where Homebrew or winget
        installed Jason, 'jason' reached through the manager's link or its own directory is ok, and no line
        is ever offered: that entry is the manager's.

        On a release, the role skills check compares the ref their record names with the runtime's own: skills
        a default install took at another tag fail it, and its repair keeps the source they came from; skills
        at the tag this build installed pass while the runtime waits to be restarted into it; and a deployment
        made with --ref passes, and the check says how to move it. harness_skills compares the harness the same
        way with this build's own ref, and its repair, 'jason skills update', is yours to run; a pack its record
        pins passes there too, with the line that moves it. A development version, and a directory read where
        it stands, compare nothing. harness_skills reads the files a record names, not the record alone: a pack
        whose files are gone fails it, and the same repair writes them again, from the source and at the ref
        the record names, and keeps a file you edited.

        An update that stopped before its swap (a download that failed, an asset that failed its digest) has
        replaced nothing: the runtime check is the runtime's own, and the update check names the update and
        the apply that resumes it. From the swap on the runtime check fails, and its repair is the apply,
        never 'jason runtime start': a runtime started any other way would run outside the update and leave
        its last steps undone, so 'jason runtime start' refuses then, and so does the start a sign-in
        registration makes. An update ledger that cannot be read fails the runtime check and names the file.

        The build check compares the version the runtime reports with this CLI's own, as whole strings: a
        runtime left running across an upgrade that replaced the executable fails it, and its repair is
        'jason runtime restart'. A runtime that did not answer is not asked.

        The package check compares each plugin package this runtime's release publishes with the package of
        that id installed, by content digest. An earlier release's package fails it, and since no command
        installs a package, the fact names the lines that place this release's; one built or edited here
        passes, and says so. A development build compares with its own tree, and the fact names the build.

        The availability check names each plugin 'jason plugin list' reports unavailable, with its problem codes
        and never their text. Each is about this machine rather than the package — most often a program the
        plugin runs is not where the runtime looks. Work routed to such a plugin fails at the claim, and a
        notification plugin delivers nothing; nothing else stops, so the check never decides readiness.
        'jason plugin list' says what each problem is, and 'jason plugin reload' reads the plugins again once
        it is repaired.

        The subscriptions check names each plugin the settings grant journal kinds to, with those kinds and
        never more than their names; none granted is absent. A kind no plugin handles is an ordinary
        installation, so the check never decides readiness. 'jason plugin list' says which kinds each plugin
        declares and which it is granted.

        The grants check names each plugin that asks for a capability and is granted nothing of it, by
        its id and the capability's key under Plugins:Grants, and never what the manifest asks for. Such a
        plugin runs without what it is not granted, and nothing else stops, so the check never decides
        readiness. 'jason plugin list --human' says what each asks for, the key that grants it and the settings
        file, and 'jason plugin reload' reads the grant.

        The provider CLI is only checked when you name it: --provider-cli <program> runs '<program> --version'
        with a ten-second bound and reports whether it answered. The name is found the way Jason finds every
        program it starts by name: on Windows an .exe or a .com first, else npm's .cmd shim, run as the
        interpreter and script it names, and never a script only a shell could run. The check says which file
        it resolved to, and, when it looked the name up on a PATH, whose: the CLI's PATH; the runtime's PATH
        may differ, see the install page. Which provider you use is yours, so this build knows no vendor's
        command by heart. With JASON_CONFINED set, only a bare name is run, found in a fully qualified entry
        of PATH; a path is not run. Relative and empty entries are skipped, so a working folder is never
        searched by name, and the check says what was not run: name a program on the search path, or run
        the check in a terminal.

        In the JSON, the role_skills check alone carries details: the deployment its record names, as fields
        a program reads instead of the fact — ref, source, commit (empty where the record names none, a
        directory read where it stood) and roles_taught. roles_taught, how many roles the runtime reports
        it teaches, is left out where the runtime did not answer or reported that it could not read its
        role root. With no record, or one that cannot be read, there are no details. No other check
        carries any. --human prints the facts alone.

        Below the checks, --human prints where this installation's parts are: its role skills, plugins and
        custom UI folders, and its settings file, saying whether that file is also where plugin secrets are
        kept. The JSON carries the same under setup, composed by this CLI from its own data directory, so it
        is there whether or not the runtime answers, and it never decides ready.

        Exit codes: 0 when every required check passed, 1 when one did not. Never 3: "the runtime did not
        answer" is this command's answer rather than a reason it has none.
        """;

    public static Command Build(CliEnvironment env)
    {
        ArgumentNullException.ThrowIfNull(env);

        var command = new Command("status", "Whether this installation can start work.")
        {
            Description = "Whether this installation can start work." + Environment.NewLine + Environment.NewLine + Partition,
        };

        var human = VerbOptions.Human();
        var providerCli = new Option<string?>("--provider-cli")
        {
            Description = "A provider CLI to check for, found the way Jason finds every program it starts by name and run as '<program> --version'; on Windows an npm-installed CLI runs as the interpreter and entry script its shim names, then --version. Named by you: this build knows no vendor's command.",
        };

        command.Options.Add(human);
        command.Options.Add(providerCli);
        command.SetAction((parse, cancellationToken) => RunAsync(env, parse.GetValue(human), parse.GetValue(providerCli), cancellationToken));
        return command;
    }

    public static async Task<int> RunAsync(CliEnvironment env, bool human, string? providerCli, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);

        var report = await StatusChecks.ComposeAsync(env, providerCli, cancellationToken).ConfigureAwait(false);

        if (human)
        {
            Render(env.Out, report);
        }
        else
        {
            env.Out.WriteLine(JsonSerializer.Serialize(report, JasonJson.Options));
        }

        return report.Ready ? ExitCodes.Success : ExitCodes.ApiError;
    }

    internal static void Render(TextWriter output, StatusReport report)
    {
        output.WriteLine(report.Ready ? "Ready." : "Not ready.");
        output.WriteLine();

        var table = new HumanTable("CHECK", "", "STATE", "WHAT WAS SEEN");
        foreach (var check in report.Checks)
        {
            table.Row(check.Name, check.Required ? "required" : "optional", Word(check.State), check.Fact);
        }

        output.WriteLine(table.Render());
        output.WriteLine();
        RenderSetup(output, report.Setup);

        // Each line once, in the order it was first called for. Four checks that all want the runtime
        // started want it started once, and a list that printed `jason runtime start` four times was the
        // first thing a new installation said to whoever had just installed it.
        var repairs = report.Checks
            .Where(check => check.State != CheckState.Ok && check.Fix is not null)
            .Select(check => check.Fix!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (repairs.Count == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine("To repair:");
        foreach (var repair in repairs)
        {
            output.WriteLine($"  {repair}");
        }
    }

    /// <summary>
    /// Where the parts are, one line each, the labels in one column. While the settings file holds plugin secrets too the
    /// line says so; where it holds none, it is the person's to change.
    /// </summary>
    private static void RenderSetup(TextWriter output, SetupInfo setup)
    {
        (string Label, string Value)[] lines =
        [
            ("Role skills:", setup.RoleSkillsDirectory),
            ("Plugins:", setup.PluginsDirectory),
            ("Custom UI:", setup.CustomUiDirectory),
            setup.SettingsFileHoldsSecrets
                ? ("Settings:", $"{setup.SettingsFile} (where plugin secrets are kept)")
                : ("Settings you may change:", setup.SettingsFile),
            ("Layout:", setup.Layout is { } layout ? layout.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unreadable"),
        ];

        var width = lines.Max(line => line.Label.Length) + 1;
        foreach (var (label, value) in lines)
        {
            output.WriteLine(label.PadRight(width) + value);
        }
    }

    private static string Word(CheckState state) => JsonNamingPolicy.SnakeCaseLower.ConvertName(state.ToString());
}
