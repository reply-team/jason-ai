using System.CommandLine;
using Jason.Cli.Commands;
using Jason.Cli.Status;

namespace Jason.Cli;

/// <summary>
/// Verb map of the CLI. Thin and one-to-one with API operations, with one declared exception; parsing errors
/// exit with 2, every command action returns its own exit code.
/// </summary>
/// <remarks>
/// <para>
/// <b>The exception is <c>jason status</c>, and it is stated here because this is where the rule is written.</b>
/// Every other top-level name is a noun that an API operation stands behind. <c>status</c> is a verb and cannot
/// be a noun, because half of what it reports is not the runtime's to know: an executable on PATH, another
/// vendor's CLI and whether it answers, files in a folder in the operator's home directory. No API operation
/// can answer "am I ready to work?", because the runtime is not the authority on the machine it runs on.
/// </para>
/// <para>
/// One exception, and a guard holds it to one: a second has to be added in the open rather than arriving as
/// one more verb somebody thought was obviously fine.
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
        if (parseResult.Errors.Count > 0)
        {
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
            // What the handler we switched off used to do: report, do not print a stack trace at anyone.
            env.Error.WriteLine(unexpected.Message);
            return ExitCodes.ApiError;
        }
    }

    internal static RootCommand BuildRootCommand(CliEnvironment env)
    {
        var root = new RootCommand("Jason command-line interface — a client of the local Jason runtime.");

        var actor = ActorOption.Create();
        root.Options.Add(actor);
        root.Subcommands.Add(RuntimeCommands.Build(env, actor));
        root.Subcommands.Add(CampaignCommands.Build(env, actor));
        root.Subcommands.Add(ContactCommands.Build(env, actor));
        root.Subcommands.Add(WorkItemCommands.Build(env, actor));
        root.Subcommands.Add(RoleCommands.Build(env, actor));
        root.Subcommands.Add(RoleNoteCommands.Build(env, actor));
        root.Subcommands.Add(ProfileCommands.Build(env, actor));
        root.Subcommands.Add(ApprovalCommands.Build(env, actor));
        root.Subcommands.Add(DecisionCommands.Build(env, actor));
        root.Subcommands.Add(ReportCommands.Build(env, actor));
        root.Subcommands.Add(JournalCommands.Build(env, actor));
        root.Subcommands.Add(SuppressionCommands.Build(env, actor));
        root.Subcommands.Add(PluginCommands.Build(env, actor));
        root.Subcommands.Add(RouteCommands.Build(env, actor));
        root.Subcommands.Add(UpdateCommands.Build(env, actor));
        root.Subcommands.Add(StatusCommand.Build(env));

        return root;
    }
}
