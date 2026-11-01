using System.CommandLine;
using Jason.Cli.Folders;
using Jason.Cli.Human;
using Jason.Contracts.Api;

namespace Jason.Cli.Commands;

/// <summary>
/// The <c>project</c> verb group: one verb per project operation. A project is made, read, listed, renamed and linked to
/// where its folder is; there is no delete, because there is none in the API, and no verb changes a slug, because a slug
/// never changes.
/// </summary>
public static class ProjectCommands
{
    public static Command Build(CliEnvironment env, Option<string?> actor)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(actor);

        var project = new Command("project", "Group campaigns by the business goal they work toward: make, read, list, rename and link projects to their folders.");
        project.Subcommands.Add(Create(env, actor));
        project.Subcommands.Add(Get(env, actor));
        project.Subcommands.Add(List(env, actor));
        project.Subcommands.Add(Update(env, actor));
        project.Subcommands.Add(Link(env, actor));
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
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)), env);

            return await OperationRunner
                .RunAsync(env, Operations.ProjectCreate, body, new RunOptions(parseResult.GetValue(human), ProjectRenderers.Project), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    private static Command Get(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "get",
            "Show one project, by its id or by its slug. With neither, it is the project whose folder, or whose campaign's folder, the command runs in.");
        var id = new Argument<string?>("project-id") { Description = "The project, as prj_.... Leave it out to name the project by --slug.", Arity = ArgumentArity.ZeroOrOne };
        var slug = new Option<string?>("--slug") { Description = "The project's slug, instead of its id." };
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(slug);
        command.Options.Add(human);

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            ActorOption.Parse(parseResult.GetValue(actor));
            var (byId, bySlug) = (parseResult.GetValue(id), parseResult.GetValue(slug));
            if (byId is not null && bySlug is not null)
            {
                throw new UsageException("Name the project by its id or by --slug, one of the two.");
            }

            var body = RequestBody.Empty().Set("project_id", byId).Set("slug", bySlug);
            if (byId is null && bySlug is null)
            {
                if ((await WorkingFolder.FillProjectAsync(env, body, WorkingFolder.ByIdOrSlug, cancellationToken).ConfigureAwait(false)).ExitCode is { } stopped)
                {
                    return stopped;
                }

                if (!body.ContainsKey("project_id"))
                {
                    throw new UsageException("Name the project by its id or by --slug, one of the two, or run this inside the project's folder.");
                }
            }

            return await OperationRunner
                .RunAsync(env, Operations.ProjectGet, body, new RunOptions(parseResult.GetValue(human), ProjectRenderers.Project), cancellationToken)
                .ConfigureAwait(false);
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
        var id = new Argument<string>("project-id") { Description = "The project, as prj_...." };
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
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)), env);

            return await OperationRunner
                .RunAsync(env, Operations.ProjectUpdate, body, new RunOptions(parseResult.GetValue(human), ProjectRenderers.Project), cancellationToken)
                .ConfigureAwait(false);
        });

        return command;
    }

    /// <summary>
    /// <c>project link</c>: the one act by which the filesystem changes a fact the runtime holds, a recorded path. With
    /// neither a project nor a path, typed inside a project's folder or one of its campaigns', it links the folder of the
    /// nearest project marker above by that marker: the folder moved and not yet relinked, which is exactly the one the
    /// project fill of the other verbs refuses to adopt.
    /// </summary>
    private static Command Link(CliEnvironment env, Option<string?> actor)
    {
        var command = new Command(
            "link",
            "Record where a project's folder is now, and its campaigns', by the markers in them. " +
            "The folder --path names is taken as a folder at a level's path is: with no marker it is adopted, its marker written and a CLAUDE.md and an AGENTS.md only where it has none; " +
            "marked for the project it is relinked; marked for anything else it is refused, with nothing written. " +
            "The project is the one project-id names, or else the one the marker at --path names; with neither, it is the folder of the nearest project marker above the folder the command runs in, linked by that marker. " +
            "With a project alone, its own folder is made under the root where it has none. " +
            "It reads the project's folder and each folder directly in its campaigns/, never deeper; a campaign found there that is another project's is reported and left as it is, and a campaign with no folder gets one unless it is archived. " +
            "It never moves, copies or deletes a folder or a file, never reads or writes a word of an AGENTS.md, and never writes over a CLAUDE.md.");
        var id = new Argument<string?>("project-id")
        {
            Description = "The project, as prj_.... Leave it out to link the folder --path names by its marker, or, inside a project's folder, that folder.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var path = new Option<string?>("--path") { Description = "The project's folder as it is now. A relative path is read from the folder the command runs in." };
        var reason = VerbOptions.Reason();
        var human = VerbOptions.Human();
        command.Arguments.Add(id);
        command.Options.Add(path);
        command.Options.Add(reason);
        command.Options.Add(human);

        command.SetAction((parseResult, cancellationToken) =>
        {
            var folder = parseResult.GetValue(path) is { } given ? Absolute(given, env.WorkingDirectory) : null;
            var project = parseResult.GetValue(id);
            if (project is null && folder is null)
            {
                folder = env.WorkingDirectory is { } start ? WorkingFolder.NearestProjectFolder(start) : null;
                if (folder is null)
                {
                    throw new UsageException("Name the project (project-id), its folder (--path), or both, or run this inside the project's folder.");
                }

                env.Error.WriteLine($"Linking the project folder '{folder}', the nearest one at or above the folder this command runs in, by its marker.");
            }

            var body = RequestBody.Empty()
                .Set("project_id", project)
                .Set("path", folder)
                .Set("reason", parseResult.GetValue(reason))
                .SetActor(ActorOption.Parse(parseResult.GetValue(actor)), env);
            return OperationRunner.RunAsync(env, Operations.ProjectLink, body, new RunOptions(parseResult.GetValue(human), ProjectRenderers.Link), cancellationToken);
        });

        return command;
    }

    /// <summary>
    /// <c>--path</c> as the absolute path the runtime takes: a relative one read from the folder the command runs in. An
    /// empty one is no folder at all — a script's unset variable, most often — and never the working directory, which
    /// the platform would make of it; a relative one where the platform cannot name the working directory has nothing to
    /// be read from.
    /// </summary>
    private static string Absolute(string given, string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(given))
        {
            throw new UsageException("--path names no folder: give the project's folder, or leave --path out.");
        }

        if (Path.IsPathFullyQualified(given))
        {
            return Path.GetFullPath(given);
        }

        return workingDirectory is { } start
            ? Path.GetFullPath(given, start)
            : throw new UsageException("--path is relative, and the folder this command runs in cannot be read to read it from: give an absolute path.");
    }
}
