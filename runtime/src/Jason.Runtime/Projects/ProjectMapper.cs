using Jason.Contracts.Api;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Projects;

/// <summary>The stored project as the API answers with it, its folder included; the internal key is not part of it.</summary>
public static class ProjectMapper
{
    public static ProjectDto ToDto(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return new ProjectDto(project.PublicId, project.Name, project.Slug, project.IsDefault, Utc(project.CreatedAt), Utc(project.UpdatedAt), project.FolderPath);
    }

    /// <summary>Everything in the database is UTC; SQLite hands the kind back unset.</summary>
    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
