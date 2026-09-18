using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>role</c> verb group: the registry of jobs a work item can be assigned to. A role is a job
/// description, not a process, so the only thing that can be added to it here is the command that starts an
/// agent host for it.
/// </summary>
public static class RoleCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var role = new Command("role", "List the roles work can be assigned to, and register new ones.");
        role.Subcommands.Add(List(env, actor));
        role.Subcommands.Add(Add(env, actor));
        role.Subcommands.Add(SetProfile(env, actor));
        return role;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List the roles, the builtin ones first.");
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.RoleList, body, new RunOptions(parseResult.GetValue(human), RoleRenderers.RoleList), cancellationToken);
        });

        return command;
    }

    private static Command Add(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("add", "Register a role. Give it an entry command to make it launchable.");
        var name = new Argument<string>("name") { Description = "The role name, as the work items will spell it — for example researcher." };
        var entryCommand = VerbOptions.Repeatable(
            "--entry-command",
            "One argument of the command that starts an agent host for this role. Repeat the option once per argument, in order.");
        var profileDefaults = new Option<string?>("--profile-defaults") { Description = "The execution defaults for this role, as a JSON object." };
        var description = new Option<string?>("--description") { Description = "What the role does, in a sentence." };
        var executionProfile = ExecutionProfileOption();
        var file = VerbOptions.File("The object holds name, entry_command, profile_defaults, description and execution_profile.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(name);
        command.Options.Add(entryCommand);
        command.Options.Add(profileDefaults);
        command.Options.Add(description);
        command.Options.Add(executionProfile);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("name", parseResult.GetValue(name))
                .Set("entry_command", VerbOptions.Strings(parseResult.GetValue(entryCommand)))
                .Set("profile_defaults", VerbOptions.Object(parseResult.GetValue(profileDefaults), "--profile-defaults"))
                .Set("description", parseResult.GetValue(description))
                .Set("execution_profile", parseResult.GetValue(executionProfile))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.RoleAdd, body, new RunOptions(parseResult.GetValue(human), RoleRenderers.Role), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    /// <summary>
    /// The one verb that changes a registered role. Roles have no general update verb: which host a kind of
    /// worker uses is a decision somebody revisits, and the rest of a job description is not.
    /// </summary>
    private static Command SetProfile(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("set-profile", "Say which execution profile a role's work uses, or take the policy away.");
        var name = new Argument<string>("name") { Description = "The role whose policy changes — for example researcher." };
        var executionProfile = ExecutionProfileOption();
        var clear = new Option<bool>("--clear")
        {
            Description = "Take the policy away, so this role's work falls back to its campaign's policy or the configured default.",
        };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(name);
        command.Options.Add(executionProfile);
        command.Options.Add(clear);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var profile = parseResult.GetValue(executionProfile);
            var cleared = parseResult.GetValue(clear);

            // Naming a profile and clearing one asks two contradictory things, and naming neither asks for
            // nothing at all — both are things the CLI can tell without a runtime.
            if (profile is not null && cleared)
            {
                throw new UsageException("--execution-profile and --clear ask for opposite things; give exactly one.");
            }

            if (profile is null && !cleared)
            {
                throw new UsageException("Give --execution-profile <name> to point the role at a profile, or --clear to take its policy away.");
            }

            var body = RequestBody.Empty()
                .Set("name", parseResult.GetValue(name))
                .Set("execution_profile", profile, onlyIfNotNull: false)
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return OperationRunner.RunAsync(env, Operations.RoleSetProfile, body, new RunOptions(parseResult.GetValue(human), RoleRenderers.Role), cancellationToken);
        });

        return command;
    }

    private static Option<string?> ExecutionProfileOption() => new("--execution-profile")
    {
        Description = "The execution profile this role's work uses, where the work item and its campaign name none.",
    };
}
