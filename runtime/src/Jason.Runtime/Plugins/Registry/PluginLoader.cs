using System.Collections;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Ids;
using Jason.Contracts.Plugins;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Manifest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>
/// One pass over the plugins directory: scan, read every manifest, hash every package, resolve grants, ask the
/// machine about the declared programs, and decide whether the set may be activated. The two kinds of problem
/// are kept apart on purpose — a package problem holds the whole set out, an environmental one holds back only
/// the plugin it belongs to.
/// </summary>
public sealed class PluginLoader(
    JasonPaths paths,
    ExecutableResolver resolver,
    IOptionsMonitor<PluginsOptions> options,
    TimeProvider clock,
    ILogger<PluginLoader> logger)
{
    private static readonly Action<ILogger, string, bool, int, int, Exception?> Loaded = LoggerMessage.Define<string, bool, int, int>(
        LogLevel.Information,
        new EventId(1, nameof(Loaded)),
        "Plugin load ({Source}): activated {Activated}, {Plugins} plugins from {Candidates} candidates");

    private static readonly Action<ILogger, int, Exception?> SectionRefused = LoggerMessage.Define<int>(
        LogLevel.Error,
        new EventId(5, nameof(SectionRefused)),
        "Plugin load refused: the Plugins section of the settings file has {Problems} problem(s); the previous snapshot stays active");

    private static readonly Action<ILogger, string, string, Exception?> CandidateRejected = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(2, nameof(CandidateRejected)),
        "Plugin candidate {Directory} is not loadable: {Codes}");

    private static readonly Action<ILogger, string, string, Exception?> CandidateUnavailable = LoggerMessage.Define<string, string>(
        LogLevel.Warning,
        new EventId(3, nameof(CandidateUnavailable)),
        "Plugin {Directory} is unavailable on this machine: {Codes}");

    private static readonly Action<ILogger, string, int, int, int, Exception?> GrantsResolved = LoggerMessage.Define<string, int, int, int>(
        LogLevel.Debug,
        new EventId(4, nameof(GrantsResolved)),
        "Plugin {Id} granted {Exec} executables, {Http} hosts, {Env} variables");

    public async Task<LoadResult> LoadAsync(SnapshotSource source, CancellationToken cancellationToken)
    {
        PluginsOptions settings;
        try
        {
            settings = options.CurrentValue;
        }
        catch (OptionsValidationException refused)
        {
            // A load reads the ceilings every package is held to, so it cannot be performed against a section
            // the validator will not hand over. Working from the last value that validated would be the wrong
            // answer here and not the kind one: a reload is an operator asking to make the file true now, and
            // activating a set built from settings the file no longer holds is exactly what they did not ask
            // for. So it is the same rejected reload a bad package gets, naming the setting, with the previous
            // snapshot left active — and never an exception nobody translated, which says the runtime broke.
            return Refused(source, refused);
        }
        var bounds = new ManifestBounds(settings.Limits.MaxTimeoutMs, settings.Limits.MaxMemoryMb);
        var environment = CurrentEnvironment();
        var candidates = new List<CandidateReport>();
        var plugins = new List<LoadedPlugin>();

        foreach (var candidate in PackageScanner.Scan(paths))
        {
            if (!candidate.HasManifest)
            {
                // A stray folder is not a plugin, and must never stand between every other plugin and the user.
                candidates.Add(new CandidateReport(
                    candidate.Name,
                    null,
                    CandidateStatus.Skipped,
                    [new ManifestProblem(ProblemCodes.ManifestMissing, PackageScanner.ManifestFileName, $"'{candidate.Name}' holds no {PackageScanner.ManifestFileName}.")]));
                continue;
            }

            var (loaded, plugin) = await LoadCandidateAsync(candidate, bounds, settings, environment, cancellationToken).ConfigureAwait(false);
            candidates.Add(loaded);
            if (plugin is not null)
            {
                plugins.Add(plugin);
            }

            switch (loaded.Status)
            {
                case CandidateStatus.Invalid:
                    CandidateRejected(logger, loaded.Directory, Codes(loaded.Problems), null);
                    break;
                case CandidateStatus.Unavailable:
                    CandidateUnavailable(logger, loaded.Directory, Codes(loaded.Problems), null);
                    break;
                default:
                    break;
            }
        }

        var activated = candidates.All(candidate => candidate.Status != CandidateStatus.Invalid);
        var now = clock.GetUtcNow().UtcDateTime;
        var report = new ReloadReport(now, source, activated, candidates, []);
        Loaded(logger, source.ToString(), activated, plugins.Count, candidates.Count, null);

        return new LoadResult(
            activated ? new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), now, source, plugins) : null,
            report);
    }

    /// <summary>
    /// A load that never looked at a package, because the section it reads its own ceilings from is invalid. The
    /// report is what <c>plugin.list</c> shows afterwards and what the caller is answered with, in the
    /// vocabulary an operator repairing an installation already reads.
    /// </summary>
    private LoadResult Refused(SnapshotSource source, OptionsValidationException refused)
    {
        var problems = refused.Failures
            .Select(failure => new ErrorDetail(
                SettingsFailure.Name(PluginsOptions.Section, failure),
                ProblemCodes.PluginsSettingsInvalid,
                failure))
            .ToList();

        SectionRefused(logger, problems.Count, null);
        return new LoadResult(null, new ReloadReport(clock.GetUtcNow().UtcDateTime, source, Activated: false, [], [], problems));
    }

    /// <summary>
    /// The runtime's own environment, as the base every plugin child is built from. Read here rather than in the
    /// child, because the child is given a fresh environment and never inherits one.
    /// </summary>
    public static Dictionary<string, string> CurrentEnvironment()
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var current = new Dictionary<string, string>(comparer);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && entry.Value is string value)
            {
                current[name] = value;
            }
        }

        return current;
    }

    private async Task<(CandidateReport Report, LoadedPlugin? Plugin)> LoadCandidateAsync(
        CandidateDirectory candidate,
        ManifestBounds bounds,
        PluginsOptions settings,
        Dictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        string yaml;
        try
        {
            yaml = await File.ReadAllTextAsync(Path.Combine(candidate.Path, PackageScanner.ManifestFileName), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (Invalid(candidate, null, ProblemCodes.PackageUnreadable, PackageScanner.ManifestFileName, exception.Message), null);
        }

        var read = ManifestReader.Read(yaml, candidate.Name, candidate.Path, bounds);
        if (read.Manifest is null)
        {
            return (new CandidateReport(candidate.Name, null, CandidateStatus.Invalid, read.Problems), null);
        }

        var manifest = read.Manifest;
        PackageDigestResult digest;
        try
        {
            digest = PackageDigest.Compute(candidate.Path);
        }
        catch (PackageTooLargeException exception)
        {
            return (Invalid(candidate, manifest.Id, ProblemCodes.PackageTooLarge, "(package)", exception.Message), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (Invalid(candidate, manifest.Id, ProblemCodes.PackageUnreadable, "(package)", exception.Message), null);
        }

        var grants = GrantResolver.Resolve(manifest, settings.Grants.GetValueOrDefault(manifest.Id));
        GrantsResolved(logger, manifest.Id, grants.Exec.Count, grants.Http.Count, grants.Env.Count, null);

        var executables = await ResolveExecutablesAsync(manifest, grants, settings, environment, cancellationToken).ConfigureAwait(false);
        var problems = new List<ManifestProblem>(grants.Warnings);
        problems.AddRange(executables.Select(executable => executable.Problem).OfType<ManifestProblem>());

        // A warning about a grant says something about the settings, not about whether the plugin can run.
        var status = problems.Any(problem => problem.Environmental) ? PluginStatus.Unavailable : PluginStatus.Valid;
        var plugin = new LoadedPlugin(
            manifest,
            candidate.Path,
            digest.Digest,
            executables,
            grants,
            new EffectivePluginLimits(
                Clamp(manifest.Limits.TimeoutMs ?? settings.Limits.TimeoutMs, settings.Limits.MaxTimeoutMs),
                Clamp(manifest.Limits.MemoryMb ?? settings.Limits.MemoryMb, settings.Limits.MaxMemoryMb)),
            status,
            problems);

        var candidateStatus = status == PluginStatus.Valid ? CandidateStatus.Valid : CandidateStatus.Unavailable;
        return (new CandidateReport(candidate.Name, manifest.Id, candidateStatus, problems), plugin);
    }

    private async Task<IReadOnlyList<ResolvedExecutable>> ResolveExecutablesAsync(
        PluginManifest manifest,
        ResolvedGrants grants,
        PluginsOptions settings,
        Dictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        if (manifest.Capabilities.Exec is null)
        {
            return [];
        }

        var resolved = new List<ResolvedExecutable>();
        var requests = manifest.Capabilities.Exec.Executables;
        for (var index = 0; index < requests.Count; index++)
        {
            var request = requests[index];
            var executable = resolver.Resolve(request, index);

            // Presence is established for every declared program; the version command runs only for one the user
            // granted, because a reload must never start a program nobody consented to.
            if (executable.Path is not null
                && request.VersionCommand is not null
                && grants.Exec.Contains(request.Name, StringComparer.Ordinal))
            {
                var childEnvironment = BaseEnvironment.Build(environment, grants.Env);
                executable = await resolver.CheckVersionAsync(
                    executable,
                    request,
                    index,
                    childEnvironment,
                    TimeSpan.FromMilliseconds(settings.Invoker.VersionCheckTimeoutMs),
                    cancellationToken,
                    new Redactor(grants.Env.Select(name => childEnvironment.GetValueOrDefault(name)))).ConfigureAwait(false);
            }

            resolved.Add(executable);
        }

        return resolved;
    }

    private static CandidateReport Invalid(CandidateDirectory candidate, string? id, string code, string path, string message) =>
        new(candidate.Name, id, CandidateStatus.Invalid, [new ManifestProblem(code, path, message)]);

    private static int Clamp(int asked, int ceiling) => Math.Min(asked, ceiling);

    private static string Codes(IReadOnlyList<ManifestProblem> problems) =>
        string.Join(", ", problems.Select(problem => problem.Code).Distinct(StringComparer.Ordinal));
}
