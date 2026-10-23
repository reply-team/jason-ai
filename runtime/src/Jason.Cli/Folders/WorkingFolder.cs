using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Harness;

namespace Jason.Cli.Folders;

/// <summary>
/// The project a verb is about when it was given none, found from the folder the command was typed in: the nearest marker
/// of the harness at or above it (<c>docs/projects.md</c>, "From the command line"). A project's marker gives that project
/// and a campaign's the campaign's project, as the runtime records it, read once; the root's marker, or none, gives nothing,
/// and the verb goes on as it would anywhere else.
/// </summary>
/// <remarks>
/// <para>
/// A marker is used only when the folder it is in is the folder the runtime records for what it names: a copied folder,
/// or one moved and not yet relinked, names a project too, and adopting it would make the campaign in the project the copy
/// claims while the person's words stay in another folder. Such a marker is said on stderr, with the folder the runtime
/// records, and never used.
/// </para>
/// <para>
/// It fills a project and never a campaign: a write addressed by where a shell happens to stand is how the wrong campaign
/// gets changed. Which verbs ask for it is decided verb by verb, where each is built; a verb that does not ask never walks,
/// and nothing here is read for one that names its project itself.
/// </para>
/// </remarks>
public static class WorkingFolder
{
    /// <summary>How a campaign verb names another project than the one its folder gave: by <c>--project</c>.</summary>
    public const string ByProjectOption = "give --project to name another";

    /// <summary>How <c>project get</c> names another project than the one its folder gave: it has no <c>--project</c>.</summary>
    public const string ByIdOrSlug = "give a project id or --slug to name another";

    /// <summary>
    /// The nearest folder at or above <paramref name="start"/> that holds a marker, with the marker as it reads; null when
    /// none up to the top of the file system holds one. A marker that is there and cannot be read is still the nearest,
    /// read as naming nothing: the walk stops at the first one, whatever it says.
    /// </summary>
    public static (string Folder, HarnessMarker Marker)? Nearest(string start)
    {
        ArgumentNullException.ThrowIfNull(start);
        for (var folder = FolderPaths.Normal(start); folder is not null; folder = Path.GetDirectoryName(folder))
        {
            if (Read(folder) is { } marker)
            {
                return (folder, marker);
            }
        }

        return null;
    }

    /// <summary>
    /// The folder of the nearest project's marker at or above <paramref name="start"/>, walking on past a campaign's — a
    /// campaign's folder is in its project's — and stopping at the root's, at one that cannot be read, or at the top of the
    /// file system: null there. Nothing is asked of the runtime: what the marker names is the runtime's to read.
    /// </summary>
    public static string? NearestProjectFolder(string start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var from = start;
        while (Nearest(from) is { } found)
        {
            var (folder, marker) = found;
            if (marker.Kind == HarnessMarker.Project)
            {
                return folder;
            }

            if (marker.Kind != HarnessMarker.Campaign || Path.GetDirectoryName(folder) is not { } above)
            {
                return null;
            }

            from = above;
        }

        return null;
    }

    /// <summary>
    /// Fills <c>project_id</c> in <paramref name="body"/> from the working directory's marker, when the body names none, and
    /// says so on stderr in one line; reports a marker it does not use, on stderr, and leaves the body as it was.
    /// </summary>
    /// <param name="toNameAnother">How the verb is told another project, said at the end of that line: verbs name a project differently.</param>
    /// <returns>
    /// What the fill came to: the exit code to end the verb with when the runtime could not be asked what the marker names,
    /// and otherwise none, with the project's slug where the folder gave the project, for a view that says why it is scoped.
    /// </returns>
    public static async Task<FolderFill> FillProjectAsync(CliEnvironment env, JsonObject body, string toNameAnother, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(env);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(toNameAnother);
        if (env.WorkingDirectory is not { } start || body.ContainsKey("project_id") || Nearest(start) is not { } found)
        {
            return FolderFill.None;
        }

        var (folder, marker) = found;
        if (marker.Kind == HarnessMarker.Root)
        {
            return FolderFill.None;
        }

        var campaign = marker.Kind == HarnessMarker.Campaign;
        if (marker.Id is null || !(campaign || marker.Kind == HarnessMarker.Project))
        {
            env.Error.WriteLine($"The marker in '{folder}' cannot be read as one of Jason's, so it is not used here.");
            return FolderFill.None;
        }

        var (exitCode, response) = await OperationRunner
            .SendAsync(
                env,
                campaign ? Operations.CampaignGet : Operations.ProjectGet,
                RequestBody.Empty().Set(campaign ? "campaign_id" : "project_id", marker.Id),
                cancellationToken)
            .ConfigureAwait(false);
        if (response is null)
        {
            return new FolderFill(exitCode, null);
        }

        var named = campaign ? $"campaign {marker.Id}" : $"project {marker.Id}";
        if (!response.IsSuccess)
        {
            env.Error.WriteLine($"The marker in '{folder}' names {named}, {Unread(response.Body)}, so it is not used here.");
            return FolderFill.None;
        }

        var answer = Parse(response.Body);
        var recorded = Text(answer, "folder_path");
        var project = campaign ? Text(answer, "project_id") : marker.Id;
        if (project is null || recorded is null || !FolderPaths.SameFolder(folder, recorded))
        {
            var where = recorded is null ? "which has no folder recorded" : $"whose folder is recorded as '{recorded}'";
            env.Error.WriteLine(
                $"The marker in '{folder}' names {named}, {where}, so it is not used here: a copied folder, or one moved and not yet relinked; " +
                "jason project link --path records where a project's folder is now.");
            return FolderFill.None;
        }

        // Said once, by the slug a person knows it by and the id the request carries, and said to come from where the
        // command runs, so a scoped answer is never a surprise.
        body["project_id"] = project;
        var slug = Text(answer, campaign ? "project_slug" : "slug");
        var whose = campaign ? $" names its campaign {marker.Id}" : " names it";
        env.Error.WriteLine($"Project {(slug is null ? project : $"{slug} ({project})")}, from the working directory: the marker in '{folder}'{whose}; {toNameAnother}.");
        return new FolderFill(null, slug ?? project);
    }

    /// <summary>The marker in <paramref name="folder"/>, null where it has none; one the disk will not let be read is read as naming nothing.</summary>
    private static HarnessMarker? Read(string folder)
    {
        try
        {
            return HarnessMarker.In(folder);
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException)
        {
            return new HarnessMarker(null, null);
        }
    }

    /// <summary>Why the runtime's answer does not name what the marker does, in the words of its error code.</summary>
    private static string Unread(string body)
    {
        var code = Text(Parse(body)?["error"] as JsonObject, "code");
        return code is "project_not_found" or "campaign_not_found"
            ? "which this runtime does not hold"
            : $"which the runtime did not answer for ({code ?? "no error code"})";
    }

    /// <summary>A string member of an answer, null where the answer is not an object or the member is not a string.</summary>
    private static string? Text(JsonNode? answer, string name) =>
        answer is JsonObject members && members[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static JsonObject? Parse(string body)
    {
        try
        {
            return JsonNode.Parse(body) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// What a fill from the working directory came to. <see cref="ExitCode"/> ends the verb where the runtime could not be
/// asked what a marker names; <see cref="Project"/> is how the project the folder gave is shown — its slug — and null
/// where the folder gave none.
/// </summary>
public sealed record FolderFill(int? ExitCode, string? Project)
{
    /// <summary>Nothing filled and nothing to end the verb with: the verb goes on as it would anywhere else.</summary>
    public static FolderFill None { get; } = new(null, null);
}
