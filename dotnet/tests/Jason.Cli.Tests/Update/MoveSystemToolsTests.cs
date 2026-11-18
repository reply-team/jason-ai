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
}
