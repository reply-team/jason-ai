using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Commands;

/// <summary>
/// <c>jason runtime drain</c> and <c>jason runtime resume</c>: the dispatcher's drain and resume, one to one with
/// <c>system.drain</c> and <c>system.resume</c>, under the noun that speaks for <c>system</c>. The routines that drain a
/// runtime before they stop it — an update, the install routine — send the same operations on their own; these verbs are
/// how a person or an agent sends them, and above all how a runtime left draining by a routine that ended part-way is
/// given back its work from the terminal.
/// </summary>
public static class RuntimeDrainCommands
{
    public static IEnumerable<Command> Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        yield return Verb(
            env,
            actor,
            "drain",
            Operations.SystemDrain,
            "Tell the runtime to claim no new work; what it is already running goes on to its end. It answers the dispatcher's "
            + "state and how many attempts are still running, and draining one already draining is the same answer. "
            + "'jason update apply' and 'jason install' send it before they stop a runtime; 'jason runtime resume' undoes it.");

        yield return Verb(
            env,
            actor,
            "resume",
            Operations.SystemResume,
            "Tell a drained runtime to claim work again. It answers the dispatcher's state and how many attempts are running, "
            + "and resuming one that is not draining is the same answer. 'jason install' sends it to a runtime it drained and "
            + "could not stop; a runtime an update or an install left draining, because it ended part-way, is resumed by this.");
    }

    private static Command Verb(CliEnvironment env, Option<string?> actor, string name, string operation, string description)
    {
        var command = new Command(name, description);
        var human = VerbOptions.Human();
        command.Options.Add(human);
        command.SetAction((parseResult, cancellationToken) =>
        {
            // About the process, not about business state: no claim travels with them, but a malformed one is still refused.
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            return OperationRunner.RunAsync(
                env, operation, RequestBody.Empty(), new RunOptions(parseResult.GetValue(human), Render), cancellationToken);
        });

        return command;
    }

    /// <summary>"The dispatcher is draining; 2 attempts running." Null for a body this version cannot read.</summary>
    internal static string? Render(string body)
    {
        DrainResponse? answer;
        try
        {
            answer = JsonSerializer.Deserialize<DrainResponse>(body, JasonJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (answer is null)
        {
            return null;
        }

        var state = JsonNamingPolicy.SnakeCaseLower.ConvertName(answer.State.ToString());
        var running = answer.RunningAttempts == 1
            ? "1 attempt running"
            : string.Create(CultureInfo.InvariantCulture, $"{answer.RunningAttempts} attempts running");
        return $"The dispatcher is {state}; {running}.";
    }
}
