using Jason.Cli.Uninstall;
using Jason.Contracts.Harness;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Uninstall;

/// <summary>
/// The directories <c>--purge-data</c> refuses before anything is removed. The cases about a drive's root, the
/// profile and the installation are decided as text: no directory there is created or removed, which is the only
/// safe way to ask this question about a drive's root. The ones about a second spelling of a directory are asked
/// of directories these tests make.
/// </summary>
public class DataDirectoryBeltTests
{
    private static readonly string Temporary = Path.GetTempPath();

    private static readonly string Profile = Path.Combine(Temporary, "belt-home", "someone");

    private static readonly string Install = Path.Combine(Temporary, "belt-apps", "jason");

    public static TheoryData<string, string> Refused() => new()
    {
        { "the root of a file system", Path.GetPathRoot(Temporary)! },
        { "the profile", Profile },
        { "the profile, spelled with a trailing separator", Profile + Path.DirectorySeparatorChar },
        { "an ancestor of the profile", Path.GetDirectoryName(Profile)! },
        { "the temporary directory", Temporary },
        { "the installation", Install },
        { "an ancestor of the installation", Path.GetDirectoryName(Install)! },
    };

    // Critical: the purge belt refuses a non-data directory.
    public static TheoryData<string, string> CriticalRefused() =>
        CriticalRows.Mark(Refused(), Family.I3, data => data is ["the root of a file system", _]);

    [Theory]
    [MemberData(nameof(CriticalRefused))]
    public void A_directory_that_is_not_a_data_directory_is_refused(string what, string directory)
    {
        var refusal = DataDirectoryBelt.Refusal(directory, Profile, Temporary, Install);

        Assert.True(refusal is not null, $"{what} ('{directory}') was not refused.");
    }

    public static TheoryData<string, string> Allowed() => new()
    {
        { "the default, inside the profile", Path.Combine(Profile, ".jason") },
        { "a directory of its own under the temporary directory", Path.Combine(Temporary, "jason-data") },
        { "a sibling of the installation", Path.Combine(Temporary, "belt-apps", "jason-data") },
        { "a directory whose name only begins like the profile's", Profile + "-data" },
    };

    [Theory]
    [MemberData(nameof(Allowed))]
    public void A_data_directory_of_its_own_is_not(string what, string directory)
    {
        var refusal = DataDirectoryBelt.Refusal(directory, Profile, Temporary, Install);

        Assert.True(refusal is null, $"{what} ('{directory}') was refused: {refusal}");
    }

    /// <summary>Where nothing names an installation or a profile, those two simply have nothing to hold.</summary>
    [Fact]
    public void An_unnamed_installation_or_profile_holds_nothing()
    {
        Assert.Null(DataDirectoryBelt.Refusal(Path.Combine(Temporary, "jason-data"), null, Temporary, null));
    }

    /// <summary>
    /// The profile spelled with the prefix that turns off Windows' path parsing is the profile. The belt compared
    /// strings, and <c>\\?\C:\Users\…</c> got past it.
    /// </summary>
    [Fact]
    [Critical(Family.I3, "\\\\?\\ cannot disguise the profile")]
    public void The_profile_spelled_with_the_long_path_prefix_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The prefix is Windows' own; elsewhere it is an ordinary, if odd, directory name.");

