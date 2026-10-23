using Jason.Contracts.Harness;

namespace Jason.Contracts.Tests.Harness;

/// <summary>
/// The one walk up every look for a level around a path takes, and the one question it asks of each folder: whether a
/// marker is there. The runtime refusing a project's folder inside another level's, the CLI finding the project of the
/// folder a shell stands in, and the purge refusing a data directory inside a level all walk this way.
/// </summary>
public sealed class FolderWalkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-contracts-tests", Guid.NewGuid().ToString("N"));

    public FolderWalkTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>The folder itself first, then each one above it, to the top of the file system and no further.</summary>
    [Fact]
    public void The_walk_goes_from_the_folder_up_to_the_top_of_the_file_system()
    {
        var start = Path.Combine(_root, "a", "b");

        var walked = FolderPaths.AndAbove(start + Path.DirectorySeparatorChar).ToList();

        Assert.True(walked.Count > 3, $"the walk from '{start}' went {walked.Count} folder(s): {string.Join(" | ", walked)}");
        Assert.Equal(new[] { start, Path.Combine(_root, "a"), _root }, walked.Take(3));
        Assert.Equal(Path.GetPathRoot(start), walked[^1]);
        for (var at = 1; at < walked.Count; at++)
        {
            Assert.True(Path.GetDirectoryName(walked[at - 1]) == walked[at], $"'{walked[at]}' is not the folder above '{walked[at - 1]}'.");
        }
    }

    /// <summary>
    /// A marker is there or not by its file, never by what it says: one that is not a marker's JSON and one another
    /// program holds open with no sharing are as there as a good one, and a folder without one has none.
    /// </summary>
    [Fact]
    public void A_marker_is_known_by_being_there_read_or_not()
    {
        var good = Marked("good", new HarnessMarker(HarnessMarker.Root, null).Bytes());
        var unreadable = Marked("unreadable", "not a marker"u8.ToArray());
        var held = Marked("held", new HarnessMarker(HarnessMarker.Project, "prj_01JB0000000000000000000000").Bytes());
        var none = Path.Combine(_root, "none");
        Directory.CreateDirectory(none);

        using var holding = new FileStream(HarnessMarker.PathIn(held), FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.True(HarnessMarker.IsIn(good), $"'{good}' holds a marker and was not found to.");
        Assert.True(HarnessMarker.IsIn(unreadable), $"'{unreadable}' holds a marker nobody can read and was not found to.");
        Assert.True(HarnessMarker.IsIn(held), $"'{held}' holds a marker another program holds open and was not found to.");
        Assert.False(HarnessMarker.IsIn(none), $"'{none}' holds no marker and was found to.");
        Assert.False(HarnessMarker.IsIn(Path.Combine(_root, "not-there")), "a folder that is not there was found to hold a marker.");
    }

    private string Marked(string name, byte[] bytes)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.Combine(folder, HarnessMarker.Directory));
        File.WriteAllBytes(HarnessMarker.PathIn(folder), bytes);
        return folder;
    }
}
