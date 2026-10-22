using Jason.Contracts.Api;

namespace Jason.Cli.Human;

/// <summary>
/// <c>--human</c> rendering for the project verbs. Every renderer returns null when the body is not what it expects, and
/// the runner falls back to printing the response as it came.
/// </summary>
public static class ProjectRenderers
{
    private const int Label = 10;

    /// <summary>One project: its id, name, slug, and whether campaigns that name no project land in it.</summary>
    public static string? Project(string json)
    {
        var project = RenderText.Read<ProjectDto>(json);
        if (project?.Id is null || project.Name is null || project.Slug is null)
        {
            return null;
        }

        return RenderText.Lines(
        [
            Line("Id:", project.Id),
            Line("Name:", project.Name),
            Line("Slug:", project.Slug),
            Line("Default:", project.Default ? "yes" : "no"),
            Line("Created:", RenderText.Moment(project.CreatedAt)),
            Line("Updated:", RenderText.Moment(project.UpdatedAt)),
        ]);
    }

    /// <summary>A page of projects, and the cursor that continues it.</summary>
    public static string? ProjectList(string json)
    {
        var page = RenderText.Read<Page<ProjectDto>>(json);
        if (page?.Items is null)
        {
            return null;
        }

        var table = new HumanTable("ID", "NAME", "SLUG", "DEFAULT", "CREATED");
        foreach (var project in page.Items)
        {
            table.Row(project.Id, project.Name, project.Slug, project.Default ? "yes" : "no", RenderText.Moment(project.CreatedAt));
        }

        return RenderText.WithCursor(table.Render(), page.NextCursor);
    }

    private static string Line(string label, string value) => label.PadRight(Label) + value;
}
