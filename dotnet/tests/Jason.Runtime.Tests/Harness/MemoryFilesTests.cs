using Jason.Contracts.Execution;
using Jason.Contracts.Harness;
using Jason.Runtime.Harness;

namespace Jason.Runtime.Tests.Harness;

/// <summary>
/// Where the person's memory files are, as a claim decides it: the root's file where the root carries its marker, the
/// project's and the campaign's in the folders recorded on them. The claim decides it once, beside the command that may
/// read them, and the envelope names what it decided (<c>AgentPreflightTests</c> holds the claim to that).
/// </summary>
public sealed class MemoryFilesTests : IDisposable
{
    private readonly TempTree _tree = new();

    private string Home => Path.Combine(_tree.Root, "home");

    public void Dispose() => _tree.Dispose();

    /// <summary>
    /// The client's memory file is named where Jason knows the root is its own, and that is known by the root's marker
    /// alone, which no record holds: a folder there with no marker, one marked as something else — a project, or a root
    /// that names an id, which no root marker this runtime writes does — or none at all names nothing. A marker the disk
    /// will not give up, held open by another program or denied to this account, names nothing either, and the launch
    /// goes ahead without it. The marker is all that is read — the root marked here has no <c>AGENTS.md</c>, and its path
    /// is given all the same — and nothing is made, taken or written at the root by a claim.
    /// </summary>
    [Theory]
    [InlineData("marked", true)]
    [InlineData("unmarked", false)]
    [InlineData("marked as a project", false)]
    [InlineData("marked as a root that names an id", false)]
    [InlineData("marked, and the marker cannot be read", false)]
    [InlineData("absent", false)]
    [Critical(Family.W7, "the client's memory file is named only where the root carries its marker")]
    public void The_client_s_memory_file_is_named_only_where_the_root_carries_its_marker(string root, bool named)
    {
        var folders = TestHarness.Folders(Home);
        var at = folders.Root;
        switch (root)
        {
            case "marked":
            case "marked, and the marker cannot be read":
                Mark(at, new HarnessMarker(HarnessMarker.Root, null));
                break;
            case "unmarked":
                Directory.CreateDirectory(at);
                break;
            case "marked as a project":
                Mark(at, new HarnessMarker(HarnessMarker.Project, "prj_one"));
                break;
            case "marked as a root that names an id":
                Mark(at, new HarnessMarker(HarnessMarker.Root, "prj_one"));
                break;
        }

        var before = Entries(at);

        MemoryFileLocations memory;
        using (root == "marked, and the marker cannot be read" ? Unreadable(HarnessMarker.PathIn(at)) : null)
        {
            memory = folders.MemoryFiles(null, null);
        }

        Assert.NotNull(memory);
        Assert.Equal(named ? Path.Combine(at, "AGENTS.md") : null, memory.Client);
        Assert.Equal(before, Entries(at));
    }

    /// <summary>
    /// A project's and a campaign's memory files are where their folders were recorded, and a level with no folder
    /// recorded is null beside the others, so the object is there whatever is known. A recorded folder is named without
    /// a look: the folders recorded here were never made, and a stale one is the host's to find.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [Critical(Family.W7, "the memory files are the recorded folders, never looked at, and the object is always there")]
    public void A_project_s_and_a_campaign_s_memory_files_are_their_recorded_folders_named_without_a_look(bool project, bool campaign)
    {
        var projectFolder = Path.Combine(Home, "Jason", "projects", "acme-corp");
        var campaignFolder = Path.Combine(projectFolder, "campaigns", "q3-marketing-leads");

        var memory = TestHarness.Folders(Home).MemoryFiles(project ? projectFolder : null, campaign ? campaignFolder : null);

        Assert.NotNull(memory);
        Assert.Null(memory.Client);
        Assert.Equal(project ? Path.Combine(projectFolder, "AGENTS.md") : null, memory.Project);
        Assert.Equal(campaign ? Path.Combine(campaignFolder, "AGENTS.md") : null, memory.Campaign);
        Assert.False(Directory.Exists(projectFolder), $"deciding the memory files made the folder recorded at '{projectFolder}'");
    }

    /// <summary>A folder at <paramref name="folder"/> with <paramref name="marker"/> as its marker, and nothing else in it.</summary>
    private static void Mark(string folder, HarnessMarker marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HarnessMarker.PathIn(folder))!);
        File.WriteAllBytes(HarnessMarker.PathIn(folder), marker.Bytes());
    }

    /// <summary>
    /// <paramref name="file"/> kept from this account until disposed: on Windows held open for this test alone, so a read
    /// meets a sharing violation, as it does while another program holds the file; elsewhere stripped of every permission,
    /// so a read meets a denial. An account that reads a file whatever its mode says, as root does, has no denial to meet.
    /// </summary>
    private static IDisposable Unreadable(string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        var denied = DenyByMode(file);
        if (Readable(file))
        {
            denied.Dispose();
            Assert.Skip("This account reads a file whatever its mode says, as root does, so there is no denial to meet.");
        }

        return denied;
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static Lifted DenyByMode(string file)
    {
        var mode = File.GetUnixFileMode(file);
        File.SetUnixFileMode(file, UnixFileMode.None);
        return new Lifted(() => LiftByMode(file, mode));
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void LiftByMode(string file, UnixFileMode mode) => File.SetUnixFileMode(file, mode);

    private static bool Readable(string file)
    {
        try
        {
            using var read = File.OpenRead(file);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed class Lifted(Action lift) : IDisposable
    {
        public void Dispose() => lift();
    }

    /// <summary>Everything below <paramref name="folder"/>, by its path there, with each file's size; empty where there is no folder.</summary>
    private static List<string> Entries(string folder) =>
        Directory.Exists(folder)
            ? [.. new DirectoryInfo(folder).EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                .Select(entry => $"{Path.GetRelativePath(folder, entry.FullName)} {(entry as FileInfo)?.Length}")
                .Order(StringComparer.Ordinal)]
            : [];
}
