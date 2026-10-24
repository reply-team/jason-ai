using Jason.Cli.Tests.Installation;
using Jason.Cli.Uninstall;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// This machine's remover against what a purge really finds in a data directory: files marked read-only, as git writes
/// the pack files of the skill packs it clones there, a directory marked read-only, and a link to somewhere else. Every
/// path is in a temporary directory of the test's own.
/// </summary>
/// <remarks>
/// <para>
/// On Windows a delete refuses a read-only file, and a read-only directory; elsewhere a read-only file in a writable
/// directory goes anyway and a directory without write permission keeps what is in it. So the rows run on every platform,
/// and on each they pin that the tree is gone.
/// </para>
/// <para>
/// What no row can pin: elsewhere than Windows, a file this account may not change the mode of — one root left in a
/// directory of this account's — is reachable only by running as root, and the remover leaves the mode of every file
/// alone there, so the delete meets it as it always did.
/// </para>
/// </remarks>
public sealed class MachineRemoverReadOnlyTests
{
    /// <summary>
    /// A tree the way a skill pack's clone leaves one: a read-only pack file deep down, in a <c>.git</c> marked hidden as Git
    /// for Windows marks it (elsewhere its dot is what hides it), and a read-only directory.
    /// </summary>
    [Fact]
    public void A_tree_holding_read_only_files_and_directories_is_removed()
    {
        using var dir = new TempPaths();
        var tree = Path.Combine(dir.Paths.Root, "skills", "staged");
        var git = Path.Combine(tree, "a-pack", ".git");
        var pack = Path.Combine(git, "objects", "pack");
        Directory.CreateDirectory(pack);
        new DirectoryInfo(git).Attributes |= FileAttributes.Hidden;
        var packFile = Path.Combine(pack, "pack-0123abcd.pack");
        var index = Path.Combine(pack, "pack-0123abcd.idx");
        File.WriteAllText(packFile, "a pack");
        File.WriteAllText(index, "its index");
        File.SetAttributes(packFile, FileAttributes.ReadOnly);
        File.SetAttributes(index, FileAttributes.ReadOnly);
        var locked = Path.Combine(tree, "a-pack", "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "inside.txt"), "in a read-only directory");
        new DirectoryInfo(locked).Attributes |= FileAttributes.ReadOnly;

        try
        {
            var failure = Record.Exception(() => InstallationRemovers.ForThisMachine().RemoveTree(tree));

            Assert.True(failure is null, $"the tree was not removed: {failure}");
            Assert.False(Directory.Exists(tree), $"'{tree}' is still there, holding: {string.Join(", ", Left(tree))}.");
        }
        finally
        {
            Writable(dir.Paths.Root);
        }
    }

    /// <summary>
    /// A link inside the tree is removed as a link: what it points at, outside the tree, keeps its files, their bytes and
    /// their attributes. A junction on Windows, which an ordinary account may make and which .NET's own recursive delete
    /// refuses to unmount there; a symbolic link elsewhere.
    /// </summary>
    [Fact]
    public void A_link_in_the_tree_goes_as_itself_and_what_it_points_at_is_left_as_it_was()
    {
        using var dir = new TempPaths();
        var (outside, kept) = Outside(dir, "outside");
        var tree = Path.Combine(dir.Paths.Root, "work");
        var holder = Path.Combine(tree, "wi_one");
        Directory.CreateDirectory(holder);
        File.WriteAllText(Path.Combine(holder, "beside.txt"), "beside the link");
        var link = Path.Combine(holder, "to-outside");
        DataDirectoryBeltTests.Link(link, outside);

        try
        {
            var failure = Record.Exception(() => InstallationRemovers.ForThisMachine().RemoveTree(tree));

            Assert.True(failure is null, $"the tree was not removed: {failure}");
            Assert.False(Directory.Exists(tree), $"'{tree}' is still there, holding: {string.Join(", ", Left(tree))}.");
            AsItWas(outside, kept);
        }
        finally
        {
            Unlink(link);
            Writable(dir.Paths.Root);
        }
    }

    /// <summary>
    /// A tree that is itself a link — an entry of the data directory linked to a folder somewhere else, a plugin author's
    /// checkout, say — goes as the link it is, and the folder it points at keeps everything in it as it was.
    /// </summary>
    [Fact]
    public void A_tree_that_is_a_link_goes_as_itself_and_what_it_points_at_is_left_as_it_was()
    {
        using var dir = new TempPaths();
        var (outside, kept) = Outside(dir, "checkout");
        var tree = Path.Combine(dir.Paths.Root, "plugins");
        DataDirectoryBeltTests.Link(tree, outside);

        try
        {
            var failure = Record.Exception(() => InstallationRemovers.ForThisMachine().RemoveTree(tree));

            Assert.True(failure is null, $"the link was not removed: {failure}");
            Assert.False(Path.Exists(tree), $"the link at '{tree}' is still there.");
            AsItWas(outside, kept);
        }
        finally
        {
            Unlink(tree);
            Writable(dir.Paths.Root);
        }
    }

    /// <summary>
    /// A symbolic link to a single read-only file outside the tree goes as itself, and the file it points at keeps its
    /// bytes and its mark: elsewhere than Windows a change of mode goes through a link to the file behind it.
    /// </summary>
    [Fact]
    public void A_file_link_in_the_tree_goes_as_itself_and_the_file_it_points_at_is_left_as_it_was()
    {
        using var dir = new TempPaths();
        var (outside, kept) = Outside(dir, "outside");
        var tree = Path.Combine(dir.Paths.Root, "work");
        var holder = Path.Combine(tree, "wi_one");
        Directory.CreateDirectory(holder);
        var link = Path.Combine(holder, "kept.pack");
        Assert.SkipUnless(ManagedLayouts.TryLink(link, kept), ManagedLayouts.NoLinkPrivilege);

        try
        {
            var failure = Record.Exception(() => InstallationRemovers.ForThisMachine().RemoveTree(tree));

            Assert.True(failure is null, $"the tree was not removed: {failure}");
            Assert.False(Directory.Exists(tree), $"'{tree}' is still there, holding: {string.Join(", ", Left(tree))}.");
            AsItWas(outside, kept);
        }
        finally
        {
            Writable(dir.Paths.Root);
        }
    }

    /// <summary>
    /// A link whose target is gone is removed as a link too, and is never taken for an entry that is gone: a look at its
    /// path reads the link itself and finds it there.
    /// </summary>
    [Fact]
    public void A_link_to_nothing_in_the_tree_goes_as_itself()
    {
        using var dir = new TempPaths();
        var gone = Path.Combine(dir.Paths.Root, "gone");
        Directory.CreateDirectory(gone);
        var tree = Path.Combine(dir.Paths.Root, "work");
        var holder = Path.Combine(tree, "wi_one");
        Directory.CreateDirectory(holder);
        var link = Path.Combine(holder, "to-nothing");
        DataDirectoryBeltTests.Link(link, gone);
        Directory.Delete(gone);

        try
        {
            Assert.True(Path.Exists(link), $"the link at '{link}', whose target is gone, does not read as there.");

            var failure = Record.Exception(() => InstallationRemovers.ForThisMachine().RemoveTree(tree));

            Assert.True(failure is null, $"the tree was not removed: {failure}");
            Assert.False(Directory.Exists(tree), $"'{tree}' is still there, holding: {string.Join(", ", Left(tree))}.");
        }
        finally
        {
            Unlink(link);
            Writable(dir.Paths.Root);
        }
    }

    /// <summary>
    /// A file's read-only mark is cleared only on Windows, where it is what refuses the delete; elsewhere a file's mode is
    /// left as it is, since the directory's permission is what decides. A second name of the file — a hard link outside
    /// the tree, as a local clone leaves one — shows which: its bytes stay everywhere, and its mark is cleared on Windows
    /// only, where the mark belongs to the file rather than to either name.
    /// </summary>
    [Fact]
    public void A_file_s_mark_is_cleared_on_windows_only()
    {
        using var dir = new TempPaths();
        var (outside, kept) = Outside(dir, "repo");
        var tree = Path.Combine(dir.Paths.Root, "work");
        var holder = Path.Combine(tree, "wi_one");
        Directory.CreateDirectory(holder);
        HardLink(Path.Combine(holder, "kept.pack"), kept);

        try
        {
            var failure = Record.Exception(() => InstallationRemovers.ForThisMachine().RemoveTree(tree));

            Assert.True(failure is null, $"the tree was not removed: {failure}");
            Assert.False(Directory.Exists(tree), $"'{tree}' is still there, holding: {string.Join(", ", Left(tree))}.");
            Assert.True(File.Exists(kept), $"'{kept}', the file's other name, is gone.");
            var bytes = File.ReadAllText(kept);
            Assert.True(bytes == Kept, $"'{kept}', the file's other name, now holds: {bytes}");
            var attributes = File.GetAttributes(kept);
            Assert.True(
                attributes.HasFlag(FileAttributes.ReadOnly) != OperatingSystem.IsWindows(),
                $"'{kept}' is {attributes} after the removal on {System.Runtime.InteropServices.RuntimeInformation.OSDescription}.");
        }
        finally
        {
            Writable(dir.Paths.Root);
        }
    }

    private const string Kept = "somebody else's";

    /// <summary>A directory outside the tree holding one read-only file of somebody else's.</summary>
    private static (string Directory, string File) Outside(TempPaths dir, string name)
    {
        var outside = Path.Combine(dir.Paths.Root, name);
        Directory.CreateDirectory(outside);
        var kept = Path.Combine(outside, "kept.pack");
        File.WriteAllText(kept, Kept);
        File.SetAttributes(kept, FileAttributes.ReadOnly);
        return (outside, kept);
    }

    /// <summary>The directory outside the tree still holds exactly its one file, with its bytes and its mark.</summary>
    private static void AsItWas(string outside, string kept)
    {
        var held = Directory.Exists(outside) ? Directory.GetFileSystemEntries(outside) : [];
        Assert.True(held.Length == 1 && held[0] == kept, $"'{outside}', outside the tree, now holds: {string.Join(", ", held)}.");
        var bytes = File.ReadAllText(kept);
        Assert.True(bytes == Kept, $"'{kept}', outside the tree, was changed through the link: it holds {bytes}");
        var attributes = File.GetAttributes(kept);
        Assert.True(attributes.HasFlag(FileAttributes.ReadOnly), $"'{kept}', outside the tree, was changed through the link: {attributes}.");
    }

    /// <summary>A second name for the file <paramref name="target"/>, made the way this platform makes one.</summary>
    private static void HardLink(string link, string target)
    {
        var start = OperatingSystem.IsWindows()
            ? new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { ArgumentList = { "/c", "mklink", "/H", link, target } }
            : new System.Diagnostics.ProcessStartInfo("/bin/ln") { ArgumentList = { target, link } };
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;

        using var process = System.Diagnostics.Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(File.Exists(link), $"the hard link at '{link}' was not made.");
    }

    /// <summary>The link at <paramref name="link"/>, if a failed row left it, removed as itself.</summary>
    private static void Unlink(string link)
    {
        if (!Path.Exists(link))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.Delete(link);
        }
        else
        {
            File.Delete(link);
        }
    }

    /// <summary>What is left under <paramref name="tree"/>, for a failure message.</summary>
    private static IEnumerable<string> Left(string tree) =>
        Directory.Exists(tree)
            ? Directory.EnumerateFileSystemEntries(tree, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true })
                .Select(entry => Path.GetRelativePath(tree, entry))
            : [];

    /// <summary>
    /// Clears the read-only mark under <paramref name="root"/>, links not followed, so the temporary directory can go
    /// whatever the test found.
    /// </summary>
    private static void Writable(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }

            if (entry is DirectoryInfo)
            {
                Writable(entry.FullName);
            }
        }
    }
}
