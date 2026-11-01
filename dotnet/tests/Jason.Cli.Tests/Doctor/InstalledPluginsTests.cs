using Jason.Cli.Doctor;
using Jason.Contracts.Discovery;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Doctor;

/// <summary>
/// Which plugins are installed here, as the settings keys that name one are told: the directories the runtime calls
/// installed, listed by the command-line client, which cannot ask the runtime and must not start it to know.
/// </summary>
public sealed class InstalledPluginsTests
{
    [Fact]
    public void Every_directory_that_holds_a_manifest_is_an_installed_plugin_by_its_name()
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(tree.Root);
        Package(paths, "reply");
        Package(paths, "fake-provider");

        Assert.Equal(["fake-provider", "reply"], InstalledPlugins.Ids(paths).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A directory without a manifest is not a plugin: a folder made by hand, or a download half unpacked, is named by
    /// whatever somebody typed, and a key that matches it is no more a plugin's id than any other.
    /// </summary>
    [Fact]
    public void A_directory_without_a_manifest_is_not_a_plugin()
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(tree.Root);
        Directory.CreateDirectory(Path.Combine(paths.PluginsDirectory, "half-installed"));
        Package(paths, "complete");

        Assert.Equal(["complete"], InstalledPlugins.Ids(paths));
    }

    /// <summary>Nor is one named the way no plugin's id can be, manifest or not.</summary>
    [Theory]
    [InlineData("UPPER")]
    [InlineData("under_score")]
    [InlineData("9lives")]
    [InlineData("x")]
    public void A_directory_whose_name_no_plugin_can_have_is_not_a_plugin(string name)
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(tree.Root);
        Package(paths, name);
        Package(paths, "reply");

        Assert.Equal(["reply"], InstalledPlugins.Ids(paths));
    }

    [Fact]
    public void A_file_and_a_directory_that_starts_with_a_dot_are_not_plugins()
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(tree.Root);
        Package(paths, "reply");
        Package(paths, ".git");
        File.WriteAllText(Path.Combine(paths.PluginsDirectory, "notes.txt"), "mine");

        Assert.Equal(["reply"], InstalledPlugins.Ids(paths));
    }

    [Fact]
    public void Plugin_names_are_compared_without_regard_to_case_as_settings_keys_are()
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(tree.Root);
        Package(paths, "reply");

        Assert.Contains("REPLY", InstalledPlugins.Ids(paths));
    }

    [Fact]
    public void A_data_directory_with_no_plugins_directory_has_no_plugins()
    {
        using var tree = new TempTree();

        Assert.Empty(InstalledPlugins.Ids(new JasonPaths(tree.Root)));
    }

    /// <summary>A plugins directory that is a file where a directory belongs cannot be listed, and a listing that cannot be read names nothing.</summary>
    [Fact]
    public void A_plugins_directory_that_cannot_be_listed_has_no_plugins()
    {
        using var tree = new TempTree();
        var paths = new JasonPaths(tree.Root);
        File.WriteAllText(paths.PluginsDirectory, "not a directory");

        Assert.Empty(InstalledPlugins.Ids(paths));
    }

    /// <summary>A package directory: the directory, and the manifest that makes it one.</summary>
    private static void Package(JasonPaths paths, string name)
    {
        Directory.CreateDirectory(Path.Combine(paths.PluginsDirectory, name));
        File.WriteAllText(Path.Combine(paths.PluginsDirectory, name, "plugin.yaml"), $"id: {name}");
    }
}
