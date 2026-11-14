using Jason.Cli.Doctor;
using Jason.Contracts.Discovery;
using Jason.Runtime.Configuration;
using Jason.Runtime.Plugins.Registry;

namespace Jason.Runtime.Tests.Configuration;

/// <summary>
/// The command-line client's list of installed plugins held to the runtime's. The client lists the directories itself
/// because it cannot reference the runtime, so its listing is a copy of the runtime's, and the two have to name the same
/// plugins or a bundle describes a settings file by keys the runtime read as something else. What an id has to be is not
/// copied: both read it from the one rule in the contracts, so what is held here is the listing around it — which
/// directories, and that each holds a manifest.
/// </summary>
public class InstalledPluginsAgreementTests
{
    /// <summary>
    /// A directory with a manifest and an id's name, one without a manifest, and one with a manifest under each kind of
    /// name no plugin can have: upper case, a dot, a space, an underscore, a leading digit or dash, one letter alone, and
    /// one letter too many.
    /// </summary>
    [Fact]
    public void The_client_names_the_plugins_the_runtime_names()
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(tree.Root, 1);
        var longest = "a" + new string('b', 63);
        var packages = new[] { "reply", "fake-provider", longest, "UPPER", ".hidden", "a.b", "with space", "under_score", "9lives", "-dash", "x", longest + "c" };
        foreach (var name in packages)
        {
            Directory.CreateDirectory(Path.Combine(paths.PluginsDirectory, name));
            File.WriteAllText(Path.Combine(paths.PluginsDirectory, name, PackageScanner.ManifestFileName), $"id: {name}");
        }

        Directory.CreateDirectory(Path.Combine(paths.PluginsDirectory, "no-manifest"));
        Directory.CreateDirectory(Path.Combine(paths.PluginsDirectory, "manifest-is-a-directory", PackageScanner.ManifestFileName));
        File.WriteAllText(Path.Combine(paths.PluginsDirectory, "stray-file"), "not a package");

        var installed = SafeBinding.InstalledPluginIds(paths).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(installed, InstalledPlugins.Ids(paths).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "fake-provider", "reply", longest }.Order(StringComparer.Ordinal), installed);
    }

    [Fact]
    public void Neither_names_anything_where_there_is_no_plugins_directory()
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(tree.Root, 1);

        Assert.Empty(SafeBinding.InstalledPluginIds(paths));
        Assert.Empty(InstalledPlugins.Ids(paths));
    }
}
