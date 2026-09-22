using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Skills;
using Jason.Runtime.Api;
using Jason.Runtime.Configuration;
using Jason.Runtime.Discovery;
using Jason.Runtime.Dispatch;
using Jason.Runtime.Execution;
using Jason.Runtime.Persistence;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Routing;
using Jason.Runtime.Update;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Hosting.Modules;

/// <summary>The runtime talking about itself: what it is, and how to stop it. No business state.</summary>
public static class SystemModule
{
    /// <summary>The instance facts this module answers with are composed during startup and registered by the host.</summary>
    public static IServiceCollection AddSystemModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ShutdownCoordinator>();
        services.AddSingleton<DrainCoordinator>();
        return services;
    }

    public static void MapSystemOperations(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapPost(
            Operations.Route(Operations.SystemInfo),
            (RuntimeInfo runtimeInfo,
             MigrationReport report,
             JasonPaths dataPaths,
             DispatcherStatus dispatcher,
             LiveSettings<DispatcherOptions> dispatcherSettings,
             LiveSettings<RolesOptions> roleSettings,
             RunningAttemptRegistry running,
             PluginRegistry plugins,
             RouteRegistry routes,
             UpdateAdvertisement update) =>
                TypedResults.Ok(new SystemInfoResponse(
                    runtimeInfo.RuntimeVersion,
                    ApiVersion.Current,
                    runtimeInfo.InstanceId,
                    runtimeInfo.Pid,
                    runtimeInfo.StartedAt,
                    dataPaths.Root,
                    new DatabaseInfo(report.AppliedMigrations, report.NewlyApplied, report.BackupFile),
                    // Through the settings, not the file: an operator whose edit was refused asks this operation
                    // what the runtime is working from, and is answered with what it is actually working from.
                    dispatcher.Snapshot(dispatcherSettings.Current, running.Count),
                    new PluginsInfo(
                        plugins.Snapshot.Plugins.Count,
                        plugins.Snapshot.Id,
                        PluginMapper.Utc(plugins.Snapshot.LoadedAt),
                        plugins.LastReload?.Activated),
                    new RoutesInfo(
                        routes.Snapshot.Id,
                        routes.Snapshot.ActivatedAt,
                        routes.Snapshot.Global.Default?.PluginId,
                        routes.Snapshot.Global.Operations.Count,
                        routes.Snapshot.Campaigns.Values.Sum(set => set.Operations.Count + (set.Default is null ? 0 : 1))),
                    // Null until a check has succeeded: "has not looked" and "looked and found nothing newer"
                    // are different answers, and a status line has to be able to give either.
                    update.Current,
                    // Read at the moment it is asked, because the directory is the runtime's own input and is
                    // read again at every launch. Nothing is cached: a deployment that landed a second ago is
                    // what the next launch will be taught.
                    Skills(dataPaths, roleSettings.Current.MaxSkillBytes))));

        app.MapOperation<ShutdownCoordinator, ShutdownRequest, ShutdownResponse>(
            Operations.SystemShutdown,
            (coordinator, _, _) => Task.FromResult(coordinator.RequestShutdown()));

        // Stop claiming, and claim again. Both are answers rather than errors when there is nothing to do,
        // because the caller that repeats one is an applier resuming an update it was interrupted in.
        app.MapOperation<DrainCoordinator, DrainRequest, DrainResponse>(
            Operations.SystemDrain,
            (coordinator, _, _) => Task.FromResult(coordinator.Drain()));

        app.MapOperation<DrainCoordinator, DrainRequest, DrainResponse>(
            Operations.SystemResume,
            (coordinator, _, _) => Task.FromResult(coordinator.Resume()));
    }

    /// <summary>
    /// The role skills root as it is right now: every directory in it, how large each is, and what would stop
    /// a launch being taught it — measured with the rule the launcher itself applies, so the answer is the
    /// launcher's answer and not a second opinion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here throws. This is the one section of <c>system.info</c> that touches the disk, and this
    /// operation is what an applier asks while it is resuming an update on a machine already in a bad state:
    /// a failure that became a 500 would read to that caller as a runtime that is not there. A root or a role
    /// that cannot be read is a sentence in the answer instead.
    /// </para>
    /// <para>
    /// Every directory is reported and none is skipped. A skip list is a thing that goes stale; what keeps
    /// this honest is the other side of the bargain — a deployment stages and sets aside under
    /// <see cref="JasonPaths.SkillsStagingDirectory"/>, which is a sibling of this root and not inside it.
    /// </para>
    /// </remarks>
    private static SkillsInfo Skills(JasonPaths paths, int maxSkillBytes)
    {
        var root = paths.RoleSkillsDirectory;
        string[] directories;
        try
        {
            directories = Directory.Exists(root) ? Directory.GetDirectories(root) : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new SkillsInfo(root, maxSkillBytes, [], $"The role skills directory could not be read: {exception.Message}");
        }

        Array.Sort(directories, StringComparer.Ordinal);
        var roles = new List<DeployedRoleSkill>(directories.Length);
        foreach (var directory in directories)
        {
            var role = Path.GetFileName(directory);

            // A directory whose name is not a role name is skipped rather than read. `mkdir ' '` is trivial
            // on Linux and macOS, and RoleSkillRules.Read opens with a null-or-whitespace guard -- so one such
            // directory made every system.info call a 500, which is the answer an applier must never get and
            // the one this section is shaped to avoid. The status verb already reports strangers as a
            // non-failing fact, which is where a name nobody seeded belongs.
            if (string.IsNullOrWhiteSpace(role) || role != Path.GetFileName(role) || role is "." or "..")
            {
                continue;
            }

            try
            {
                var reading = RoleSkillRules.Read(directory, role, maxSkillBytes);
                if (!reading.Exists)
                {
                    // It was there when the root was listed and is not there now, which is what a deployment
                    // renaming into place looks like from here.
                    continue;
                }

                roles.Add(new DeployedRoleSkill(
                    role,
                    reading.Bytes,
                    reading.Problem?.Message
                        ?? (reading.HasSkillFile
                            ? null
                            : $"'{directory}' holds no {RoleSkillRules.SkillFile}, so a launch copies what is there and the host loads no skill from it.")));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                roles.Add(new DeployedRoleSkill(role, null, $"It could not be read: {exception.Message}"));
            }
        }

        return new SkillsInfo(root, maxSkillBytes, roles);
    }
}
