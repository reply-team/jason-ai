using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Contracts.Ui;

namespace Jason.Runtime.Ui;

/// <summary>
/// Which directory the UI is served from, read at every ask: custom when it is a build (an index and a manifest),
/// else default when that is, else nothing. Nothing is cached, so a build dropped into custom is served by the
/// next request and deleting it brings the default back. Choosing reads the index and the manifest alone; the
/// digest, which hashes every file, is computed only by <see cref="Active"/>, which system.info asks.
/// </summary>
public sealed class UiModule(JasonPaths paths)
{
    public const string Custom = "custom";

    public const string Default = "default";

    /// <summary>The build served now, with its digest, or null when nothing is.</summary>
    public UiModuleInfo? Active()
    {
        var chosen = Choose();
        return chosen is null ? null : WithDigest(chosen);
    }

    /// <summary>The directory served now, or null.</summary>
    public string? ActiveDirectory() => Choose()?.Directory;

    /// <summary>
    /// The file a request for <paramref name="relative"/> is answered with, or null: nothing outside the directory, no
    /// hidden file, no directory, no index fallback. An empty path is the index.
    /// </summary>
    public string? Resolve(string relative)
    {
        ArgumentNullException.ThrowIfNull(relative);
        var directory = ActiveDirectory();
        if (directory is null)
        {
            return null;
        }

        if (relative.Length == 0)
        {
            relative = UiAssetLayout.IndexFile;
        }

        if (relative.Contains('\\', StringComparison.Ordinal) || Path.IsPathRooted(relative)
            || relative.Split('/').Any(segment => segment.Length == 0 || segment.StartsWith('.')))
        {
            return null;
        }

        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full) ? full : null;
    }

    /// <summary>
    /// Why nothing is served: where a build goes, and what is wrong with the custom directory when it holds something
    /// that is not one, so "I unzipped it, why is it not installed" has an answer.
    /// </summary>
    public string NotInstalled()
    {
        var message = $"No UI is installed: nothing to serve at {paths.UiDefaultDirectory} or {paths.UiCustomDirectory}. "
            + $"Run 'jason ui install' to place this release's build, or unzip a build of the UI ({UiAssetLayout.AssetName}) into {paths.UiCustomDirectory}.";
        return CustomProblem(Describe(Custom, paths.UiCustomDirectory)) is { } problem ? message + " " + problem : message;
    }

    private UiModuleInfo? Choose()
    {
        var custom = Describe(Custom, paths.UiCustomDirectory);
        if (custom is { Problem: null })
        {
            return custom;
        }

        var served = Describe(Default, paths.UiDefaultDirectory);
        if (served is null)
        {
            return null;
        }

        // A custom directory that was skipped is said on the one that is served, so "why is my build not showing" has an answer.
        return CustomProblem(custom) is { } problem ? served with { Problem = problem } : served;
    }

    /// <summary>What keeps the custom directory from being served: its own problem, or files with no index among them.</summary>
    private string? CustomProblem(UiModuleInfo? custom)
    {
        if (custom is not null)
        {
            return custom.Problem;
        }

        var directory = paths.UiCustomDirectory;
        return Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()
            ? $"'{directory}' holds files but no {UiAssetLayout.IndexFile}, so it is not served."
            : null;
    }

    /// <summary>Null when the directory is not there or holds no index; a problem when it holds an index but is not a build.</summary>
    private static UiModuleInfo? Describe(string source, string directory)
    {
        if (!File.Exists(Path.Combine(directory, UiAssetLayout.IndexFile)))
        {
            return null;
        }

        UiManifest? manifest = null;
        string? problem = null;
        try
        {
            manifest = UiManifest.Read(directory);
        }
        catch (UiManifestException refused)
        {
            problem = refused.Message;
        }

        if (manifest is null && problem is null)
        {
            problem = $"'{directory}' holds an {UiAssetLayout.IndexFile} but no {UiAssetLayout.ManifestFile}, so it is not served.";
        }

        return new UiModuleInfo(
            source,
            directory,
            manifest?.Version,
            manifest?.ApiVersion,
            manifest is not null && manifest.ApiVersion == ApiVersion.Current,
            Digest: null,
            problem);
    }

    /// <summary>The digest hashes every file of the build, so it is computed for a report and never for a request.</summary>
    private static UiModuleInfo WithDigest(UiModuleInfo info)
    {
        try
        {
            return info with { Digest = PackageDigest.Compute(info.Directory).Digest };
        }
        catch (PackageTooLargeException)
        {
            return info with
            {
                Problem = info.Problem ?? $"'{info.Directory}' holds more than {PackageDigest.MaxFiles} files or {PackageDigest.MaxBytes} bytes, so no digest is reported for it.",
            };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return info with { Problem = info.Problem ?? $"The digest of '{info.Directory}' could not be computed: {exception.Message}" };
        }
    }
}
