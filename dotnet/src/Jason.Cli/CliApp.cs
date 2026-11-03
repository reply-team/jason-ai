using System.CommandLine;
using System.CommandLine.Parsing;
using Jason.Cli.Commands;
using Jason.Cli.Doctor;
using Jason.Cli.Logs;
using Jason.Cli.Skills;
using Jason.Cli.Status;
using Jason.Cli.Uninstall;

namespace Jason.Cli;

/// <summary>
/// Verb map of the CLI. Thin, and mostly one-to-one with API operations; parsing errors exit with 2, every
/// command action returns its own exit code.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nine names are not one-to-one with anything, and they are named here because this is where the rule is
/// written.</b> <c>status</c> is a verb rather than a noun, because half of what it reports is not the
/// runtime's to know: an executable on PATH, another vendor's CLI and whether it answers, files in a folder in
/// the operator's home directory. No API operation can answer "am I ready to work?", because the runtime is
/// not the authority on the machine it runs on.
/// </para>
/// <para>
/// <c>skills</c> and <c>update</c> are nouns with no operation behind them either. Both change this
/// installation — fetching, verifying and copying files on this machine, or replacing the executable and
/// redeploying the recorded skills — which
/// the runtime neither performs nor is asked about. <c>Operations</c> carries no <c>skills.*</c> and no
/// <c>update.*</c>, and saying the map is one-to-one with one exception was false of two more.
/// </para>
/// <para>
/// <c>uninstall</c> is the fourth, and the second that is not a noun at all. It takes this installation off
/// the machine — a logon registration, an executable, a PATH entry, the files a deployment recorded — none of
/// which the runtime performs or is asked about. <c>Operations</c> carries no <c>uninstall.*</c> and never
/// will, because there is nothing for it to stand for.
/// </para>
/// <para>
/// <c>logs</c> is the fifth. It reads the installation's log files, which the runtime writes and is never asked
/// about: the answer it exists to give is the one a person needs when the runtime will not start, and a verb
/// that asked the runtime where its logs are, or whether it is running, would fail exactly then. It stands for
/// no operation, takes no subcommands, and never answers 3 — the runtime is not asked, so it cannot fail to be
/// reached.
/// </para>
/// <para>
/// <c>doctor</c> is the sixth. It collects one installation into one file a person can read before they share
/// it, and half of that — the files, the status checks — is not the runtime's to know. Of the runtime it asks
/// only for what the runtime says about itself, its roles, profiles and routes, and for the journal and work-item pages
/// it counts, keeping their metadata and dropping their values. It stands for no operation, takes no subcommands, and never
/// answers 3: a bundle is most wanted when the runtime does not answer, so not answering is written into the
/// bundle rather than given as a reason there is none.
/// </para>
/// <para>
/// <c>ui install</c> is the seventh, and the first verb of a noun whose other verbs are operations. It changes this
/// installation as <c>skills</c> and <c>update</c> do: it places the release's UI as the default build on this
/// machine, and asks the runtime nothing. <c>ui open</c>, <c>ui tokens</c> and <c>ui revoke</c> stay operations.
/// </para>
/// <para>
/// <c>plugin install</c> is the eighth, made the same way. It places a plugin package the release publishes in the
/// plugins directory — downloaded beside the release's checksums.txt, held to its line there and to the content digest
/// the release's manifest gives — which the runtime neither performs nor is asked about, since its plugins are read
/// from disk. Of the runtime it asks only the reload <c>plugin reload</c> asks, and only when one answers; it never
/// starts one. <c>plugin list</c> and <c>plugin reload</c> stay operations.
/// </para>
/// <para>
/// <c>session open</c> is the ninth, and the noun is the third with no operation behind it. It starts the person's own
/// agent host — the one the named preference <c>agent.interactive_host</c> names — in a new terminal window at the
/// campaign's folder, with the brief <c>approval get --prompt</c> or <c>decision get --prompt</c> prints as its first
/// input. A terminal on this machine is not the runtime's to start or to know about, so the runtime is asked nothing new:
/// only what those two renderings ask, and <c>preference.list</c>. It never approves, rejects or answers; the agent it
/// starts proposes.
/// </para>
/// <para>
/// Guards hold the list to these: the verb-map tests to the four verbs, the three nouns, the <c>ui</c> group's three
/// operations and <c>install</c>, the <c>plugin</c> group's two operations and <c>install</c>, and the <c>session</c>
/// noun's one verb, <c>open</c>. A tenth has to be added in the open rather than arriving as one more verb somebody
/// thought was obviously fine.
/// </para>
/// </remarks>
public static class CliApp
{
    public static async Task<int> RunAsync(string[] args, CliEnvironment env, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(env);

        var root = BuildRootCommand(env);
        var parseResult = root.Parse(args);
        var unknown = UnknownOptions(parseResult);
        if (unknown.Count > 0 || parseResult.Errors.Count > 0)
        {
            foreach (var option in unknown)
            {
                env.Error.WriteLine($"'{option}' is not an option of 'jason {Verb(parseResult)}', or give it after -- to use it as a value.");
            }

            foreach (var error in parseResult.Errors)
            {
                env.Error.WriteLine(error.Message);
            }

            env.Error.WriteLine("Run 'jason --help' for usage.");
            return ExitCodes.Usage;
        }

        // The default handler would swallow everything an action throws and report it as one unhandled
        // failure; a bad argument the action discovers while composing its request body has to reach the
        // usage exit code instead.
        var configuration = new InvocationConfiguration
        {
            Output = env.Out,
            Error = env.Error,
            EnableDefaultExceptionHandler = false,
        };

        try
        {
            return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (UsageException usage)
        {
            env.Error.WriteLine(usage.Message);
            env.Error.WriteLine("Run 'jason --help' for usage.");
            return ExitCodes.Usage;
        }
        catch (Exception unexpected)
        {
            // What the handler we switched off used to do: report, do not print a stack trace at anyone. But
            // every cause, not only the outermost: a type initializer that fails says only that it failed, and
            // what failed is in the exception it wraps.
            env.Error.WriteLine(unexpected.Message);
            foreach (var cause in Causes.Within(unexpected))
            {
                env.Error.WriteLine($"  because {cause}");
            }

            return ExitCodes.ApiError;
        }
    }

    /// <summary>
    /// The words a positional argument took that look like options: the parser hands a word it does not know as an option
    /// to whatever argument has room for it, so a misspelt <c>--summary</c> became a campaign id and the runtime answered
    /// that no such campaign exists. Decided here, once, for every verb. A lone <c>-</c> is a value (standard input), an
    /// option's own value is never read here, and after <c>--</c> every word is a value, which is how one that starts with
    /// a dash is given.
    /// </summary>
    private static List<string> UnknownOptions(ParseResult parseResult)
    {
        var tokens = parseResult.Tokens;
        var end = tokens.Count;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Type == TokenType.DoubleDash)
            {
                end = index;
                break;
            }
        }

