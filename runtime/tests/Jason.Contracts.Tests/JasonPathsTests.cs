using Jason.Contracts.Discovery;

namespace Jason.Contracts.Tests;

public class JasonPathsTests
{
    [Fact]
    public void Layout_is_derived_from_the_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-paths-test");
        var paths = new JasonPaths(root);
        Assert.Equal(Path.GetFullPath(root), paths.Root);
        Assert.Equal(Path.Combine(paths.Root, "state", "jason.db"), paths.DatabaseFile);
        Assert.Equal(Path.Combine(paths.Root, "state", "backups"), paths.BackupsDirectory);
        Assert.Equal(Path.Combine(paths.Root, "config", "settings.json"), paths.UserSettingsFile);
        Assert.Equal(Path.Combine(paths.Root, "run", "runtime.json"), paths.DescriptorFile);
        Assert.Equal(Path.Combine(paths.Root, "run", "runtime.lock"), paths.LockFile);
        Assert.Equal(Path.Combine(paths.Root, "plugins"), paths.PluginsDirectory);
        Assert.Equal(Path.Combine(paths.Root, "logs"), paths.LogsDirectory);
        Assert.Equal(5, paths.Layout.Count());
    }

    [Fact]
    public void Default_root_is_dot_jason_under_the_user_profile()
    {
        var previous = Environment.GetEnvironmentVariable(JasonPaths.DataDirectoryVariable);
        try
        {
            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, null);
            var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jason");
            Assert.Equal(Path.GetFullPath(expected), JasonPaths.FromEnvironment().Root);

            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, Path.Combine(Path.GetTempPath(), "jason-override"));
            Assert.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "jason-override")), JasonPaths.FromEnvironment().Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, previous);
        }
    }
}
