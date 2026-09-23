using System.Security.Cryptography;
using System.Text.Json;
using Jason.App.Tests.EndToEnd;
using Jason.Cli;
using Jason.Cli.Autostart;
using Jason.Cli.Skills;
using Jason.Cli.Uninstall;
using Jason.Contracts.Json;

namespace Jason.App.Tests.Uninstall;

/// <summary>
/// The proof A8 asks for, and the one no unit test can give: an installation that was put on a machine comes
/// off it, and what was already there is untouched.
/// </summary>
/// <remarks>
/// <para>
/// Scratch data directory, scratch install directory, scratch harness root, and this repository read where it
/// stands. Three canaries — a neighbouring skill in the harness root, a file in the data directory, and a file
/// in the install directory the installer did not write — because the only convincing evidence that a
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
        Assert.Equal(Without(Outside(before, tree), tree.Executable), Outside(after, tree));

        // The install directory itself stays, because somebody else's file is in it.
        Assert.True(Directory.Exists(tree.InstallDirectory), "The install directory went although a file this installer did not write is in it.");
        Assert.Contains(report.Kept, line => line.Contains(tree.InstallDirectory, StringComparison.OrdinalIgnoreCase));

        // And the data directory is kept, with the canary in it untouched.
        Assert.Equal(before[tree.DataCanary], after[tree.DataCanary]);
        Assert.True(Directory.Exists(installation.Root), "The data directory was removed without --purge-data.");
    }

    /// <summary>And with the word said, the tree goes back to the snapshot exactly.</summary>
    [Fact]
    public async Task And_purge_data_returns_the_tree_to_the_snapshot_exactly()
    {
        using var tree = new ScratchTree();
        var before = tree.Snapshot();

        await tree.InstallSkillsAsync();
        var report = await tree.UninstallAsync(purgeData: true);

        Assert.True(report.Completed, $"{string.Join(" ", report.Problems)} {report.Refusal}");

        // The data directory and everything in it, including the canary in it: the one place --purge-data is
        // allowed to take somebody's own file, and the word is what allows it.
        Assert.False(Directory.Exists(tree.Installation.Root), "--purge-data left the data directory behind.");
        Assert.Equal(Without(Outside(before, tree), tree.Executable), Outside(tree.Snapshot(), tree));
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
    /// The same set with one path taken out of it — the executable, which is the one file outside the data
    /// directory that an uninstall is supposed to remove.
    /// </summary>
    /// <remarks>
    /// It asserts the path was in there. Silently removing nothing would turn this comparison into "the tree
    /// is unchanged", which is the opposite of what is being claimed and would pass on a verb that did
    /// nothing at all.
    /// </remarks>
    private static Dictionary<string, string> Without(Dictionary<string, string> snapshot, string path)
    {
        Assert.True(snapshot.Remove(path), $"'{path}' was not in the snapshot this comparison takes it out of.");
        return snapshot;
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

            // Three canaries. The neighbour is what a real harness root looks like: somebody else's skill
            // beside the ones a deployment wrote, with no receipt naming it.
            Canary(Path.Combine(Harness, "somebody-elses-skill", "SKILL.md"), "---\nname: somebody-elses-skill\n---\n");
            Canary(Path.Combine(InstallDirectory, "notes.txt"), "a file the installer did not write");
            Canary(DataCanary, "a file somebody put in the data directory");
        }

        public Installation Installation { get; }

        public string Harness => Path.Combine(_root, "harness");

        public string InstallDirectory => Path.Combine(_root, "install");

        public string Executable => Path.Combine(InstallDirectory, OperatingSystem.IsWindows() ? "jason.exe" : "jason");

        public string DataCanary => Path.Combine(Installation.Root, "notes-of-my-own.txt");

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

        private static string Digest(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexStringLower(SHA256.HashData(stream));
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

        public void Remove(AutostartRegistration registration)
        {
        }
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

        private string Inside(string path)
        {
            var full = Path.GetFullPath(path);
            Assert.StartsWith(Path.GetFullPath(root), full, StringComparison.OrdinalIgnoreCase);
            return full;
        }
    }
}
