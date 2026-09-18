using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>rolenote</c> verb group: what a role remembers about a campaign. It is the role's own working
/// knowledge and not the runtime's — where a note disagrees with campaign state, the state is what is true.
/// <para>
/// There is no patch and no delete: a note is replaced whole, and clearing one is writing an empty object. A
/// role merging into its own memory would have to reason about what an earlier session of itself meant by a
/// key, and the rule that needs no reasoning is that the last writer owns the document.
/// </para>
/// </summary>
public static class RoleNoteCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var rolenote = new Command("rolenote", "Read and replace what a role remembers about a campaign.");
        rolenote.Subcommands.Add(Get(env, actor));
        rolenote.Subcommands.Add(Set(env, actor));
        rolenote.Subcommands.Add(List(env, actor));
        return rolenote;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("get", "One role's note for one campaign. A role that has never written reads an empty one.");
        var campaign = CampaignArgument();
        var role = RoleArgument();
        var human = VerbOptions.Human();
        command.Arguments.Add(campaign);
        command.Arguments.Add(role);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("role", parseResult.GetValue(role));

            return OperationRunner.RunAsync(
                env, Operations.RoleNoteGet, body, new RunOptions(parseResult.GetValue(human), RoleNoteRenderers.Note), cancellationToken);
        });

        return command;
    }

    private static Command Set(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("set", "Replace a role's note for a campaign. The whole document is written; what was there is gone.");
        var campaign = CampaignArgument();
        var role = RoleArgument();
        var note = new Option<string?>("--note") { Description = "The whole note, as a JSON object." };
        var noteFile = new Option<string?>("--note-file")
        {
            Description = "The whole note, read from a file holding a JSON object — or - for standard input.",
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(campaign);
        command.Arguments.Add(role);
        foreach (var option in (Option[])[note, noteFile, reason, human])
        {
            command.Options.Add(option);
        }

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var document = await DocumentAsync(env, parseResult.GetValue(note), parseResult.GetValue(noteFile), cancellationToken)
                .ConfigureAwait(false);
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("role", parseResult.GetValue(role))
                .Set("note", document)
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.RoleNoteSet, body, new RunOptions(parseResult.GetValue(human), RoleNoteRenderers.Note), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "Which roles have written notes for a campaign, how big they are and when — never what they say.");
        var campaign = CampaignArgument();
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Arguments.Add(campaign);
        foreach (var option in (Option[])[limit, cursor, human])
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(
                env, Operations.RoleNoteList, body, new RunOptions(parseResult.GetValue(human), RoleNoteRenderers.NoteList), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// The note itself, from the command line or from a file. Exactly one of the two: a verb that silently
    /// preferred one source over the other would overwrite a role's memory with whichever the caller forgot
    /// they had given.
    /// </summary>
    private static async Task<JsonNode?> DocumentAsync(CliEnvironment env, string? inline, string? file, CancellationToken cancellationToken)
    {
        if (inline is not null && file is not null)
        {
            throw new UsageException("Give the note with --note or --note-file, not both.");
        }

        if (inline is null && file is null)
        {
            throw new UsageException("The note is required: give it with --note or --note-file.");
        }

        return inline is not null
            ? VerbOptions.Object(inline, "--note")
            : await RequestBody.FromFileAsync(file!, env.In ?? TextReader.Null, arrayProperty: null, cancellationToken).ConfigureAwait(false);
    }

    private static Argument<string> CampaignArgument() =>
        new("campaign") { Description = "The campaign the note is about, as campaign list shows it." };

    private static Argument<string> RoleArgument() =>
        new("role") { Description = "The role whose memory this is, as role list shows it." };
}