        var beforeTheDoubleDash = new HashSet<Token>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < end; index++)
        {
            beforeTheDoubleDash.Add(tokens[index]);
        }

        var unknown = new List<string>();
        for (CommandResult? command = parseResult.CommandResult; command is not null; command = command.Parent as CommandResult)
        {
            foreach (var argument in command.Children.OfType<ArgumentResult>())
            {
                unknown.AddRange(argument.Tokens
                    .Where(token => beforeTheDoubleDash.Contains(token) && token.Value.Length > 1 && token.Value[0] == '-')
                    .Select(token => token.Value));
            }
        }

        return unknown;
    }

    /// <summary>The verb as it was typed, from the root's subcommand down: <c>campaign list-contacts</c>.</summary>
    private static string Verb(ParseResult parseResult)
    {
        var names = new List<string>();
        for (var command = parseResult.CommandResult; command.Parent is CommandResult parent; command = parent)
        {
            names.Insert(0, command.Command.Name);
        }

        return string.Join(' ', names);
    }

    internal static RootCommand BuildRootCommand(CliEnvironment env)
    {
        var root = new RootCommand("Jason command-line interface — a client of the local Jason runtime.");

        var actor = ActorOption.Create();
        root.Options.Add(actor);
        root.Subcommands.Add(RuntimeCommands.Build(env, actor));
        root.Subcommands.Add(CampaignCommands.Build(env, actor));
        root.Subcommands.Add(ProjectCommands.Build(env, actor));
        root.Subcommands.Add(PreferenceCommands.Build(env, actor));
        root.Subcommands.Add(ContactCommands.Build(env, actor));
        root.Subcommands.Add(WorkItemCommands.Build(env, actor));
        root.Subcommands.Add(RoleCommands.Build(env, actor));
        root.Subcommands.Add(RoleNoteCommands.Build(env, actor));
        root.Subcommands.Add(ProfileCommands.Build(env, actor));
        root.Subcommands.Add(ApprovalCommands.Build(env, actor));
        root.Subcommands.Add(BlockCommands.Build(env, actor));
        root.Subcommands.Add(DecisionCommands.Build(env, actor));
        root.Subcommands.Add(ReplyCommands.Build(env, actor));
        root.Subcommands.Add(ReportCommands.Build(env, actor));
        root.Subcommands.Add(JournalCommands.Build(env, actor));
        root.Subcommands.Add(SuppressionCommands.Build(env, actor));
        root.Subcommands.Add(PluginCommands.Build(env, actor));
        root.Subcommands.Add(RouteCommands.Build(env, actor));
        root.Subcommands.Add(EventsCommands.Build(env, actor));
        root.Subcommands.Add(UiCommands.Build(env, actor));
        root.Subcommands.Add(Session.SessionCommands.Build(env, actor));
        root.Subcommands.Add(UpdateCommands.Build(env, actor));
        root.Subcommands.Add(SkillsCommands.Build(env));
        root.Subcommands.Add(StatusCommand.Build(env));
        root.Subcommands.Add(LogsCommand.Build(env));
        root.Subcommands.Add(DoctorCommand.Build(env));
        root.Subcommands.Add(UninstallCommand.Build(env));

        return root;
    }
}
