using System.CommandLine;

namespace Jason.Cli.Commands;

/// <summary>The <c>runtime</c> verb group: everything that is about the local runtime process itself.</summary>
public static class RuntimeCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var runtime = new Command("runtime", "Inspect and control the local runtime process, and drain or resume its dispatcher.");

        runtime.Subcommands.Add(Verb(
            env,
            "status",
            "Show whether the runtime is reachable and what it reports about itself.",
            actor,
            (human, cancellationToken) => RuntimeStatusCommand.RunAsync(env, human, cancellationToken)));

        runtime.Subcommands.Add(Stop(env, actor));

        runtime.Subcommands.Add(Verb(
            env,
            "start",
            "Make sure a runtime is running, launching it in the background if none answers. Beside an update stopped part-way from its swap on, or an update ledger it cannot read, it starts nothing and says why; 'jason update apply' finishes the update.",
            actor,
            (human, cancellationToken) => RuntimeStartCommand.RunAsync(env, human, cancellationToken)));

        runtime.Subcommands.Add(Verb(
            env,
            "restart",
            "Stop the running runtime, if any, and start a fresh one in the background. Beside an update stopped part-way from its swap on, or an update ledger it cannot read, it stops and starts nothing and says why.",
            actor,
            (human, cancellationToken) => RuntimeRestartCommand.RunAsync(env, human, cancellationToken)));

        foreach (var verb in RuntimeDrainCommands.Build(env, actor))
        {
            runtime.Subcommands.Add(verb);
        }

        runtime.Subcommands.Add(RuntimeAutostartCommands.Build(env, actor));

        return runtime;
    }

    /// <summary>
    /// <c>runtime stop</c>, the one verb of the group with an option of its own: <c>--start-owed</c>, for a stop made on
    /// behalf of an install or an update, which leaves the note that a start is owed where a stop a person or an agent asks
    /// for takes it back.
    /// </summary>
    private static Command Stop(CliEnvironment env, Option<string?> actor)
    {
        var onBehalf = new Option<bool>("--start-owed")
        {
            Description = "For a stop made on behalf of an install or an update, never one a person or an agent asks for: write "
                + "run/start-owed.json in the data directory, the note that a start is owed, instead of taking it back, so "
                + "Jason Desktop starts a runtime again and jason status says one is owed. The note is written as the shutdown is "
                + "asked. Where the runtime does not stop it is taken back only if the runtime refused the shutdown and runs on, "
                + "and kept where it ended, took the shutdown and has not ended, or was sent it with no answer back, as jason "
                + "install keeps it; an older note already standing is left as it is; with no "
                + "runtime answering nothing is written. Jason "
                + "Desktop's own stop for its installer passes it.",
        };
        return Verb(
            env,
            "stop",
            "Ask the running runtime to shut down and wait until its process is gone. A stop takes back the note that a start is "
                + "owed (run/start-owed.json), which an install that stopped a runtime left; with --start-owed it writes it instead. "
                + "Where the runtime is registered to start at logon, the stop holds until the next logon, which starts it again, and "
                + "says so on stderr; jason runtime autostart disable stops that.",
            actor,
            (parseResult, human, cancellationToken) => parseResult.GetValue(onBehalf)
                ? RuntimeStopCommand.RunOnBehalfAsync(env, human, cancellationToken)
                : RuntimeStopCommand.RunAsDeliberateAsync(env, human, cancellationToken),
            onBehalf);
    }

    private static Command Verb(CliEnvironment env, string name, string description, Option<string?> actor, Func<bool, CancellationToken, Task<int>> run) =>
        Verb(env, name, description, actor, (_, human, cancellationToken) => run(human, cancellationToken));

    private static Command Verb(
        CliEnvironment env, string name, string description, Option<string?> actor, Func<ParseResult, bool, CancellationToken, Task<int>> run, params Option[] options)
    {
        var command = new Command(name, description);
        var human = new Option<bool>("--human") { Description = "Render for people instead of printing the JSON response." };
        command.Options.Add(human);
        foreach (var option in options)
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            // These verbs are about the process, not about business state, so no claim travels with them —
            // but a malformed claim is still refused here, so --actor behaves the same on every verb.
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return run(parseResult, parseResult.GetValue(human), cancellationToken);
        });

        return command;
    }
}
