using System.Text.RegularExpressions;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Uninstall;
using Jason.Cli.Tests.Update;
using Jason.Cli.Tests.Ui;
using Jason.Cli.Ui;
using Jason.Cli.Update;
using Jason.Contracts.Skills;
using Jason.Contracts.Update;

namespace Jason.Cli.Tests.Files;

/// <summary>
/// A tree the command-line client made of its own is deleted whole through one delete, <c>DirectoryTrees.Delete</c>: a
/// skill a deployment staged, the skill a swap replaced, the UI's staging directory and the build it set aside, and the
/// update's staging directory, as well as the uninstall's purge and a staged skills fetch that already went through it.
/// A recursive <c>Directory.Delete</c> refuses a read-only file on Windows, as git leaves the packs of every clone, and
/// refuses a junction there after removing it; the one delete clears the marks and removes a link as itself.
/// </summary>
/// <remarks>
/// Each row plants, in the site's tree, a read-only file and a link to a directory outside the tree — a junction on
/// Windows, which an ordinary account may make, and a symbolic link elsewhere — and drives the operation down the path
/// that discards the tree. The tree is gone after it, and the directory the link pointed at keeps its file, its bytes and
/// its mark. Where the delete is left to swallow a refusal, as every one of these is, a tree left behind says nothing,
/// so the rows look at the disk.
/// </remarks>
public sealed partial class OwnTreeDeleteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly UiExpected Expected = new(new SemanticVersion(0, 10, 0, null), "v1");

    /// <summary>
    /// The one recursive delete in the client is the one inside <c>DirectoryTrees</c>. A site that must keep its own is
    /// named in <see cref="Kept"/> with the sentence it says at the site; none does today.
    /// </summary>
    [Fact]
    public void The_only_recursive_directory_delete_in_the_client_is_the_one_tree_delete()
    {
        var source = Path.Combine(RepositoryRoot(), "runtime", "src", "Jason.Cli");
        var found = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(source, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal)
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (File: Path.GetRelativePath(source, file).Replace('\\', '/'), Line: index + 1, Text: line.Trim())))
            .Where(line => !line.Text.StartsWith("//", StringComparison.Ordinal) && RecursiveDelete().IsMatch(line.Text))
            .ToList();

        var elsewhere = found.Where(line => line.File != TreeDelete && !Kept.Contains(line.File)).ToList();
        Assert.True(
            elsewhere.Count == 0,
            "A tree is deleted whole outside DirectoryTrees.Delete, which is the one delete for a tree that may hold git's "
            + "read-only files or a link: call it instead, or, where a site must keep its own, say why in one sentence there "
            + $"and name the file in {nameof(OwnTreeDeleteTests)}.{nameof(Kept)}:{Environment.NewLine}"
            + string.Join(Environment.NewLine, elsewhere.Select(line => $"  {line.File}:{line.Line}: {line.Text}")));

        // The scan finds the one delete it allows, so a pattern that stopped matching anything would not pass here.
        Assert.Single(found, line => line.File == TreeDelete);
    }

    /// <summary>The tree a swap moved aside, holding what the live skill held.</summary>
    [Fact]
    public void The_skill_a_swap_replaced_goes_whole()
    {
        using var dir = new TempPaths();
        var (outside, kept) = Outside(dir);
        var staging = Path.Combine(dir.Paths.Root, "staging");
        var live = Skill(Path.Combine(dir.Paths.Root, "roles", "researcher"), "OLD");
        Plant(live, outside);
        var staged = Skill(Path.Combine(staging, "researcher"), "NEW");

        try
        {
            Assert.Null(SkillsSwap.Replace(staged, live, staging, TimeProvider.System));

            Assert.Contains("NEW", File.ReadAllText(Path.Combine(live, "SKILL.md")), StringComparison.Ordinal);
            Gone(staging, ".replaced-");
            AsItWas(outside, kept);
        }
        finally
        {
            Clear(dir.Paths.Root);
        }
    }

    /// <summary>
    /// The skill a deployment staged beside its destination, discarded when the swap is refused. A file held open in the
    /// live skill refuses the move aside, as a launch reading it does; the clock plants into the staged tree when the
    /// swap first reads it, and says the wait is over at its next read.
    /// </summary>
    /// <remarks>
    /// Windows only, because a held file is what refuses a directory rename and only Windows refuses it; elsewhere the
    /// swap goes through and nothing is discarded. Windows is also where the plant stops a recursive delete.
    /// </remarks>
    [Fact]
    public void The_skill_a_deployment_staged_goes_whole_when_its_swap_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses a directory rename for a file held open in it.");

        using var dir = new TempPaths();
        var (outside, kept) = Outside(dir);
        var root = Path.Combine(dir.Paths.Root, "harness");
        var live = Skill(Path.Combine(root, "researcher"), "OLD");
        var source = Skill(Path.Combine(dir.Paths.Root, "source", "researcher"), "NEW");
        var file = Path.Combine(source, "SKILL.md");
        var plan = new SkillsPlan(
            new StagedSource(Path.GetDirectoryName(source)!, "a test source", "main", RefOverridden: false, Commit: null),
            [new PlannedPack("a-pack", root, [new PlannedUnit("researcher", live, [new PlannedFile(file, Path.Combine(live, "SKILL.md"), SkillsRecord.Digest(file))])])],
            [],
            Cap: 1024 * 1024,
            CapFromRuntime: false);
        var staging = SkillsSwap.StagingFor(dir.Paths, root);
        var clock = new Planting(() => Plant(Assert.Single(Directory.GetDirectories(staging, "researcher.staging-*")), outside));

        try
        {
            DeploymentReport report;
            using (File.Open(Path.Combine(live, "SKILL.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                report = SkillsDeployer.Apply(plan, dir.Paths, force: false, clock);
            }

            Assert.True(clock.Planted, "The swap never read its clock, so nothing was planted in the staged skill.");
            Assert.Contains(report.Problems, problem => problem.Contains("could not be replaced", StringComparison.Ordinal));
            Gone(staging, ".staging-");
            AsItWas(outside, kept);
        }
        finally
        {
            Clear(dir.Paths.Root);
        }
    }

    /// <summary>
    /// The UI's staging directory, discarded when the build cannot be moved into place: the clock plants into it when the
    /// swap first reads it and puts a directory where the build is going, so the move is refused and the wait is over.
    /// </summary>
    [Fact]
    public void The_ui_s_staging_directory_goes_whole_when_the_build_is_refused()
    {
        using var dir = new TempPaths();
        var (outside, kept) = Outside(dir);
        var zip = UiZipFile(dir);
        var clock = new Planting(() =>
        {
            Plant(Assert.Single(Directory.GetDirectories(dir.Paths.UiDirectory, ".staging-*")), outside);
            Directory.CreateDirectory(dir.Paths.UiDefaultDirectory);
        });

        try
        {
            Assert.Throws<UiAssetRefusedException>(() => UiAssetInstaller.Install(zip, dir.Paths, Expected, clock));

            Assert.True(clock.Planted, "The swap never read its clock, so nothing was planted in the staging directory.");
            Gone(dir.Paths.UiDirectory, ".staging-");
            AsItWas(outside, kept);
        }
        finally
        {
            Clear(dir.Paths.Root);
        }
    }

    /// <summary>The build a second install set aside, holding what the default build held.</summary>
    [Fact]
    public void The_build_the_ui_set_aside_goes_whole()
    {
        using var dir = new TempPaths();
        var (outside, kept) = Outside(dir);
        UiAssetInstaller.Install(UiZipFile(dir), dir.Paths, Expected, TimeProvider.System);
        Plant(dir.Paths.UiDefaultDirectory, outside);

        try
        {
            UiAssetInstaller.Install(UiZipFile(dir), dir.Paths, Expected, TimeProvider.System);

            Assert.False(File.Exists(Path.Combine(dir.Paths.UiDefaultDirectory, Marked)), "The second install kept the first build's files.");
            Gone(dir.Paths.UiDirectory, ".old-");
            AsItWas(outside, kept);
        }
        finally
        {
            Clear(dir.Paths.Root);
        }
    }

    /// <summary>The update's staging directory, emptied before a stage of what an earlier update left in it.</summary>
    [Fact]
    public async Task The_update_s_staging_directory_goes_whole()
    {
        using var dir = new TempPaths();
        var (outside, kept) = Outside(dir);
        var paths = new UpdatePaths(dir.Paths);
        var earlier = Path.Combine(paths.Staging, "0.1.0");
        Directory.CreateDirectory(earlier);
        Plant(earlier, outside);
        using var release = new FakeRelease(SemanticVersion.Parse("0.2.0"));

        try
        {
            var manifest = UpdateManifest.Read(release.Manifest());
            await new UpdateStager(new HttpClient(release, disposeHandler: false)).StageAsync(manifest, FakeRelease.Rid, release.Feed, paths, Ct);

            Assert.False(Directory.Exists(earlier), $"'{earlier}' is still there, holding: {string.Join(", ", Left(earlier))}.");
            AsItWas(outside, kept);
        }
        finally
        {
            Clear(dir.Paths.Root);
        }
    }

    private const string TreeDelete = "Files/DirectoryTrees.cs";

    /// <summary>Files that keep a recursive delete of their own, each saying why at the site.</summary>
    private static readonly HashSet<string> Kept = new(StringComparer.Ordinal);

    private const string Marked = "marked.pack";

    private const string KeptText = "somebody else's";

    /// <summary>
    /// <c>Directory.Delete(…, recursive: true)</c> or <c>(…, true)</c>, and <c>DirectoryInfo.Delete(true)</c>: a call on one
    /// line with a literal <c>true</c>, positional or named. A call split over lines, or a flag held in a variable, is
    /// beyond it; the client has neither.
    /// </summary>
    [GeneratedRegex(@"\.Delete\((?:[^;]*?,\s*)?(?:recursive:\s*)?true\s*\)")]
    private static partial Regex RecursiveDelete();

    /// <summary>A read-only file, as git leaves a pack, and a link to <paramref name="outside"/>, put in <paramref name="tree"/>.</summary>
    private static void Plant(string tree, string outside)
    {
        var marked = Path.Combine(tree, Marked);
        File.WriteAllText(marked, "read-only, as git leaves a pack");
        File.SetAttributes(marked, FileAttributes.ReadOnly);
        DataDirectoryBeltTests.Link(Path.Combine(tree, "to-outside"), outside);
    }

    /// <summary>A directory outside every tree holding one read-only file of somebody else's.</summary>
    private static (string Directory, string File) Outside(TempPaths dir)
    {
        var outside = Path.Combine(dir.Paths.Root, "outside");
        Directory.CreateDirectory(outside);
        var kept = Path.Combine(outside, "kept.pack");
        File.WriteAllText(kept, KeptText);
        File.SetAttributes(kept, FileAttributes.ReadOnly);
        return (outside, kept);
    }

    /// <summary>The directory outside the tree still holds exactly its one file, with its bytes and its mark.</summary>
    private static void AsItWas(string outside, string kept)
    {
        var held = Directory.Exists(outside) ? Directory.GetFileSystemEntries(outside) : [];
        Assert.True(held.Length == 1 && held[0] == kept, $"'{outside}', outside the tree, now holds: {string.Join(", ", held)}.");
        var bytes = File.ReadAllText(kept);
        Assert.True(bytes == KeptText, $"'{kept}', outside the tree, was changed through the link: it holds {bytes}");
        var attributes = File.GetAttributes(kept);
        Assert.True(attributes.HasFlag(FileAttributes.ReadOnly), $"'{kept}', outside the tree, was changed through the link: {attributes}.");
    }

    /// <summary>Nothing under <paramref name="directory"/> whose name carries <paramref name="marker"/>.</summary>
    private static void Gone(string directory, string marker)
    {
        var left = Directory.Exists(directory)
            ? Directory.GetDirectories(directory).Where(entry => Path.GetFileName(entry).Contains(marker, StringComparison.Ordinal)).ToList()
            : [];
        Assert.True(
            left.Count == 0,
            $"A tree that was to be discarded is still there: {string.Join("; ", left.Select(entry => $"'{entry}' holding {string.Join(", ", Left(entry))}"))}.");
    }

    /// <summary>What is left under <paramref name="tree"/>, links not followed, for a failure message.</summary>
    private static IEnumerable<string> Left(string tree) =>
        Directory.Exists(tree)
            ? Directory.EnumerateFileSystemEntries(tree, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true })
                .Select(entry => Path.GetRelativePath(tree, entry))
            : [];

    /// <summary>
    /// Every link under <paramref name="root"/> removed as itself and every read-only mark cleared, links not followed,
    /// so the temporary directory can go whatever a failed row left.
    /// </summary>
    private static void Clear(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (OperatingSystem.IsWindows() && entry is DirectoryInfo)
                {
                    Directory.Delete(entry.FullName);
                }
                else
                {
                    File.Delete(entry.FullName);
                }

                continue;
            }

            if (entry.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                entry.Attributes &= ~FileAttributes.ReadOnly;
            }

            if (entry is DirectoryInfo)
            {
                Clear(entry.FullName);
            }
        }
    }

    private static string Skill(string directory, string marker)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "SKILL.md"), $"---\nname: researcher\ndescription: one line\n---\n\n{marker}\n");
        return directory;
    }

    private static string UiZipFile(TempPaths dir)
    {
        var file = Path.Combine(dir.Paths.Root, Guid.NewGuid().ToString("N") + ".zip");
        File.WriteAllBytes(file, UiZip.Build("0.10.0"));
        return file;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }

    /// <summary>
    /// A clock that runs <c>plant</c> at its first read, and answers every read a whole bound past the one before it, so
    /// any wait the operation starts is over at its next read.
    /// </summary>
    /// <remarks><c>GetElapsedTime</c> is computed from two timestamps, so a bound apart is the same thing in the units it reads.</remarks>
    private sealed class Planting(Action plant) : TimeProvider
    {
        private long _reads;

        public bool Planted { get; private set; }

        public override long GetTimestamp()
        {
            if (!Planted)
            {
                Planted = true;
                plant();
            }

            return Interlocked.Increment(ref _reads) * ((long)(SkillsSwap.ReplaceTimeout.TotalSeconds * TimestampFrequency) + 1);
        }
    }
}
