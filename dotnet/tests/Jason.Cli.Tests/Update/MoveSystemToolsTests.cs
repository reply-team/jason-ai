using Jason.Cli.Update;
using Jason.Contracts.Discovery;

namespace Jason.Cli.Tests.Update;

/// <summary>The programs a move into Jason Desktop starts, by the paths it names them with. Nothing here starts one.</summary>
public class MoveSystemToolsTests
{
    /// <summary>
    /// Every program a move starts is named by its full path: the three tools macOS keeps where it keeps them, from the
    /// root, and the Windows setup by the file it was downloaded to under the data directory, which is a full path however
    /// the data directory was spelled.
    /// </summary>
    [Fact]
    public void Every_program_a_move_starts_is_named_by_its_full_path()
    {
        Assert.Equal(["/usr/sbin/sysctl", "/usr/bin/codesign", "/usr/bin/ditto"], [MoveSystem.Sysctl, MoveSystem.Codesign, MoveSystem.Ditto]);
        Assert.True(Path.IsPathFullyQualified(new UpdatePaths(new JasonPaths("relative-data")).MoveDownloads));
    }

    /// <summary>
    /// Linux: apt-get, dpkg, sudo and pkexec, which the move and the deb's uninstall run as root with the person's password,
    /// are named by the paths Debian and Ubuntu keep them at, the install line's, and never looked up on the PATH, which
    /// decides nothing: whether there is apt is whether those files are there. Catches a PATH that hands sudo or pkexec
    /// another program under apt's name.
    /// </summary>
    [Fact]
    public void On_linux_the_tools_run_as_root_are_the_system_s_own_by_their_paths()
    {
        Assert.Equal(["/usr/bin/apt-get", "/usr/bin/dpkg", "/usr/bin/sudo", "/usr/bin/pkexec"], [MoveSystem.AptGet, MoveSystem.Dpkg, MoveSystem.Sudo, MoveSystem.Pkexec]);
        var source = File.ReadAllText(Path.Combine(Repository(), "dotnet", "src", "Jason.Cli", "Update", "MoveSystem.cs"));
        foreach (var tool in new[] { "apt-get", "dpkg", "sudo", "pkexec" })
        {
            Assert.DoesNotContain($"OnPath(\"{tool}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("public bool HasApt() => File.Exists(AptGet) && File.Exists(Dpkg);", source, StringComparison.Ordinal);
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
