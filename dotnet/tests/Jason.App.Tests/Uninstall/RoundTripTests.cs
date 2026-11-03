using System.Security.Cryptography;
using System.Text.Json;
using Jason.App.Tests.EndToEnd;
using Jason.Cli;
using Jason.Cli.Autostart;
using Jason.Cli.Skills;
using Jason.Cli.Uninstall;
using Jason.Contracts.Json;
using Jason.Contracts.Update;

namespace Jason.App.Tests.Uninstall;

/// <summary>
/// The proof A8 asks for, and the one no unit test can give: an installation that was put on a machine comes
/// off it, and what was already there is untouched.
/// </summary>
/// <remarks>
/// <para>
/// Scratch data directory, scratch install directory, scratch harness root, and this repository read where it
/// stands. Four canaries — a neighbouring skill in the harness root, a file in the data directory, and two files
/// in the install directory the installer did not write, one of them another tool's <c>LICENSE</c> beside Jason's
/// notice files — because the only convincing evidence that a
/// destructive verb removed the right things is that named other things are still byte-identical afterwards.
/// A scan for a deletion that did not happen proves nothing.
/// </para>
/// <para>
/// The remover is this machine's own for everything that is a file, so the deletions are real. Its PATH half
/// is not: the PATH of whoever ran the suite is not this test's to edit, and what an edit to it would be is
/// held by the pure text rules in <c>UninstallPathEntryTests</c> instead. Every path it is handed is checked
/// to lie inside the scratch tree first — a test of a verb that deletes things should be the last place
/// without that belt.
/// </para>
/// </remarks>
public class RoundTripTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Installed, then removed, with a real runtime started in between and stopped by the verb itself.
    /// </summary>
    [Fact]
    public async Task What_was_installed_comes_off_and_the_canaries_are_byte_identical()
    {
        using var tree = new ScratchTree();
        var before = tree.Snapshot();

        await tree.InstallSkillsAsync();
        Assert.NotEqual(before.Count, tree.Snapshot().Count);

        // A real runtime, so that the step which stops one is the real step rather than a substitute. It is
        // started as the repository documents starting it, and this verb is what stops it.
        var installation = tree.Installation;
        var descriptor = await GoldenPath.StartAsync(installation);

        var report = await tree.UninstallAsync();

        Assert.True(report.Completed, $"{string.Join(" ", report.Problems)} {report.Refusal}");
        Assert.False(File.Exists(installation.Paths.DescriptorFile), "The descriptor outlived the runtime it described.");
        Assert.False(GoldenPath.IsRunning(descriptor.Pid), "The runtime was still alive after uninstall reported success.");

        var after = tree.Snapshot();

        // Outside the data directory, the tree is what it was before the installation existed, less the one
        // file that was the installation: every deployed file is gone, and every file that was already there
        // is still there with the same bytes. Stated as an equality rather than as a handful of Exists calls,
        // because a verb that removed one thing too many would pass every one of those and fail this.
        Assert.Equal(Without(Outside(before, tree), [tree.Executable, .. tree.Notices]), Outside(after, tree));

        // The install directory itself stays, because somebody else's file is in it.
        Assert.True(Directory.Exists(tree.InstallDirectory), "The install directory went although a file this installer did not write is in it.");
        Assert.Contains(report.Kept, line => line.Contains(tree.InstallDirectory, StringComparison.OrdinalIgnoreCase));

        // And the data directory is kept, with the canary in it untouched.
        Assert.Equal(before[tree.DataCanary], after[tree.DataCanary]);
        Assert.True(Directory.Exists(installation.Root), "The data directory was removed without --purge-data.");
    }

    /// <summary>
    /// And with the word said, everything Jason keeps in the data directory goes too — and nothing else does,
    /// the canary somebody put there included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to assert that the canary went: the data directory "and everything in it" was the one place
    /// the purge was allowed to take somebody's own file. The data directory is whatever <c>JASON_DATA_DIR</c>
    /// names, so the purge now removes what Jason keeps there and names the rest; the directory stays, holding
    /// exactly the file that was put there, byte for byte.
    /// </para>
    /// <para>
    /// The skill packs in the data directory are git clones, and git marks its pack files read-only, which a delete on
    /// Windows refuses; so a pack file sits deep in this data directory, marked so, in a <c>.git</c> marked hidden as Git
    /// for Windows marks it, and goes with the rest.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task And_purge_data_removes_what_jason_keeps_and_nothing_else()
    {
        using var tree = new ScratchTree();
        var before = tree.Snapshot();
        var git = Path.Combine(tree.Installation.Root, "skills", "staged", "a-pack", ".git");
        var pack = Path.Combine(git, "objects", "pack", "pack-0123abcd.pack");
        tree.Canary(pack, "a pack file, as git writes one");
        File.SetAttributes(pack, FileAttributes.ReadOnly);
        new DirectoryInfo(git).Attributes |= FileAttributes.Hidden;

        await tree.InstallSkillsAsync();
        var report = await tree.UninstallAsync(purgeData: true);

        Assert.True(report.Completed, $"{string.Join(" ", report.Problems)} {report.Refusal}");

        var left = Directory.EnumerateFileSystemEntries(tree.Installation.Root).ToList();
        Assert.Equal([tree.DataCanary], left);
        Assert.Equal(before[tree.DataCanary], tree.Snapshot()[tree.DataCanary]);
        Assert.Contains(report.Kept, line => line.Contains(Path.GetFileName(tree.DataCanary), StringComparison.Ordinal));
        Assert.Equal(Without(Outside(before, tree), [tree.Executable, .. tree.Notices]), Outside(tree.Snapshot(), tree));
    }

    /// <summary>
    /// The harness root is the person's, and an uninstall leaves it as it found it, the purge included: the root a real
    /// runtime made at its first creates — its marker, a project's folder and a campaign's with theirs, the memory files —
    /// and the words the person wrote there and a file of their own beside them are the same afterwards, folder for folder
    /// and byte for byte.
    /// </summary>
    /// <remarks>
    /// The runtime makes the root in the home its test environment gives it, beside the data directory and inside this
    /// tree, so the remover's belt covers it and the tree's disposal removes it.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_harness_root_and_everything_in_it_stay_byte_for_byte(bool purgeData)
    {
        using var tree = new ScratchTree();
        await tree.InstallSkillsAsync();

        var installation = tree.Installation;
        await GoldenPath.StartAsync(installation);
        var project = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(installation, "project", "create", "--name", "Acme Widgets")));
        var campaign = GoldenPath.Json(await GoldenPath.Ok(GoldenPath.JasonAsync(installation, "campaign", "create", "--name", "Autumn outreach")));

        var root = tree.HarnessRoot;
        var projectFolder = (string)project["folder_path"]!;
        var campaignFolder = (string)campaign["folder_path"]!;
        foreach (var folder in (string[])[projectFolder, campaignFolder])
        {
            Assert.True(folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), $"The runtime made '{folder}' outside the root this test gave it, '{root}'.");
        }

        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "We sell widgets to teams at example.com.\n");
        File.WriteAllText(Path.Combine(projectFolder, "AGENTS.md"), "Write as Marta, plainly.\n");
        tree.Canary(Path.Combine(root, "notes", "pricing.txt"), "a file of the person's own in the root");
        var before = Entries(root);
        Assert.Contains(Path.Combine(root, ".jason", "marker.json"), before.Keys);
        Assert.Contains(Path.Combine(projectFolder, ".jason", "marker.json"), before.Keys);
        Assert.Contains(Path.Combine(campaignFolder, ".jason", "marker.json"), before.Keys);

        var report = await tree.UninstallAsync(purgeData);

        Assert.True(report.Completed, $"{string.Join(" ", report.Problems)} {report.Refusal}");
        Assert.False(GoldenPath.IsRunning(installation.Pid!.Value), "The runtime was still alive after uninstall reported success.");
        Assert.Equal(before, Entries(root));
    }

    /// <summary>
    /// A second uninstall removes nothing and says so, rather than failing because there is nothing left.
    /// </summary>
    /// <remarks>
    /// Idempotence in the direction that matters. After a run that stopped half-way — a runtime that would
    /// not stop, a file that could not be removed — the operator's next move is to run it again, and a verb
    /// that errored on the second run would make that move frightening.
    /// </remarks>
    [Fact]
    public async Task A_second_uninstall_removes_nothing_and_says_so()
    {
        using var tree = new ScratchTree();
        await tree.InstallSkillsAsync();

        var first = await tree.UninstallAsync();
        Assert.True(first.Completed, $"{string.Join(" ", first.Problems)} {first.Refusal}");
        Assert.NotEmpty(first.Done);

        var after = tree.Snapshot();
        var second = await tree.UninstallAsync();

        Assert.True(second.Completed, $"a second uninstall refused: {string.Join(" ", second.Problems)} {second.Refusal}");
        Assert.Empty(second.Plan.Roots);
        Assert.Equal(after, tree.Snapshot());
    }

    /// <summary>
    /// The same set with the installation's own files taken out of it — the executable and the two notice files the
    /// installer keeps beside it, which are the files outside the data directory that an uninstall is supposed to remove.
    /// </summary>
    /// <remarks>
    /// It asserts the path was in there. Silently removing nothing would turn this comparison into "the tree
    /// is unchanged", which is the opposite of what is being claimed and would pass on a verb that did
    /// nothing at all.
    /// </remarks>
    private static Dictionary<string, string> Without(Dictionary<string, string> snapshot, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            Assert.True(snapshot.Remove(path), $"'{path}' was not in the snapshot this comparison takes it out of.");
        }

        return snapshot;
    }

    /// <summary>Every folder and file under a directory, a folder as itself and a file by its digest: an empty folder that went is a change too.</summary>
    private static Dictionary<string, string> Entries(string directory) =>
        Directory.EnumerateFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => Directory.Exists(path) ? "<folder>" : Digest(path), StringComparer.Ordinal);

    private static string Digest(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Every file outside the data directory, which is the half an uninstall must account for exactly.</summary>
    private static Dictionary<string, string> Outside(Dictionary<string, string> snapshot, ScratchTree tree) =>
        snapshot
            .Where(entry => !entry.Key.StartsWith(tree.Installation.Root, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(StringComparer.Ordinal);

    /// <summary>
    /// One machine, entirely inside one temporary directory: a data directory, an install directory with an
    /// executable in it, a harness root, and the canaries that are how this test knows.
    /// </summary>
    private sealed class ScratchTree : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-roundtrip", Guid.NewGuid().ToString("N"));

        public ScratchTree()
        {
            Directory.CreateDirectory(Path.Combine(_root, "data", "config"));
            Directory.CreateDirectory(Harness);
            Directory.CreateDirectory(InstallDirectory);

            Installation = new Installation(Path.Combine(_root, "data"), new Jason.Runtime.Tests.Plugins.Reply.ReplyAccount());

            // The file this installation is installed as, named rather than worked out. Working it out is how
            // this verb came to plan the removal of the muxer that was running it.
            File.WriteAllText(Executable, "not really a program");
            foreach (var notice in Notices)
            {
                File.WriteAllText(notice, "a notice the installer keeps beside the executable");
            }

            // Four canaries. The neighbour is what a real harness root looks like: somebody else's skill
            // beside the ones a deployment wrote, with no receipt naming it. The LICENSE is what a directory other
            // tools share can hold beside Jason's own notice files.
            Canary(Path.Combine(Harness, "somebody-elses-skill", "SKILL.md"), "---\nname: somebody-elses-skill\n---\n");
            Canary(Path.Combine(InstallDirectory, "notes.txt"), "a file the installer did not write");
            Canary(Path.Combine(InstallDirectory, "LICENSE"), "another tool's licence, beside Jason's notices");
            Canary(DataCanary, "a file somebody put in the data directory");
        }

        public Installation Installation { get; }

        public string Harness => Path.Combine(_root, "harness");

        public string InstallDirectory => Path.Combine(_root, "install");

        public string Executable => Path.Combine(InstallDirectory, OperatingSystem.IsWindows() ? "jason.exe" : "jason");

        public string DataCanary => Path.Combine(Installation.Root, "notes-of-my-own.txt");

        /// <summary>Where a runtime of this installation makes the harness root: in the home its test environment gives it.</summary>
        public string HarnessRoot => Path.Combine(Jason.Runtime.Tests.TempDataDir.HomeOf(Installation.Root), Jason.Runtime.Configuration.HarnessOptions.DefaultFolder);

        /// <summary>The notice files an install keeps beside the executable.</summary>
        public IReadOnlyList<string> Notices => [.. ReleaseAssets.Notices.Select(notice => ReleaseAssets.NoticeBeside(Executable, notice))];

        public void Canary(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        /// <summary>Every file under this tree, by path and by digest.</summary>
        public Dictionary<string, string> Snapshot() =>
            Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, Digest, StringComparer.Ordinal);

        /// <summary>The deployment, from this repository where it stands, into the scratch harness root.</summary>
        public async Task InstallSkillsAsync()
        {
            var error = new StringWriter();
            var exit = await CliApp.RunAsync(
                ["skills", "install", "--source", RepositoryRoot(), "--root", Harness],
                Machine(error),
                Ct);

            Assert.True(exit == 0, $"the deployment this round trip is about did not happen: {error}");
        }

        public async Task<UninstallReport> UninstallAsync(bool purgeData = false)
        {
            var output = new StringWriter();
            await UninstallCommand.RunAsync(
                Machine(new StringWriter(), output),
                new UninstallOptions(Human: false, DryRun: false, PurgeData: purgeData, Yes: true, Force: false),
                Ct);

            return JsonSerializer.Deserialize<UninstallReport>(output.ToString(), JasonJson.Options)!;
        }

        private CliEnvironment Machine(StringWriter error, StringWriter? output = null) =>
            new(
                output ?? new StringWriter(),
                error,
                Installation.Paths,
                InstallPath: Executable,
                Autostart: new NothingRegistered(),
                Harnesses: HarnessLocators.At(Harness),
                Removes: new ScopedRemover(_root));

        public void Dispose()
        {
            Installation.Dispose();
            GoldenPath.Delete(_root);
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
    }

    /// <summary>An account with nothing registered to start at logon, which is the state a round trip starts in.</summary>
    private sealed class NothingRegistered : IAutostartRegistrar
    {
        public AutostartPlatform Platform => AutostartPlatform.Unsupported;

        public AutostartState Read() => new(false, [], null);

        public void Apply(AutostartRegistration registration) => Assert.Fail("nothing here registers anything.");

        public AutostartRemoval Remove(AutostartRegistration registration) => new(Registration: false, Legacy: false);
    }

    /// <summary>
    /// This machine's remover for everything that is a file, refusing anything outside the scratch tree, and
    /// declining the PATH half outright.
    /// </summary>
    /// <remarks>
    /// The deletions have to be real or this proves nothing about deleting. The PATH must not be, because it
    /// belongs to whoever ran the suite; what an edit to it would be is held by pure text rules elsewhere. And
    /// the scope check is the belt: a test of a verb that removes things is the last place to leave one off,
    /// and if the verb ever composes a path outside the tree this fails loudly instead of quietly obeying.
    /// </remarks>
    private sealed class ScopedRemover(string root) : IInstallationRemover
    {
        private readonly IInstallationRemover _real = InstallationRemovers.ForThisMachine();

        public bool CanRemoveRunningImage => _real.CanRemoveRunningImage;

        public void RemoveFile(string path) => _real.RemoveFile(Inside(path));

        public ExecutableOutcome RemoveExecutable(string path) => _real.RemoveExecutable(Inside(path));

        public bool RemoveDirectoryIfEmpty(string path) => _real.RemoveDirectoryIfEmpty(Inside(path));

        public void RemoveTree(string path) => _real.RemoveTree(Inside(path));

        public PathEntryPlan ReadPathEntry(string directory) => new(directory, [], null, Ours: false);

        public PathEntryOutcome RemovePathEntry(PathEntryPlan plan) =>
            new(false, [], "this round trip does not edit the PATH of whoever ran it.");

        // Never the registry of whoever ran the suite: no test shows a toast, so there is nothing of a test's there.
        public IReadOnlyList<string> ReadNotificationRegistration() => [];

        public void RemoveNotificationKey(string key) =>
            throw new InvalidOperationException("this round trip removes nothing from the registry of whoever ran it.");

        public LinkRegistrationRead? ReadLinkRegistration() => null;

        public void RemoveLinkRegistration(string command) =>
            throw new InvalidOperationException("this round trip removes nothing from the registry of whoever ran it.");

        // The test host is no single-file build, so the machine's answer is null; asked anyway, so that a
        // change which made it name something outside the scratch tree fails here rather than deleting it.
        public string? ReadExtractedLibraries(string executable) =>
            _real.ReadExtractedLibraries(executable) is { } extracted ? Inside(extracted) : null;

        public void RemoveExtractedLibraries(string directory) => _real.RemoveExtractedLibraries(Inside(directory));

        private string Inside(string path)
        {
            var full = Path.GetFullPath(path);
            Assert.StartsWith(Path.GetFullPath(root), full, StringComparison.OrdinalIgnoreCase);
            return full;
        }
    }
}
