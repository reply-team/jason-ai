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
/// that machine, or a version before the list's first line, 0.3.2 — which <c>jason status</c> judges by its version — and
/// the check never names it. A package that cannot be read is passed over in silence: what is wrong on the installed side
/// is <c>jason status</c>'s to say.
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

        var behind = new List<PluginUpdate>();
        foreach (var (id, package) in manifest.Plugins.OrderBy(plugin => plugin.Key, StringComparer.Ordinal))
        {
            if (InstalledDigest(paths, id) is { } digest
                && package.VersionOf(digest) is { } installed
                && installed < package.Version)
            {
                behind.Add(new PluginUpdate(id, installed.ToString(), package.Version.ToString()));
            }
        }

        return behind;
    }

    /// <summary>The digest of the package installed as <paramref name="id"/>, or null when none is installed or it cannot be read.</summary>
    private static string? InstalledDigest(JasonPaths paths, string id)
    {
        var directory = paths.PluginPackageDirectory(id);
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
