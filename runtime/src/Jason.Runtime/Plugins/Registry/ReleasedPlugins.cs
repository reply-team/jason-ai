using System.Reflection;
using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Contracts.Update;
using Jason.Runtime.Plugins.Manifest;

namespace Jason.Runtime.Plugins.Registry;

/// <summary>A plugin package a release publishes: its id, the version its manifest names, and its content digest.</summary>
public sealed record ReleasedPlugin(string Id, string Version, string Digest);

/// <summary>
/// The plugin packages this build's release publishes beside its archives, and how the active snapshot stands against
/// each. The build carries the packages' files as resources, so the yardstick is the release this runtime is, read with
/// no network and no child: a person installs the package of the release they run, and whether a newer release exists
/// is the update check's to say.
/// </summary>
/// <remarks>
/// The files are hashed by <see cref="PackageDigest"/> itself, once per process and on the first ask, so the digest here
/// is the one a reload computes over the same files unpacked. Nothing is ever written from the resources: they are read
/// for their digest and their manifest's version, and a package is installed only by a person.
/// </remarks>
public sealed class ReleasedPlugins
{
    /// <summary>Where the build puts each package's files: <c>ReleasedPlugins/&lt;id&gt;/&lt;relative path&gt;</c>.</summary>
    public const string ResourcePrefix = "ReleasedPlugins/";

    private readonly Lazy<IReadOnlyList<ReleasedPlugin>> _packages;

    public ReleasedPlugins(Func<IReadOnlyList<ReleasedPlugin>> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        _packages = new Lazy<IReadOnlyList<ReleasedPlugin>>(read, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The packages this build carries.</summary>
    public static ReleasedPlugins Embedded() => new(() => Read(typeof(ReleasedPlugins).Assembly));

    /// <summary>Packages named outright, for a test that needs a release of its own.</summary>
    public static ReleasedPlugins Of(params ReleasedPlugin[] packages) => new(() => packages);

    public IReadOnlyList<ReleasedPlugin> Packages => _packages.Value;

    /// <summary>One entry per released package, in id order, against the package of that id the snapshot holds.</summary>
    public IReadOnlyList<ReleasedPluginInfo> Compare(PluginSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return [.. Packages.Select(released =>
        {
            var installed = snapshot.Find(released.Id);
            return new ReleasedPluginInfo(
                released.Id,
                State(released, installed),
                new PluginPackageInfo(released.Version, released.Digest),
                installed is null ? null : new PluginPackageInfo(installed.Manifest.Version, installed.Digest));
        })];
    }

    private static ReleasedPluginState State(ReleasedPlugin released, LoadedPlugin? installed)
    {
        if (installed is null)
        {
            return ReleasedPluginState.NotInstalled;
        }

        if (string.Equals(installed.Digest, released.Digest, StringComparison.Ordinal))
        {
            return ReleasedPluginState.Current;
        }

        // Lower is an earlier release's package. The same version or a higher one with other bytes is a package built
        // or edited here, which is the person's choice and is said as such rather than as something to repair.
        return SemanticVersion.TryParse(installed.Manifest.Version, out var mine)
            && SemanticVersion.TryParse(released.Version, out var theirs)
            && mine < theirs
                ? ReleasedPluginState.Behind
                : ReleasedPluginState.Differs;
    }

    /// <summary>The files of each package the assembly carries, grouped by the id that leads their resource name.</summary>
    internal static IReadOnlyList<ReleasedPlugin> Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var files = Files(assembly);
        return [.. files
            .GroupBy(file => file.Id, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => Package(assembly, group.Key, [.. group]))];
    }

    /// <summary>The files of one package as the digest reads them, from the resources of <paramref name="assembly"/>.</summary>
    internal static IReadOnlyList<PackageFile> PackageFiles(Assembly assembly, string id)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return [.. Files(assembly).Where(file => file.Id == id).Select(file => File(assembly, file))];
    }

    private static ReleasedPlugin Package(Assembly assembly, string id, IReadOnlyList<ResourceFile> files)
    {
        var digest = PackageDigest.Compute(files.Select(file => File(assembly, file))).Digest;

        var manifest = files.SingleOrDefault(file => file.Path == "plugin.yaml")
            ?? throw new InvalidOperationException($"The build carries the package '{id}' without a plugin.yaml.");
        using var reader = new StreamReader(Open(assembly, manifest.Resource));
        var version = YamlToJson.Convert(reader.ReadToEnd())?["version"]?.GetValue<string>()
            ?? throw new InvalidOperationException($"The plugin.yaml the build carries for '{id}' names no version.");

        return new ReleasedPlugin(id, version, digest);
    }

    private static PackageFile File(Assembly assembly, ResourceFile file)
    {
        using var measure = Open(assembly, file.Resource);
        return new PackageFile(file.Path, measure.Length, () => Open(assembly, file.Resource));
    }

    private static List<ResourceFile> Files(Assembly assembly)
    {
        var files = new List<ResourceFile>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            // The build names a resource by the file's directory under the package as the platform writes it, so a
            // backslash on Windows is a separator here like a slash anywhere else.
            var rest = resource[ResourcePrefix.Length..].Replace('\\', '/');
            var slash = rest.IndexOf('/', StringComparison.Ordinal);
            if (slash > 0 && slash < rest.Length - 1)
            {
                files.Add(new ResourceFile(rest[..slash], rest[(slash + 1)..], resource));
            }
        }

        return files;
    }

    private static Stream Open(Assembly assembly, string resource) =>
        assembly.GetManifestResourceStream(resource)
        ?? throw new InvalidOperationException($"The resource '{resource}' is listed and cannot be opened.");

    private sealed record ResourceFile(string Id, string Path, string Resource);
}