        Assert.NotNull(DataDirectoryBelt.Refusal(@"\\?\" + Profile, Profile, Temporary, Install));
    }

    /// <summary>
    /// And a link whose target is the profile is the profile — a junction on Windows, which any account may make. The
    /// belt compared the link's own path, and it got past.
    /// </summary>
    [Fact]
    [Critical(Family.I3, "the purge never follows a link to the profile")]
    public void A_link_to_the_profile_is_refused()
    {
        using var tree = new Documentation.TempTree();
        var profile = tree.NewDirectory("someone");
        var link = Path.Combine(tree.Root, "link-to-someone");
        Link(link, profile);

        Assert.NotNull(DataDirectoryBelt.Refusal(link, profile, Temporary, Install));
        Assert.NotNull(DataDirectoryBelt.Refusal(Path.Combine(link, "."), Path.Combine(profile, "inside"), Temporary, Install));
    }

    /// <summary>
    /// A repository's working tree is not a data directory. <c>JASON_DATA_DIR=.</c> typed in a checkout of this
    /// repository would have the purge remove its <c>plugins</c> and <c>skills</c>, which are names Jason keeps in a
    /// data directory too.
    /// </summary>
    [Fact]
    public void A_repositorys_working_tree_is_refused()
    {
        using var tree = new Documentation.TempTree();
        var checkout = tree.NewDirectory("checkout");
        Directory.CreateDirectory(Path.Combine(checkout, ".git"));

        var refusal = DataDirectoryBelt.Refusal(checkout, Profile, Temporary, Install);

        Assert.NotNull(refusal);
        Assert.Contains(".git", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A folder of the harness root is the person's, not a data directory. <c>JASON_DATA_DIR</c> naming the root, a
    /// project's folder or a campaign's would have the purge remove whatever of theirs bears a name Jason keeps — a
    /// campaign called "Logs" has the folder <c>logs</c> — so a data directory that holds a marker at its top, or in a
    /// folder directly in it, is refused: a marker of any level, and one nobody can read, which is still somebody's. And
    /// one anywhere in a folder the purge removes whole is refused too, however deep: <c>JASON_DATA_DIR=D:\Sales</c> with
    /// the root at <c>D:\Sales\work\Jason</c> would have the purge take <c>work</c>, and the root with it.
    /// </summary>
    [Theory]
    [InlineData("the root itself", "", HarnessMarker.Root)]
    [InlineData("a project's folder", "", HarnessMarker.Project)]
    [InlineData("a folder holding campaigns, one of them named like a folder Jason keeps", "logs", HarnessMarker.Campaign)]
    [InlineData("a folder holding the root", "Jason", HarnessMarker.Root)]
    [InlineData("a folder with a marker nobody can read", "notes", null)]
    [InlineData("a folder holding the root two folders down, in one the purge removes whole", "work/Jason", HarnessMarker.Root)]
    [InlineData("a folder holding a marker deep in one the purge removes whole", "work/wi_01JB0000000000000000000000/a/b", HarnessMarker.Root)]
    [InlineData("a folder holding a campaign's in another the purge removes whole", "logs/projects/acme/campaigns/q4", HarnessMarker.Campaign)]
    [InlineData("a folder holding a marker nobody can read deep in one the purge removes whole", "plugins/notes/old", null)]
    public void A_folder_of_the_harness_root_is_refused(string what, string folder, string? kind)
    {
        using var tree = new Documentation.TempTree();
        var data = tree.NewDirectory("data");
        var marked = Path.Combine([data, .. folder.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
        Mark(marked, kind);

        var refusal = DataDirectoryBelt.Refusal(data, Profile, Temporary, Install);

        Assert.True(refusal is not null, $"{what} ('{marked}') was not refused.");
        Assert.Contains(".jason/marker.json", refusal, StringComparison.Ordinal);
        Assert.Contains("harness root", refusal, StringComparison.Ordinal);
        Assert.Contains(folder.Length == 0 ? data : Path.GetRelativePath(data, marked), refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// And a data directory inside a folder of the harness root is refused, at any depth: a stray <c>JASON_DATA_DIR=.</c>
    /// typed in a folder of the person's own under a campaign's would have the purge remove that folder's <c>logs</c> and
    /// <c>config</c>, which are the person's, under the root. Each folder above it is asked for its marker, which is one
    /// look per folder and no listing.
    /// </summary>
    [Theory]
    [InlineData("a folder of the person's own in a campaign's", "Jason/projects/acme/campaigns/q4/research", "Jason/projects/acme/campaigns/q4", HarnessMarker.Campaign)]
    [InlineData("the folder that holds the root's projects", "Jason/projects", "Jason", HarnessMarker.Root)]
    [InlineData("a folder deep in a project's", "Jason/projects/acme/notes/2026/october", "Jason/projects/acme", HarnessMarker.Project)]
    [InlineData("a folder in one with a marker nobody can read", "shared/Jason/inbox", "shared/Jason", null)]
    public void A_data_directory_inside_a_folder_of_the_harness_root_is_refused(string what, string inside, string level, string? kind)
    {
        using var tree = new Documentation.TempTree();
        var top = tree.NewDirectory("top");
        var marked = Path.Combine([top, .. level.Split('/')]);
        Mark(marked, kind);
        var data = Path.Combine([top, .. inside.Split('/')]);
        Directory.CreateDirectory(data);

        var refusal = DataDirectoryBelt.Refusal(data, Profile, Temporary, Install);

        Assert.True(refusal is not null, $"{what} ('{data}', inside '{marked}') was not refused.");
        Assert.Contains(".jason/marker.json", refusal, StringComparison.Ordinal);
        Assert.Contains("harness root", refusal, StringComparison.Ordinal);
        Assert.Contains(marked, refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A marker is known by being there, so one that cannot be opened refuses as one that can: another program holding
    /// it with no sharing, as a sync client or an editor may. The look read the file, and the read failed, and the purge
    /// went ahead — for that folder and for every one after it.
    /// </summary>
    [Fact]
    public void A_marker_another_program_holds_open_is_still_a_marker()
    {
        using var tree = new Documentation.TempTree();
        var data = tree.NewDirectory("projects");
        var held = Path.Combine(data, "a-first");
        Mark(held, HarnessMarker.Project);

        using var holding = new FileStream(HarnessMarker.PathIn(held), FileMode.Open, FileAccess.Read, FileShare.None);
        var refusal = DataDirectoryBelt.Refusal(data, Profile, Temporary, Install);

        Assert.True(refusal is not null, $"'{data}', holding '{held}' whose marker another program holds open, was not refused.");
        Assert.Contains("a-first", refusal, StringComparison.Ordinal);
    }

    /// <summary>
    /// A folder in what the purge would remove that the look cannot enter refuses the purge: what is in it is not known,
    /// so neither is whether a folder of the harness root is, and a purge that cannot see what it would remove does not go
    /// ahead. The refusal names the folder.
    /// </summary>
    [Fact]
    public void A_folder_the_look_cannot_enter_refuses()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged account lists any folder, so none can be closed to it.");

        using var tree = new Documentation.TempTree();
        var data = tree.NewDirectory("data");
        var closed = Path.Combine(data, "work", "wi_01JB0000000000000000000000");
        Directory.CreateDirectory(Path.Combine(closed, "inside"));
        Directory.CreateDirectory(Path.Combine(data, "logs"));

        using (Unlistable(closed))
        {
            Assert.Throws<UnauthorizedAccessException>(() => Directory.EnumerateDirectories(closed).ToList());

            var refusal = DataDirectoryBelt.Refusal(data, Profile, Temporary, Install);

            Assert.True(refusal is not null, $"'{data}' was not refused: '{closed}', in what the purge would remove, cannot be listed.");
            Assert.Contains(closed, refusal, StringComparison.Ordinal);
            Assert.Contains("harness root", refusal, StringComparison.Ordinal);
            Assert.Contains(".jason/marker.json", refusal, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// And the look goes on past such a folder, so a marker in a folder after it is the refusal's reason, named. The look
    /// failed as one, so the first folder it could not open ended it, and nothing after was looked at.
    /// </summary>
    [Fact]
    public void A_folder_the_look_cannot_enter_does_not_end_the_look()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged account lists any folder, so none can be closed to it.");

        using var tree = new Documentation.TempTree();
        var data = tree.NewDirectory("data");
        var closed = Path.Combine(data, "work", "a-closed");
        Directory.CreateDirectory(Path.Combine(closed, "inside"));
        var marked = Path.Combine(data, "work", "b-later", "Jason");
        Mark(marked, HarnessMarker.Root);

        using (Unlistable(closed))
        {
            Assert.Throws<UnauthorizedAccessException>(() => Directory.EnumerateDirectories(closed).ToList());

            var refusal = DataDirectoryBelt.Refusal(data, Profile, Temporary, Install);

            Assert.True(refusal is not null, $"'{data}' was not refused: the folder after '{closed}', which cannot be listed, holds '{marked}'.");
            Assert.Contains(marked, refusal, StringComparison.Ordinal);
        }
    }

    /// <summary>A folder in the data directory that the purge keeps is not looked into, so one closed to the look refuses nothing.</summary>
    [Fact]
    public void A_folder_the_purge_keeps_is_not_looked_into()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged account lists any folder, so none can be closed to it.");

        using var tree = new Documentation.TempTree();
        var data = tree.NewDirectory("data");
        var kept = Path.Combine(data, "my-notes", "closed");
        Directory.CreateDirectory(Path.Combine(kept, "inside"));

        using (Unlistable(kept))
        {
            Assert.Null(DataDirectoryBelt.Refusal(data, Profile, Temporary, Install));
        }
    }

    /// <summary>
    /// The look reaches as far as the purge's delete and no further. The delete never goes through a link — it removes
    /// the link itself, or, for a junction an ordinary Windows account may not unmount, refuses — so a folder of the
    /// harness root that a link in a folder the purge removes points at is not looked through, and nothing refuses; and
    /// the delete leaves that folder as it was, which is what makes passing it right.
    /// </summary>
    [Fact]
    public void A_link_in_what_the_purge_removes_is_not_looked_through_since_the_delete_does_not_go_through_it()
    {
        using var tree = new Documentation.TempTree();
        var root = tree.NewDirectory("Jason");
        Mark(root, HarnessMarker.Root);
        Directory.CreateDirectory(Path.Combine(root, "projects", "acme"));
        var data = tree.NewDirectory("data");
        var work = Path.Combine(data, "work");
        Directory.CreateDirectory(work);
        var link = Path.Combine(work, "to-the-root");
        Link(link, root);

        try
        {
            Assert.Null(DataDirectoryBelt.Refusal(data, Profile, Temporary, Install));

            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (Exception refused) when (refused is UnauthorizedAccessException or IOException)
            {
                // The junction refused, as itself: an ordinary account may not unmount it, and an elevated one is
                // told it is no volume's mount point. Nothing behind it was reached.
            }

            Assert.True(
                File.Exists(HarnessMarker.PathIn(root)) && Directory.Exists(Path.Combine(root, "projects", "acme")),
                $"deleting '{work}' went through its link into '{root}'.");
        }
        finally
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
        }
    }

    /// <summary>A marker of <paramref name="kind"/> in <paramref name="folder"/>, or, for null, one nobody can read.</summary>
    private static void Mark(string folder, string? kind)
    {
        Directory.CreateDirectory(Path.Combine(folder, HarnessMarker.Directory));
        File.WriteAllBytes(
            HarnessMarker.PathIn(folder),
            kind is null ? "not a marker"u8.ToArray() : new HarnessMarker(kind, kind == HarnessMarker.Root ? null : "prj_01JB0000000000000000000000").Bytes());
    }

    /// <summary>
    /// Closes <paramref name="folder"/> to a listing by this account until disposed: a rule denying it on Windows, no
    /// read permission elsewhere.
    /// </summary>
    internal static IDisposable Unlistable(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            return new ListingDenied(folder);
        }

        return new NoPermissions(folder);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private sealed class ListingDenied : IDisposable
    {
        private readonly DirectoryInfo _folder;

        private readonly System.Security.AccessControl.FileSystemAccessRule _rule;

        public ListingDenied(string folder)
        {
            _folder = new DirectoryInfo(folder);
            _rule = new System.Security.AccessControl.FileSystemAccessRule(
                System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                System.Security.AccessControl.FileSystemRights.ListDirectory,
                System.Security.AccessControl.AccessControlType.Deny);
            var security = _folder.GetAccessControl();
            security.AddAccessRule(_rule);
            _folder.SetAccessControl(security);
        }

        public void Dispose()
        {
            var security = _folder.GetAccessControl();
            security.RemoveAccessRule(_rule);
            _folder.SetAccessControl(security);
        }
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private sealed class NoPermissions : IDisposable
    {
        private readonly string _folder;

        private readonly UnixFileMode _mode;

        public NoPermissions(string folder)
        {
            _folder = folder;
            _mode = File.GetUnixFileMode(folder);
            File.SetUnixFileMode(folder, UnixFileMode.None);
        }

        public void Dispose() => File.SetUnixFileMode(_folder, _mode);
    }

    /// <summary>A link at <paramref name="link"/> to the directory <paramref name="target"/>, of the kind this account may make.</summary>
    private static void Link(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }

        // A junction: a symbolic link needs a privilege an ordinary Windows account does not hold, and a junction
        // does not — which is what makes it the one a stale JASON_DATA_DIR is likely to go through.
        var start = new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), $"the junction at '{link}' was not made.");
    }
}
