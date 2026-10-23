using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>rolenote</c> verb group: what a role remembers, at one of three levels — a campaign's (<c>--campaign</c>), a
/// project's (<c>--project</c>), or the client's when neither is named, which is the role's general craft. It is the
/// role's own working knowledge and not the runtime's — where a note disagrees with campaign state, the state is what
/// is true.
/// <para>
/// There is no patch and no delete: a note is replaced whole, and clearing one is writing an empty object. A
/// role merging into its own memory would have to reason about what an earlier session of itself meant by a
/// key, and the rule that needs no reasoning is that the last writer owns the document.
/// </para>
/// <para>
/// The form is <c>rolenote get|set --role &lt;role&gt; [--campaign &lt;id&gt; | --project &lt;id&gt;]</c>. The older form,
/// <c>rolenote get|set &lt;campaign-id&gt; &lt;role&gt;</c>, is still accepted because skills already written print it, and only
/// whole: exactly those two arguments and none of the three options. One argument alone is refused, because it is what
/// the older form becomes when the variable that held the campaign is empty — and read as the role, it would reach the
/// client's note. A note is read or written at the client's level only by <c>--role</c> with neither scope option;
/// <c>list</c> with neither option lists the client's level, which roles wrote there and never what they wrote.
/// </para>
/// </summary>
public static class RoleNoteCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var rolenote = new Command("rolenote", "Read and replace what a role remembers: about a campaign, a project, or the client's work as a whole.");
        rolenote.Subcommands.Add(Get(env, actor));
        rolenote.Subcommands.Add(Set(env, actor));
        rolenote.Subcommands.Add(List(env, actor));
        return rolenote;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "get",
            "One role's note at one level: --campaign, --project, or the client's when neither is given. A role that has never written reads an empty one."
            + Environment.NewLine + Level.OlderForm("get"));
        var level = new Level(command, "get");
        var human = VerbOptions.Human();
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = level.Into(RequestBody.Empty(), parseResult);

            return OperationRunner.RunAsync(
                env, Operations.RoleNoteGet, body, new RunOptions(parseResult.GetValue(human), RoleNoteRenderers.Note), cancellationToken);
        });

        return command;
    }

    private static Command Set(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "set",
            "Replace a role's note at one level: --campaign, --project, or the client's when neither is given. The whole document is written; what was there is gone."
            + Environment.NewLine + Level.OlderForm("set"));
        var level = new Level(command, "set");
        var note = new Option<string?>("--note") { Description = "The whole note, as a JSON object." };
        var noteFile = new Option<string?>("--note-file")
        {
            Description = "The whole note, read from a file holding a JSON object — or - for standard input.",
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        foreach (var option in (Option[])[note, noteFile, reason, human])
        {
            command.Options.Add(option);
        }

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = level.Into(RequestBody.Empty(), parseResult);
            var document = await DocumentAsync(env, parseResult.GetValue(note), parseResult.GetValue(noteFile), cancellationToken)
                .ConfigureAwait(false);
            body = body
                .Set("note", document)
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.RoleNoteSet, body, new RunOptions(parseResult.GetValue(human), RoleNoteRenderers.Note), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    /// <summary>
    /// The roles that have written at one level. It takes no argument: no skill ever printed a campaign as one, and the
    /// level is named the way <c>get</c> and <c>set</c> name it.
    /// </summary>
    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "list",
            "Which roles have written notes at one level — --campaign, --project, or the client's when neither is given — how big they are and when; never what they say.");
        var campaign = CampaignOption("notes are listed");
        var project = ProjectOption("notes are listed");
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        foreach (var option in (Option[])[campaign, project, limit, cursor, human])
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("campaign_id", parseResult.GetValue(campaign))
                .Set("project_id", parseResult.GetValue(project))
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

    private static Option<string?> CampaignOption(string whose = "note this is") =>
        new("--campaign") { Description = $"The campaign whose {whose}, as cmp_…. With neither this nor --project, it is the client's level." };

    private static Option<string?> ProjectOption(string whose = "note this is") =>
        new("--project") { Description = $"The project whose {whose}, as prj_…. With neither this nor --campaign, it is the client's level." };

    /// <summary>
    /// The role and the level a <c>get</c> or <c>set</c> names: <c>--role</c>, with <c>--campaign</c>, <c>--project</c> or
    /// neither for the client's; or the older form's two arguments, the campaign and then the role, and nothing else.
    /// Whether a campaign and a project together name one level is the runtime's to answer, and it refuses them by name.
    /// </summary>
    private sealed class Level
    {
        // The older form's two arguments, accepted whole and never shown as the form to use.
        private readonly Argument<string?> _olderCampaign = new("campaign-id") { Arity = ArgumentArity.ZeroOrOne, Hidden = true };

        private readonly Argument<string?> _olderRole = new("role") { Arity = ArgumentArity.ZeroOrOne, Hidden = true };

        private readonly Option<string?> _role = new("--role") { Description = "The role whose memory this is, as role list shows it." };

        private readonly Option<string?> _campaign = CampaignOption();

        private readonly Option<string?> _project = ProjectOption();

        private readonly string _verb;

        public Level(Command command, string verb)
        {
            _verb = verb;
            command.Arguments.Add(_olderCampaign);
            command.Arguments.Add(_olderRole);
            command.Options.Add(_role);
            command.Options.Add(_campaign);
            command.Options.Add(_project);
        }

        /// <summary>The one line of a verb's help that names the older form, so a form it accepts is never one it hides.</summary>
        public static string OlderForm(string verb) =>
            $"The older form, rolenote {verb} <campaign-id> <role>, is still accepted: exactly those two arguments, and none of --role, --campaign or --project.";

        public JsonObject Into(JsonObject body, ParseResult parseResult)
        {
            var (olderCampaign, olderRole) = (parseResult.GetValue(_olderCampaign), parseResult.GetValue(_olderRole));
            var (role, campaign, project) = (parseResult.GetValue(_role), parseResult.GetValue(_campaign), parseResult.GetValue(_project));

            if (olderCampaign is null)
            {
                return role is null
                    ? throw new UsageException(
                        $"Name the role with --role: rolenote {_verb} --role <role>, with --campaign or --project for a campaign's or a project's note and neither for the client's.")
                    : body.Set("campaign_id", campaign).Set("project_id", project).Set("role", role);
            }

            // One argument is what the older form becomes when the variable holding the campaign is empty. Read as the
            // role, it would reach the client's note; refused, a script that meant a campaign finds out here.
            if (olderRole is null)
            {
                throw new UsageException(
                    $"One argument is not a form of rolenote {_verb}: give the campaign and the role (rolenote {_verb} <campaign-id> <role>), or name the role with --role.");
            }

            return role is not null || campaign is not null || project is not null
                ? throw new UsageException(
                    $"The older form, rolenote {_verb} <campaign-id> <role>, takes none of --role, --campaign or --project; use one form or the other.")
                : body.Set("campaign_id", olderCampaign).Set("role", olderRole);
        }
    }
}
