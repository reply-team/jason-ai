using System.CommandLine;
using System.Text.Json.Nodes;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>profile</c> verb group: the agent hosts this installation can launch, and how. A profile holds no
/// credential and has nowhere to put one — the host it names was installed and authenticated by a person, and
/// the profile only says which one to start.
/// <para>
/// There is no delete here because there is none in the API: an attempt names the revision it ran under for
/// ever, so a profile that should not be used again is disabled.
/// </para>
/// </summary>
public static class ProfileCommands
{
    private const string FileShape =
        "The object may hold name, description, host, program, args, deny, cli_command and host_version_verified, "
        + "and is the way to send an empty args or deny list.";

    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var profile = new Command("profile", "Register the agent hosts this machine can launch, and read what is registered.");
        profile.Subcommands.Add(Create(env, actor));
        profile.Subcommands.Add(Update(env, actor));
        profile.Subcommands.Add(Get(env, actor));
        profile.Subcommands.Add(List(env, actor));
        profile.Subcommands.Add(Toggle(env, actor, "disable", "Take a profile out of service. Its revisions stay exactly where they are.", Operations.ProfileDisable));
        profile.Subcommands.Add(Toggle(env, actor, "enable", "Put a disabled profile back into service.", Operations.ProfileEnable));
        return profile;
    }

    private static Command Create(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("create", "Register an agent host: what starts it, what it may not do, and what it calls home with.");
        var name = new Argument<string>("name") { Description = "The profile name, as work will spell it — for example local-claude." };
        var fields = new Fields();
        var file = VerbOptions.File(FileShape);
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();

        command.Arguments.Add(name);
        fields.AddTo(command);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), arrayProperty: null, cancellationToken).ConfigureAwait(false);
            fields.LayOver(body, parseResult)
                .Set("name", parseResult.GetValue(name))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.ProfileCreate, body, new RunOptions(parseResult.GetValue(human), ProfileRenderers.Profile), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Update(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("update", "Edit a profile. The runtime appends a whole new revision; the one before it never changes.");
        var name = new Argument<string>("name") { Description = "The profile to edit. A profile is never renamed: work names it by this." };
        var fields = new Fields();
        var file = VerbOptions.File(FileShape);
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();

        command.Arguments.Add(name);
        fields.AddTo(command);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), arrayProperty: null, cancellationToken).ConfigureAwait(false);
            fields.LayOver(body, parseResult)
                .Set("name", parseResult.GetValue(name))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.ProfileUpdate, body, new RunOptions(parseResult.GetValue(human), ProfileRenderers.Profile), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("get", "One profile: how it launches a host now, or how it did at an earlier revision.");
        var name = new Argument<string>("name") { Description = "The profile, as profile list shows it." };
        var revision = new Option<int?>("--revision") { Description = "Read an earlier revision — the number an attempt records — instead of the one in force." };
        var human = VerbOptions.Human();
        command.Arguments.Add(name);
        command.Options.Add(revision);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("name", parseResult.GetValue(name))
                .Set("revision", parseResult.GetValue(revision));

            return OperationRunner.RunAsync(
                env, Operations.ProfileGet, body, new RunOptions(parseResult.GetValue(human), ProfileRenderers.Profile), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "The profiles that can launch work. Disabled ones are left out unless you ask for them.");
        var includeDisabled = new Option<bool>("--include-disabled") { Description = "Include profiles that are out of service." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();

        foreach (var option in (Option[])[includeDisabled, limit, cursor, human])
        {
            command.Options.Add(option);
        }

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("include_disabled", parseResult.GetValue(includeDisabled) ? JsonValue.Create(true) : null)
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(
                env, Operations.ProfileList, body, new RunOptions(parseResult.GetValue(human), ProfileRenderers.ProfileList), cancellationToken);
        });

        return command;
    }

    /// <summary>Disable and enable take the same three things and differ only in which operation they call.</summary>
    private static Command Toggle(CliEnvironment env, Option<string?> actor, string verb, string description, string operation)
    {
        var command = new Command(verb, description);
        var name = new Argument<string>("name") { Description = "The profile, as profile list shows it." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(name);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var body = RequestBody.Empty()
                .Set("name", parseResult.GetValue(name))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return OperationRunner.RunAsync(
                env, operation, body, new RunOptions(parseResult.GetValue(human), ProfileRenderers.Profile), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// The fields a profile's revision is made of, which create and update both carry. Written once: the two
    /// verbs differ in what an absent option means to the runtime, not in what the options are.
    /// </summary>
    private sealed class Fields
    {
        private readonly Option<string?> _description = new("--description") { Description = "What this profile is for, in a sentence." };
        private readonly Option<string?> _host = new("--host") { Description = "Which agent host this describes — for example claude_code." };
        private readonly Option<string?> _program = new("--program") { Description = "The program to start: an absolute path, or a name resolved on PATH." };
        private readonly Option<string[]> _arg = VerbOptions.Repeatable("--arg", "One argument added after the ones the runtime composes. Repeat the option once per argument, in order.");
        private readonly Option<string[]> _deny = VerbOptions.Repeatable("--deny", "One thing the launched agent may not do, in the host's own vocabulary. Repeat for more.");
        private readonly Option<string?> _cliCommand = new("--cli-command") { Description = "The bare command word the launched agent calls home with. Absent means the runtime's own." };
        private readonly Option<string?> _hostVersion = new("--host-version") { Description = "The host version this profile was verified against. Recorded, never enforced." };

        public void AddTo(Command command)
        {
            foreach (var option in (Option[])[_description, _host, _program, _arg, _deny, _cliCommand, _hostVersion])
            {
                command.Options.Add(option);
            }
        }

        public JsonObject LayOver(JsonObject body, ParseResult parseResult) =>
            body
                .Set("description", parseResult.GetValue(_description))
                .Set("host", parseResult.GetValue(_host))
                .Set("program", parseResult.GetValue(_program))
                .Set("args", VerbOptions.Strings(parseResult.GetValue(_arg)))
                .Set("deny", VerbOptions.Strings(parseResult.GetValue(_deny)))
                .Set("cli_command", parseResult.GetValue(_cliCommand))
                .Set("host_version_verified", parseResult.GetValue(_hostVersion));
    }
}
