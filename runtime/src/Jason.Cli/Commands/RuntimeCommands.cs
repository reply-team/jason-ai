using System.CommandLine;

namespace Jason.Cli.Commands;

/// <summary>The <c>runtime</c> verb group: everything that is about the local runtime process itself.</summary>
public static class RuntimeCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var runtime = new Command("runtime", "Inspect and control the local runtime process.");

        var status = new Command("status", "Show whether the runtime is reachable and what it reports about itself.");
        var human = new Option<bool>("--human") { Description = "Render for people instead of printing the JSON response." };
        status.Options.Add(human);
        status.SetAction((parseResult, cancellationToken) =>
        {
            // These verbs ask the runtime about itself and write nothing, so no claim travels with them —
            // but a malformed claim is still refused here, so --actor behaves the same on every verb.
            ActorOption.Parse(parseResult.GetValue(actor));
            return RuntimeStatusCommand.RunAsync(env, parseResult.GetValue(human), cancellationToken);
        });
        runtime.Subcommands.Add(status);

        return runtime;
    }
}
