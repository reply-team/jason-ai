using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>preference</c> verb group: one verb per preference operation. A preference is a person's word about their
/// business — who messages are written as, how the company sounds, which agent host <c>jason session open</c> starts —
/// so <c>set</c> and <c>unset</c> name the person with <c>--actor human:&lt;name&gt;</c>, and the runtime refuses anyone
/// else. What a value must hold is the key's document's to say: this verb sends the value as it was written and checks
/// only that it is one JSON object.
/// </summary>
public static class PreferenceCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var preference = new Command("preference", "The client's named preferences — who messages are written as, how the company sounds, which agent host jason session open starts: set, unset and list them.");
        preference.Subcommands.Add(Set(env, actor));
        preference.Subcommands.Add(Unset(env, actor));
        preference.Subcommands.Add(List(env, actor));
        return preference;
    }

    private static Command Set(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "set",
            "Set a preference's value whole, as the person said it. The value is checked against the key's published schema. " + ActorOption.NoDefault);
        var key = KeyArgument();
        var value = new Option<string?>("--value") { Description = "The whole value, as a JSON object." };
        var valueFile = new RequestFileOption("--value-file")
        {
            Description = "The whole value, read from a file holding a JSON object — or - for standard input.",
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(key);
        foreach (var option in (Option[])[value, valueFile, reason, human])
        {
            command.Options.Add(option);
        }

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var document = await ValueAsync(env, parseResult.GetValue(value), parseResult.GetValue(valueFile), cancellationToken).ConfigureAwait(false);
            var body = RequestBody.Empty()
                .Set("key", parseResult.GetValue(key))
                .Set("value", document)
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, "preference set", "a preference is set on a person's word");

            return await OperationRunner
                .RunAsync(env, Operations.PreferenceSet, body, new RunOptions(parseResult.GetValue(human), PreferenceRenderers.Preference), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Unset(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "unset",
            "Take a preference's value back: it is deleted, and the record of the change keeps only its size and hash. " + ActorOption.NoDefault);
        var key = KeyArgument();
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(key);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("key", parseResult.GetValue(key))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment()));
            ActorOption.Named(body, "preference unset", "a preference is taken back on a person's word");

            return OperationRunner.RunAsync(env, Operations.PreferenceUnset, body, new RunOptions(parseResult.GetValue(human), PreferenceRenderers.Preference), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "Every preference this build knows, set or not, with its value where it is set.");
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor), env.AttemptFromEnvironment());
            var body = RequestBody.Empty()
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.PreferenceList, body, new RunOptions(parseResult.GetValue(human), PreferenceRenderers.PreferenceList), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// The value itself, from the command line or from a file. Exactly one of the two: a verb that silently preferred one
    /// source over the other would replace the person's word with whichever the caller forgot they had given, and no value
    /// at all is not an empty one — taking a value back is <c>unset</c>.
    /// </summary>
    private static async Task<JsonNode?> ValueAsync(CliEnvironment env, string? inline, string? file, CancellationToken cancellationToken)
    {
        if (inline is not null && file is not null)
        {
            throw new UsageException("Give the value with --value or --value-file, not both.");
        }

        if (inline is null && file is null)
        {
            throw new UsageException("The value is required: give it with --value or --value-file. To take a value back, use preference unset.");
        }

        return inline is not null
            ? VerbOptions.Object(inline, "--value")
            : await RequestBody.FromFileAsync(env, "--value-file", file!, arrayProperty: null, cancellationToken).ConfigureAwait(false);
    }

    private static Argument<string> KeyArgument() =>
        new("key") { Description = "The preference, as preference list shows it: agent.interactive_host, send_mode.cold, send_mode.warm, sender.identity, tone_of_voice." };
}
