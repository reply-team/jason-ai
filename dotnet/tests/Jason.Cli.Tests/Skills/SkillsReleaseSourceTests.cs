using System.Diagnostics;
using System.Net;
using System.Text;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Process;
using Jason.Cli.Tests.Update;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// The default source: the release this build was published as, read from the public repository's releases on GitHub —
/// its <c>checksums.txt</c>, then the <see cref="ReleaseAssets.Skills"/> it lists, held to its line there before it is
/// opened.
/// </summary>
/// <remarks>
/// The feed is a <see cref="FakeRelease"/> standing where the network would be, which answers only the addresses a
/// release really serves. No git runs here: the program runner fails the test if anything asks it to.
/// </remarks>
public class SkillsReleaseSourceTests
{
    private const string Build = "1.2.3";

    private const string Tag = "v1.2.3";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The default source is the public repository's releases on GitHub, read directly and not through the address
    /// operated for the product: the package of the release at the tag, found by the same two rules every release file
    /// is found by.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "the default skills source is GitHub's release download address")]
    public void The_default_source_is_the_public_repository_s_releases_on_github()
    {
        var release = UpdateFeed.PinnedFor(new Uri(SkillsSource.DefaultSource), SemanticVersion.Parse(Build));

        Assert.Equal(
            $"https://github.com/reply-team/jason-ai/releases/download/{Tag}/{ReleaseAssets.Skills}",
            UpdateFeed.DownloadUrlFor(release, ReleaseAssets.Skills).AbsoluteUri);
        Assert.Equal(
            $"https://github.com/reply-team/jason-ai/releases/download/{Tag}/{ReleaseAssets.Checksums}",
            UpdateFeed.DownloadUrlFor(release, ReleaseAssets.Checksums).AbsoluteUri);
    }

    /// <summary>
    /// Given no source, the packs are the release's package at this build's tag: fetched beside the release's checksums,
    /// unpacked with the files whose names start with a dot, and recorded as the default source at the tag, with the
    /// commit its <c>source.json</c> names. Nothing is cloned.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "skills install with no source reads the release's package")]
    public async Task With_no_source_the_packs_are_the_release_s_package_at_this_build_s_tag()
    {
        using var dir = new TempPaths();
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(SkillsPackage.Zip(Tag));

        var staged = await SkillsSource.StageAsync(Machine(dir, release), null, null, overriddenByRecord: null, Ct);

        Assert.Equal(SkillsSource.DefaultSource, staged.Source);
        Assert.Equal(Tag, staged.Ref);
        Assert.False(staged.RefOverridden);
        Assert.Equal(SkillsPackage.Commit, staged.Commit);
        Assert.Contains(TaggedSource.Body(Tag), File.ReadAllText(Path.Combine(staged.Directory, "skills", "runtime", "operating", "SKILL.md")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(staged.Directory, "skills", "runtime", ".claude-plugin", "plugin.json")));
        Assert.Equal(
            [$"/reply-team/jason-ai/releases/download/{Tag}/{ReleaseAssets.Checksums}", $"/reply-team/jason-ai/releases/download/{Tag}/{ReleaseAssets.Skills}"],
            release.Requested);
        Assert.All(release.Hosts, host => Assert.Equal("github.com", host));
        Assert.All(release.UserAgents.Values, agent => Assert.StartsWith("jason/", agent, StringComparison.Ordinal));
    }

    /// <summary>A ref named with no source is that release's package, and the record says it was chosen.</summary>
    [Fact]
    public async Task A_ref_named_with_no_source_is_that_release_s_package()
    {
        using var dir = new TempPaths();
        var release = new FakeRelease(SemanticVersion.Parse("1.2.0")).WithSkills(SkillsPackage.Zip("v1.2.0"));

        var staged = await SkillsSource.StageAsync(Machine(dir, release), null, "v1.2.0", overriddenByRecord: null, Ct);

        Assert.Equal("v1.2.0", staged.Ref);
        Assert.True(staged.RefOverridden);
        Assert.Contains(TaggedSource.Body("v1.2.0"), File.ReadAllText(Path.Combine(staged.Directory, "skills", "business", "campaign-planning", "SKILL.md")), StringComparison.Ordinal);
    }

