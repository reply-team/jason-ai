using Jason.Contracts.Api;
using Jason.Runtime.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Harness;
using Jason.Runtime.Projects;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>
/// The projects campaigns work toward. Five verbs: a project is made, read, listed, renamed and linked to where its folder
/// is, and never removed.
/// </summary>
public static class ProjectModule
{
    public static IServiceCollection AddProjectModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The harness root is read at each create and move, so an edit applies to the next one and a refused edit keeps
        // the last root that validated.
        services.AddSingleton(DispatcherModule.Seam<HarnessOptions>(HarnessOptions.Section));
        services.AddSingleton<HarnessFolders>();
        services.AddHostedService<ImportFilesSweep>();
        services.AddScoped<ProjectLinker>();
        return services.AddScoped<ProjectService>();
    }

    public static void MapProjectOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapOperation<ProjectService, ProjectCreateRequest, ProjectDto>(
            Operations.ProjectCreate, (service, request, cancellationToken) => service.CreateAsync(request, cancellationToken));
        app.MapOperation<ProjectService, ProjectGetRequest, ProjectDto>(
            Operations.ProjectGet, (service, request, cancellationToken) => service.GetAsync(request, cancellationToken));
        app.MapOperation<ProjectService, ProjectListRequest, Page<ProjectDto>>(
            Operations.ProjectList, (service, request, cancellationToken) => service.ListAsync(request, cancellationToken));
        app.MapOperation<ProjectService, ProjectUpdateRequest, ProjectDto>(
            Operations.ProjectUpdate, (service, request, cancellationToken) => service.UpdateAsync(request, cancellationToken));
        app.MapOperation<ProjectLinker, ProjectLinkRequest, ProjectLinkResult>(
            Operations.ProjectLink, (linker, request, cancellationToken) => linker.LinkAsync(request, cancellationToken));
    }
}
