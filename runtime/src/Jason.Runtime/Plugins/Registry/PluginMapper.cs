using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// The registry as the API answers it. Requested is shown next to granted for every capability, because
/// declaration is not permission and the difference is the thing a person needs to see.
/// </summary>
public static class PluginMapper
{
    /// <summary>The prefix a problem's path carries when it is about the file rather than a field in it.</summary>
    public const string ManifestPrefix = "plugin.yaml#";

    public static PluginRegistryDto ToDto(PluginSnapshot snapshot, string routingSnapshotId, ReloadReport? lastReload, bool activated)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new PluginRegistryDto(
            new SnapshotDto(snapshot.Id, Utc(snapshot.LoadedAt), snapshot.Source, snapshot.Plugins.Count),
            routingSnapshotId,
            [.. snapshot.Plugins.Select(ToDto)],
            lastReload is null ? null : ToDto(lastReload),
            activated);
    }

    public static PluginDto ToDto(LoadedPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        var manifest = plugin.Manifest;
        return new PluginDto(
            manifest.Id,
            manifest.Version,
            manifest.Kind,
            manifest.Name,
            manifest.Description,
            manifest.Homepage,
            plugin.Root,
            plugin.Digest,
            new PluginContractsDto(manifest.Contracts.Protocol, manifest.Contracts.Operations),
            manifest.Operations,
            manifest.Entry,
            new PluginCapabilitiesDto(
                manifest.Capabilities.Exec is null
                    ? null
                    : new ExecCapabilityDto(
                        [.. plugin.Executables.Select(executable => new ExecutableDto(
                            executable.Name,
                            executable.Path,
                            executable.Version,
                            executable.MinVersion,
                            executable.Launch.Count == 0 ? null : executable.Launch))],
                        plugin.Grants.Exec),
                manifest.Capabilities.Http is null ? null : new ListCapabilityDto(manifest.Capabilities.Http.Hosts, plugin.Grants.Http),
                manifest.Capabilities.Env is null ? null : new ListCapabilityDto(manifest.Capabilities.Env.Variables, plugin.Grants.Env)),
            new PluginLimitsDto(plugin.Limits.TimeoutMs, plugin.Limits.MemoryMb),

            // A copy, because the DTO is serialized on another thread than the snapshot it came from and a
            // JsonNode belongs to exactly one parent.
            manifest.Binding?.DeepClone().AsObject(),
            plugin.Status,
            [.. plugin.Problems.Select(ToDto)]);
    }

    public static ReloadReportDto ToDto(ReloadReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new ReloadReportDto(
            Utc(report.At),
            report.Source,
            report.Activated,
            [.. report.Candidates.Select(candidate => new CandidateDto(
                candidate.Directory,
                candidate.Id,
                candidate.Status,
                [.. candidate.Problems.Select(ToDto)]))],
            [.. report.Routes.Select(route => new RouteProblemDto(route.Field, route.Code, route.Message))]);
    }

    /// <summary>
    /// The problems of a rejected reload, each located for a person: a package problem by the candidate's
    /// directory, the manifest and the place inside it — the file is named exactly once, whichever half the path
    /// already carries — and a route problem by the route, which already names itself.
    /// </summary>
    public static IReadOnlyList<ErrorDetail> ToDetails(ReloadReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return
        [
            .. report.Candidates
                .Where(candidate => candidate.Status == CandidateStatus.Invalid)
                .SelectMany(candidate => candidate.Problems.Select(problem =>
                    new ErrorDetail(Field(candidate.Directory, problem.Path), problem.Code, problem.Message))),
            .. report.Routes,
            .. report.Settings ?? [],
        ];
    }

    public static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static PluginProblemDto ToDto(ManifestProblem problem) => new(problem.Code, problem.Path, problem.Message);

    private static string Field(string directory, string path) =>
        path.StartsWith(ManifestPrefix, StringComparison.Ordinal)
            ? $"{directory}/{path}"
            : $"{directory}/{ManifestPrefix}{path}";
}
