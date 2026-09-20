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

        runtime.Subcommands.Add(Verb(
            "status",
            "Show whether the runtime is reachable and what it reports about itself.",
            actor,
            (human, cancellationToken) => RuntimeStatusCommand.RunAsync(env, human, cancellationToken)));

        runtime.Subcommands.Add(Verb(
            "stop",
            "Ask the running runtime to shut down and wait until its process is gone.",
            actor,
            (human, cancellationToken) => RuntimeStopCommand.RunAsync(env, human, cancellationToken)));

        runtime.Subcommands.Add(Verb(
            "start",
            "Make sure a runtime is running, launching it in the background if none answers.",
            actor,
            (human, cancellationToken) => RuntimeStartCommand.RunAsync(env, human, cancellationToken)));

        runtime.Subcommands.Add(Verb(
            "restart",
            "Stop the running runtime, if any, and start a fresh one in the background.",
            actor,
            (human, cancellationToken) => RuntimeRestartCommand.RunAsync(env, human, cancellationToken)));

        runtime.Subcommands.Add(RuntimeAutostartCommands.Build(env, actor));

        return runtime;
    }

    private static Command Verb(string name, string description, Option<string?> actor, Func<bool, CancellationToken, Task<int>> run)
    {
        var command = new Command(name, description);
        var human = new Option<bool>("--human") { Description = "Render for people instead of printing the JSON response." };
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            // These verbs are about the process, not about business state, so no claim travels with them —
            // but a malformed claim is still refused here, so --actor behaves the same on every verb.
            ActorOption.Parse(parseResult.GetValue(actor));
            return run(parseResult.GetValue(human), cancellationToken);
        });

        return command;
    }
}
