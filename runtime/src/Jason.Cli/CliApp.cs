using System.CommandLine;
using Jason.Cli.Commands;

namespace Jason.Cli;

/// <summary>
/// Verb map of the CLI. Thin and one-to-one with API operations; parsing errors exit with 2, every
/// command action returns its own exit code.
/// </summary>
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

        var configuration = new InvocationConfiguration { Output = env.Out, Error = env.Error };
        return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
    }

    internal static RootCommand BuildRootCommand(CliEnvironment env)
    {
        var root = new RootCommand("Jason command-line interface — a client of the local Jason runtime.");

        var runtime = new Command("runtime", "Inspect and control the local runtime process.");
        var status = new Command("status", "Show whether the runtime is reachable and what it reports about itself.");
        var human = new Option<bool>("--human") { Description = "Render for people instead of printing the JSON response." };
        status.Options.Add(human);
        status.SetAction((parseResult, cancellationToken) => RuntimeStatusCommand.RunAsync(env, parseResult.GetValue(human), cancellationToken));
        runtime.Subcommands.Add(status);
        root.Subcommands.Add(runtime);

        return root;
    }
}
