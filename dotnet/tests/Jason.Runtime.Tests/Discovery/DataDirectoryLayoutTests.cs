using Jason.Runtime.Discovery;

namespace Jason.Runtime.Tests.Discovery;

public class DataDirectoryLayoutTests
{
    [Fact]
    public void Creates_every_directory_and_is_idempotent()
    {
        using var dir = new TempDataDir();

        DataDirectoryLayout.Ensure(dir.Paths);
        DataDirectoryLayout.Ensure(dir.Paths);

        foreach (var expected in dir.Paths.StartDirectories)
        {
            Assert.True(Directory.Exists(expected), expected);
        }

        Assert.True(FilePermissions.IsRestrictedToCurrentUser(dir.Paths.RunDirectory));
    }
}
