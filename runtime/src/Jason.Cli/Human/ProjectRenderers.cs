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

    /// <summary>
    /// What a link did, by ids and paths as the answer gives them and never a name: the project's folder, each campaign's,
    /// and the folders found and not acted on.
    /// </summary>
    public static string? Link(string json)
    {
        var linked = RenderText.Read<ProjectLinkResult>(json);
        if (linked?.Project?.Id is null || linked.Campaigns is null || linked.Reported is null)
        {
            return null;
        }

        var lines = new List<string>
        {
            Line("Project:", linked.Project.Id),
            Line("Folder:", $"{RenderText.Snake(linked.Folder)}  {linked.Project.FolderPath}"),
        };

        if (linked.Campaigns.Count > 0)
        {
            var campaigns = new HumanTable("CAMPAIGN", "OUTCOME", "FOLDER");
            foreach (var campaign in linked.Campaigns)
            {
                campaigns.Row(campaign.CampaignId, RenderText.Snake(campaign.Outcome), campaign.FolderPath ?? "-");
            }

            lines.Add(string.Empty);
            lines.Add(campaigns.Render());
        }

        if (linked.Reported.Count > 0)
        {
            var reported = new HumanTable("FOUND AND LEFT AS IT IS", "REASON", "CAMPAIGN", "PROJECT");
            foreach (var report in linked.Reported)
            {
                reported.Row(report.FolderPath, RenderText.Snake(report.Reason), report.CampaignId ?? "-", report.ProjectId ?? "-");
            }

            lines.Add(string.Empty);
            lines.Add(reported.Render());
        }

        return RenderText.Lines(lines);
    }

    private static string Line(string label, string value) => label.PadRight(Label) + value;
}
