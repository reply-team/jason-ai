using Jason.Contracts.Api;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Manifest;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Tests.Documentation;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The packages this build's release publishes, as the build carries them, and how an installed package stands against
/// each: the same digest is current, an earlier version with other bytes is behind, anything else with other bytes
/// differs, and no package of that id is not installed.
/// </summary>
public class ReleasedPluginsTests
{
    private const string Released = "sha256:released";

    private static readonly ReleasedPlugins Release = ReleasedPlugins.Of(new ReleasedPlugin("reply", "0.7.0", Released));

    /// <summary>
    /// The files the build carries are the package in the tree, byte for byte: the build embeds the directory by a glob,
    /// and this holds the glob to the directory, so a file added to the package cannot be left out of the binary.
    /// </summary>
    [Fact]
    public void The_build_carries_the_reply_package_as_the_tree_holds_it()
    {
        var tree = Path.Combine(Publication.RepositoryRoot(), "plugins", "reply");
        var carried = ReleasedPlugins.PackageFiles(typeof(ReleasedPlugins).Assembly, "reply");

        Assert.Equal(PackageDigest.Files(tree), carried.Select(file => file.Path).Order(StringComparer.Ordinal));
        Assert.Equal(PackageDigest.Compute(tree).Digest, PackageDigest.Compute(carried).Digest);

        var package = Assert.Single(ReleasedPlugins.Embedded().Packages);
        Assert.Equal("reply", package.Id);
        Assert.Equal(PackageDigest.Compute(tree).Digest, package.Digest);
        var manifest = YamlToJson.Convert(File.ReadAllText(Path.Combine(tree, "plugin.yaml")))!;
        Assert.Equal(manifest["version"]!.GetValue<string>(), package.Version);
    }

    [Theory]
    [InlineData("0.7.0", Released, ReleasedPluginState.Current)]
    [InlineData("0.6.1", "sha256:earlier", ReleasedPluginState.Behind)]
    [InlineData("0.7.0", "sha256:edited", ReleasedPluginState.Differs)]
    [InlineData("0.8.0-dev", "sha256:built-here", ReleasedPluginState.Differs)]
    public void An_installed_package_stands_against_the_release_by_digest_then_version(string version, string digest, ReleasedPluginState state)
    {
        var installed = Installed("reply", version, digest);

        var entry = Assert.Single(Release.Compare(Snapshot(installed)));

        Assert.Equal(new ReleasedPluginInfo("reply", state, new PluginPackageInfo("0.7.0", Released), new PluginPackageInfo(version, digest)), entry);
    }

    /// <summary>A dev build compares against its own tree: a released package under a later dev tree is behind it.</summary>
    [Fact]
    public void A_released_package_under_a_dev_tree_is_behind_it()
    {
        var dev = ReleasedPlugins.Of(new ReleasedPlugin("reply", "0.7.1-dev", "sha256:dev-tree"));

        var entry = Assert.Single(dev.Compare(Snapshot(Installed("reply", "0.7.0", Released))));

        Assert.Equal(ReleasedPluginState.Behind, entry.State);
    }

    [Fact]
    public void No_package_of_that_id_is_not_installed()
    {
        var entry = Assert.Single(Release.Compare(Snapshot(Installed("other-provider", "0.7.0", Released))));

        Assert.Equal(new ReleasedPluginInfo("reply", ReleasedPluginState.NotInstalled, new PluginPackageInfo("0.7.0", Released), null), entry);
    }

    [Fact]
    public void A_version_that_is_not_a_semantic_version_differs_rather_than_trails()
    {
        var entry = Assert.Single(Release.Compare(Snapshot(Installed("reply", "seven", "sha256:other"))));

        Assert.Equal(ReleasedPluginState.Differs, entry.State);
    }

    private static LoadedPlugin Installed(string id, string version, string digest)
    {
        var plugin = TestPlugins.Loaded(id);
        return plugin with { Manifest = plugin.Manifest with { Version = version }, Digest = digest };
    }

    private static PluginSnapshot Snapshot(params LoadedPlugin[] plugins) =>
        PluginSnapshot.Empty(DateTime.UnixEpoch, SnapshotSource.Reload) with { Plugins = plugins };
}
