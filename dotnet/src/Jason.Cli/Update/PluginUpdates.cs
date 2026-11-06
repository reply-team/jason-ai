using System.Security;
using Jason.Cli.Doctor;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;

namespace Jason.Cli.Update;

/// <summary>
/// The plugin packages installed here that a newer release publishes a later version of, read from the release's
/// manifest and the plugins directory and from nothing else: the check asks the runtime nothing, so it answers on a
/// machine whose runtime will not start.
/// </summary>
/// <remarks>
/// An installed package is named by its content, as the runtime names it: its digest, looked up in the release's record
/// of each version's digest. A digest the record does not hold is a package the list does not know: built or edited on
/// that machine, or a version before the first line of that package's list (the Reply package's is 0.3.2, the forwarder's 1.0.0) — which <c>jason status</c> judges by its version — and
/// the check never names it. A package that cannot be read is passed over in silence: what is wrong on the installed side
/// is <c>jason status</c>'s to say. The <c>plugins</c> step of <c>jason update apply</c> reads the same plan, so the check
/// names exactly the packages the apply replaces.
/// </remarks>
internal static class PluginUpdates
{
    /// <summary>
    /// Null when <paramref name="manifest"/> is not newer than this installation or names no package; otherwise the
    /// packages behind it, in id order, which is empty when nothing installed here that the record holds is behind.
    /// </summary>
    public static IReadOnlyList<PluginUpdate>? Behind(UpdateManifest manifest, bool available, JasonPaths paths)
    {
        if (!available || manifest.Plugins.Count == 0)
        {
            return null;
        }

        return [.. Plan(manifest, paths)
            .Where(plugin => plugin.State == PluginPlanState.Behind)
            .Select(plugin => new PluginUpdate(plugin.Id, plugin.Installed!.Value.ToString(), plugin.Package.Version.ToString()))];
    }

    /// <summary>
    /// Every package <paramref name="manifest"/> names, in id order, with what is installed as it and what that makes it.
    /// The one reading of the plugins directory: the check names exactly the packages the update's <c>plugins</c> step
    /// replaces, because both ask this.
    /// </summary>
    public static IReadOnlyList<PluginPlan> Plan(UpdateManifest manifest, JasonPaths paths)
    {
        var plan = new List<PluginPlan>();
        foreach (var (id, package) in manifest.Plugins.OrderBy(plugin => plugin.Key, StringComparer.Ordinal))
        {
            var directory = paths.PluginPackageDirectory(id);
            if (!Path.Exists(directory))
            {
                plan.Add(new PluginPlan(id, package, PluginPlanState.NotInstalled, null, null));
                continue;
            }

            var digest = InstalledDigest(directory);
            var installed = digest is null ? null : package.VersionOf(digest);
            var state = digest is null ? PluginPlanState.Unreadable
                : installed is null ? PluginPlanState.Differs
                : installed < package.Version ? PluginPlanState.Behind
                : installed == package.Version ? PluginPlanState.Current
                : PluginPlanState.Later;
            plan.Add(new PluginPlan(id, package, state, digest, installed));
        }

        return plan;
    }

    /// <summary>The digest of the package in <paramref name="directory"/>, or null when it holds no manifest or cannot be read.</summary>
    public static string? InstalledDigest(string directory)
    {
        try
        {
            return File.Exists(Path.Combine(directory, InstalledPlugins.ManifestFileName))
                ? PackageDigest.Compute(directory).Digest
                : null;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException or SecurityException or PackageTooLargeException)
        {
            return null;
        }
    }
}

/// <summary>What the package installed as an id a release names is, beside the release's own.</summary>
internal enum PluginPlanState
{
    /// <summary>Nothing is installed as that id.</summary>
    NotInstalled,

    /// <summary>Something is there that cannot be read as a package: no manifest, or a file that will not open.</summary>
    Unreadable,

    /// <summary>A package whose digest the release's record does not hold: built or edited on this machine, or older than the record.</summary>
    Differs,

    /// <summary>An earlier version the record holds: the one the update replaces.</summary>
    Behind,

    /// <summary>The release's own.</summary>
    Current,

    /// <summary>A later version the record holds, as after an apply of an older release by <c>--version</c>.</summary>
    Later,
}

/// <summary>One package a release names, and what is installed as it: its digest and the version the record gives that digest.</summary>
internal sealed record PluginPlan(string Id, UpdatePluginPackage Package, PluginPlanState State, string? InstalledDigest, SemanticVersion? Installed);
