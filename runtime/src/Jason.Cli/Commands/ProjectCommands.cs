using System.CommandLine;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>project</c> verb group: one verb per project operation. A project is made, read, listed and renamed; there is
/// no delete, because there is none in the API, and no verb changes a slug, because a slug never changes.
/// </summary>
public static class ProjectCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var project = new Command("project", "Group campaigns by the business goal they work toward: make, read, list and rename projects.");
        project.Subcommands.Add(Create(env, actor));
        project.Subcommands.Add(Get(env, actor));
        project.Subcommands.Add(List(env, actor));
        project.Subcommands.Add(Update(env, actor));
        return project;
    }

    private static Command Create(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "create",
            "Make a project. Its slug is made from the name unless given, and never changes; a slug already taken is refused, naming the project that has it.");
        var name = new Option<string?>("--name") { Description = "The project name." };
        var slug = new Option<string?>("--slug") { Description = "The slug: lower-case letters and digits joined by hyphens. Absent, it is made from the name." };
        var file = VerbOptions.File("The object holds name and slug.");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Options.Add(name);
        command.Options.Add(slug);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("name", parseResult.GetValue(name))
                .Set("slug", parseResult.GetValue(slug))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.ProjectCreate, body, new RunOptions(parseResult.GetValue(human), ProjectRenderers.Project), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("get", "Show one project, by its id or by its slug.");
        var id = new Argument<string?>("project-id") { Description = "The project, as prj_…. Leave it out to name the project by --slug.", Arity = ArgumentArity.ZeroOrOne };
        var slug = new Option<string?>("--slug") { Description = "The project's slug, instead of its id." };
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(slug);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var (byId, bySlug) = (parseResult.GetValue(id), parseResult.GetValue(slug));
            if ((byId is null) == (bySlug is null))
            {
                throw new UsageException("Name the project by its id or by --slug, one of the two.");
            }

            var body = RequestBody.Empty().Set("project_id", byId).Set("slug", bySlug);
            return OperationRunner.RunAsync(env, Operations.ProjectGet, body, new RunOptions(parseResult.GetValue(human), ProjectRenderers.Project), cancellationToken);
        });

        return command;
    }

    private static Command List(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("list", "List projects in the order they were made, the default among them.");
        var query = new Option<string?>("--query") { Description = "Only projects whose name contains this text; case and accents do not matter." };
        var limit = VerbOptions.Limit();
        var cursor = VerbOptions.Cursor();
        var human = VerbOptions.Human();
        command.Options.Add(query);
        command.Options.Add(limit);
        command.Options.Add(cursor);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var body = RequestBody.Empty()
                .Set("query", parseResult.GetValue(query))
                .Set("limit", parseResult.GetValue(limit))
                .Set("cursor", parseResult.GetValue(cursor));

            return OperationRunner.RunAsync(env, Operations.ProjectList, body, new RunOptions(parseResult.GetValue(human), ProjectRenderers.ProjectList), cancellationToken);
        });

        return command;
    }

    private static Command Update(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command("update", "Rename a project. Its slug stays as it is.");
        var id = new Argument<string>("project-id") { Description = "The project, as prj_…." };
        var name = new Option<string?>("--name") { Description = "The new project name." };
        var file = VerbOptions.File("The object holds the fields to patch (name).");
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(name);
        command.Options.Add(file);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var body = await VerbOptions.BodyAsync(env, parseResult.GetValue(file), null, cancellationToken).ConfigureAwait(false);
            body.Set("project_id", parseResult.GetValue(id))
                .Set("name", parseResult.GetValue(name))
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)));

            return await OperationRunner
                .RunAsync(env, Operations.ProjectUpdate, body, new RunOptions(parseResult.GetValue(human), ProjectRenderers.Project), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }
}