    /// <summary>
    /// A release is found by its tag, so a ref that is not one — a branch — is refused with the source that can fetch it
    /// named, before anything is asked of the feed.
    /// </summary>
    [Theory]
    [InlineData("main")]
    [InlineData("1.2.3")]
    [InlineData("v1.2")]
    [InlineData("v1.2.3+build.7")]
    public async Task A_ref_that_is_not_a_release_s_tag_is_refused_naming_the_git_source(string reference)
    {
        using var dir = new TempPaths();
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(SkillsPackage.Zip(Tag));

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir, release), null, reference, overriddenByRecord: null, Ct));

        Assert.Contains($"'{reference}'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--source <git url> --ref <branch>", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(release.Requested);
        AssertNothingStaged(dir);
    }

    /// <summary>
    /// A pin no release has published yet — the checksums are not there — and a release that published no skills
    /// package, as no release before them did, are refused naming the pin and <c>--source</c>, which works without one;
    /// the second says that release published none.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_pin_with_no_package_published_is_refused_naming_the_pin_and_a_directory(bool releasePublished)
    {
        using var dir = new TempPaths();
        var release = releasePublished
            ? new FakeRelease(SemanticVersion.Parse(Build))
            : new FakeRelease(SemanticVersion.Parse("1.2.2")).WithSkills(SkillsPackage.Zip("v1.2.2"));

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir, release), null, null, overriddenByRecord: null, Ct));

        Assert.Contains($"'{Tag}'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("--source", refusal.Message, StringComparison.Ordinal);
        Assert.True(
            refusal.Message.Contains("published no skills package", StringComparison.Ordinal) == releasePublished,
            refusal.Message);
        AssertNothingStaged(dir);
    }

    /// <summary>A package that is not the one its line in checksums.txt names is refused, and nothing is staged.</summary>
    [Fact]
    [Critical(Family.I1, "the skills package is held to its checksum")]
    public async Task A_package_that_does_not_match_its_checksum_is_refused_and_nothing_is_staged()
    {
        using var dir = new TempPaths();
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(SkillsPackage.Zip(Tag), digest: new string('0', 64));

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir, release), null, null, overriddenByRecord: null, Ct));

        Assert.Contains("does not match", refusal.Message, StringComparison.Ordinal);
        AssertNothingStaged(dir);
    }

    /// <summary>A feed that answers with an error is refused with what it answered.</summary>
    [Fact]
    public async Task A_feed_that_fails_is_refused_with_what_it_answered()
    {
        using var dir = new TempPaths();
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(SkillsPackage.Zip(Tag));
        release.FailWith = HttpStatusCode.BadGateway;

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir, release), null, null, overriddenByRecord: null, Ct));

        Assert.Contains("502", refusal.Message, StringComparison.Ordinal);
        AssertNothingStaged(dir);
    }

    /// <summary>
    /// A package whose <c>source.json</c> names another tag than the one it was fetched for is refused: the record would
    /// name a tag its files are not.
    /// </summary>
    [Fact]
    public async Task A_package_that_says_it_is_another_tag_is_refused()
    {
        using var dir = new TempPaths();
        var files = SkillsPackage.Files(Tag).Select(file => file.Name == "source.json" ? (file.Name, SkillsPackage.Source("v9.9.9")) : file);
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(SkillsPackage.Zip(Tag, files));

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir, release), null, null, overriddenByRecord: null, Ct));

        Assert.Contains("v9.9.9", refusal.Message, StringComparison.Ordinal);
        AssertNothingStaged(dir);
    }

    /// <summary>
    /// What a package holds is held before anything is written: entries are relative paths of plain segments under
    /// <c>skills/</c>, beside <c>source.json</c> alone, each once. A package that reaches anywhere else is refused whole.
    /// </summary>
    [Theory]
    [InlineData("../outside.md")]
    [InlineData("skills/../../outside.md")]
    [InlineData("/outside.md")]
    [InlineData("skills\\runtime\\outside.md")]
    [InlineData("other/outside.md")]
    [InlineData("outside.md")]
    [InlineData("skills//outside.md")]
    [InlineData("skills/./outside.md")]
    public async Task A_package_with_an_entry_outside_its_layout_is_refused_whole(string entry)
    {
        using var dir = new TempPaths();
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(SkillsPackage.Zip(Tag, [.. SkillsPackage.Files(Tag), (entry, "hostile")]));

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir, release), null, null, overriddenByRecord: null, Ct));

        Assert.Contains(entry, refusal.Message, StringComparison.Ordinal);
        AssertNothingStaged(dir);
        Assert.False(File.Exists(Path.Combine(dir.Paths.Root, "skills", "outside.md")));
        Assert.False(File.Exists(Path.Combine(dir.Paths.Root, "outside.md")));
    }

    /// <summary>A package with no <c>source.json</c> says nothing an installation can record, and is refused.</summary>
    [Fact]
    public async Task A_package_with_no_source_file_is_refused()
    {
        using var dir = new TempPaths();
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(SkillsPackage.Zip(Tag, SkillsPackage.Files(Tag).Where(file => file.Name != "source.json")));

        var refusal = await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(Machine(dir, release), null, null, overriddenByRecord: null, Ct));

        Assert.Contains("source.json", refusal.Message, StringComparison.Ordinal);
        AssertNothingStaged(dir);
    }

    /// <summary>A package staged once is used as it is: a tag is one commit, and asking the feed again buys nothing.</summary>
    [Fact]
    public async Task A_package_already_staged_is_used_as_it_is_rather_than_fetched_again()
    {
        using var dir = new TempPaths();
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(SkillsPackage.Zip(Tag));
        await SkillsSource.StageAsync(Machine(dir, release), null, null, overriddenByRecord: null, Ct);
        var asked = release.Requested.Count;

        var again = await SkillsSource.StageAsync(Machine(dir, release), null, null, overriddenByRecord: null, Ct);

        Assert.Equal(asked, release.Requested.Count);
        Assert.Equal(SkillsPackage.Commit, again.Commit);
        Assert.Equal(SkillsSource.DefaultSource, again.Source);
    }

    /// <summary>
    /// A git fetch and the release's package stage into the same directory for the same tag, and neither is taken for the
    /// other: each knows its own finished tree by its own mark, so a tree the other made is staged again.
    /// </summary>
    [Fact]
    public async Task A_tree_git_staged_at_the_tag_is_not_taken_for_the_package_and_back()
    {
        using var dir = new TempPaths();
        var origin = await TaggedSource.CreateAsync(dir, Ct, Tag);
        var git = new RecordingRunner(ProgramRunners.ForThisMachine());
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(SkillsPackage.Zip(Tag));
        var machine = new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths, HttpHandler: release, Programs: git, BuildVersion: Build);

        var cloned = await SkillsSource.StageAsync(machine, origin, null, overriddenByRecord: null, Ct);
        var packaged = await SkillsSource.StageAsync(machine, null, null, overriddenByRecord: null, Ct);
        var clonedAgain = await SkillsSource.StageAsync(machine, origin, null, overriddenByRecord: null, Ct);

        Assert.Equal(cloned.Directory, packaged.Directory);
        Assert.Equal(SkillsPackage.Commit, packaged.Commit);
        Assert.Contains($"/reply-team/jason-ai/releases/download/{Tag}/{ReleaseAssets.Skills}", release.Requested);
        Assert.NotEqual(SkillsPackage.Commit, clonedAgain.Commit);
        Assert.Equal(2, git.Requested.Count(arguments => arguments[0] == "clone"));
    }

    /// <summary>
    /// A git source is still a git source: a repository named with <c>--source</c>, the public one included, is cloned
    /// and the feed is never asked.
    /// </summary>
    [Fact]
    public async Task A_git_source_named_with_source_is_cloned_and_the_feed_is_not_asked()
    {
        using var dir = new TempPaths();
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(SkillsPackage.Zip(Tag));
        var git = new FakeProgramRunner(_ => new ProgramResult(128, string.Empty, "fatal: could not read from remote", false));
        var machine = new CliEnvironment(new StringWriter(), new StringWriter(), dir.Paths, HttpHandler: release, Programs: git, BuildVersion: Build);

        await Assert.ThrowsAsync<SkillsSourceUnavailable>(
            () => SkillsSource.StageAsync(machine, SkillsSource.EarlierDefaultSource, null, overriddenByRecord: null, Ct));

        Assert.Empty(release.Requested);
        Assert.Contains(git.Requested, request => request.Arguments.Contains(SkillsSource.EarlierDefaultSource));
    }

    /// <summary>
    /// The package the release writes is the package this program reads: the release's own script run over this
    /// repository's <c>skills/</c>, served as the release serves it and staged, leaves the tree's files with the tree's
    /// bytes, the files under directories whose names start with a dot among them. The packages the other rows build by
    /// hand hold a few names; this one holds every name the packs really have, so an entry rule or a character refused
    /// that no hand-made name carries is found here.
    /// </summary>
    [Fact]
    public async Task The_package_the_release_s_script_writes_of_this_tree_stages_as_the_tree()
    {
        using var dir = new TempPaths();
        using var scratch = new TempTree();
        var root = RepositoryRoot();
        const string commit = "0123456789abcdef0123456789abcdef01234567";
        var archive = Path.Combine(scratch.NewDirectory("release"), ReleaseAssets.Skills);
        var (exit, stdout, stderr) = Pwsh(
            "-File", Path.Combine(root, ".github", "scripts", "Write-SkillsArchive.ps1"),
            "-Tree", root,
            "-Archive", archive,
            "-Repository", "https://github.com/example/jason",
            "-Ref", Tag,
            "-Commit", commit);
        Assert.True(exit == 0 && File.Exists(archive), $"Write-SkillsArchive.ps1 exited {exit} ({stdout.Trim()}): {stderr}");
        var release = new FakeRelease(SemanticVersion.Parse(Build)).WithSkills(File.ReadAllBytes(archive));

        var staged = await SkillsRelease.StageAsync(Machine(dir, release), SkillsSource.DefaultSource, Tag, overridden: false, Ct);

        Assert.Equal(commit, staged.Commit);
        var tree = Files(Path.Combine(root, "skills"));
        var unpacked = Files(Path.Combine(staged.Directory, "skills"));
        Assert.Contains("runtime/.claude-plugin/plugin.json", tree);
        Assert.True(
            unpacked.SequenceEqual(tree),
            $"Staged and not in the tree: [{string.Join(", ", unpacked.Except(tree))}]; in the tree and not staged: [{string.Join(", ", tree.Except(unpacked))}].");
        Assert.All(tree, name => Assert.True(
            File.ReadAllBytes(Path.Combine(root, "skills", name)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(staged.Directory, "skills", name))),
            $"skills/{name} was staged with bytes other than the tree's."));
    }

    /// <summary>Every file under <paramref name="directory"/>, those whose names start with a dot included, by its path from it.</summary>
    private static List<string> Files(string directory) =>
        [.. Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    /// <summary>The repository root, found by walking up from the test binaries to the directory holding <c>global.json</c>.</summary>
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

    /// <summary>PowerShell run as a release step runs it, with no profile and no telemetry, and what it said.</summary>
    private static (int Exit, string Stdout, string Stderr) Pwsh(params string[] arguments)
    {
        var pwsh = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => new[] { Path.Combine(directory, "pwsh.exe"), Path.Combine(directory, "pwsh") })
            .FirstOrDefault(File.Exists);
        Assert.SkipWhen(pwsh is null, "pwsh is not on this machine's PATH; every CI runner has it, and the release is only ever built there.");

        var start = new ProcessStartInfo(pwsh!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        start.Environment["POWERSHELL_TELEMETRY_OPTOUT"] = "1";
        foreach (var argument in (string[])["-NoProfile", "-NonInteractive", .. arguments])
        {
            start.ArgumentList.Add(argument);
        }

        using var process = System.Diagnostics.Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>An environment whose release reads, the build at <see cref="Build"/> and a runner that refuses every program.</summary>
    private static CliEnvironment Machine(TempPaths dir, FakeRelease release) =>
        new(new StringWriter(), new StringWriter(), dir.Paths, HttpHandler: release, Programs: new FakeProgramRunner(request => throw new InvalidOperationException($"Nothing here may run {request.Program}.")), BuildVersion: Build);

    /// <summary>A runner that runs what it is asked through another, and keeps each argument list it was given.</summary>
    private sealed class RecordingRunner(IProgramRunner inner) : IProgramRunner
    {
        public List<IReadOnlyList<string>> Requested { get; } = [];

        public Task<ProgramResult> RunAsync(string program, IReadOnlyList<string> arguments, string? workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
        {
            Requested.Add([.. arguments]);
            return inner.RunAsync(program, arguments, workingDirectory, timeout, cancellationToken);
        }
    }

    /// <summary>Nothing finished at the tag, and nothing beside it a later run would have to clean.</summary>
    private static void AssertNothingStaged(TempPaths dir)
    {
        var staged = dir.Paths.SkillsStagedSourcesDirectory;
        Assert.True(
            !Directory.Exists(staged) || !Directory.EnumerateFileSystemEntries(staged).Any(),
            $"Staged after a refusal: {(Directory.Exists(staged) ? string.Join(", ", Directory.EnumerateFileSystemEntries(staged)) : string.Empty)}");
    }
}
