using System.Net;
using Jason.Cli.Process;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Process;
using Jason.Cli.Tests.Update;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// The default source: the release this build was published as, read from the release feed — its
/// <c>checksums.txt</c>, then the <see cref="ReleaseAssets.Skills"/> it lists, held to its line there before it is opened.
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
    /// The default source is the release feed's own address, the one every other download of this product reads: one
    /// address operated for the product, which keeps the public repository's paths behind it.
    /// </summary>
    [Fact]
    [Critical(Family.I1, "the default skills source is the release feed")]
    public void The_default_source_is_the_release_feed() =>
        Assert.Equal(SkillsSource.DefaultSource, UpdateFeed.Default.AbsoluteUri);

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
            [$"/releases/download/{Tag}/{ReleaseAssets.Checksums}", $"/releases/download/{Tag}/{ReleaseAssets.Skills}"],
            release.Requested);
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
        Assert.Contains("--source", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(release.Requested);
        AssertNothingStaged(dir);
    }

    /// <summary>
    /// A pin no release has published yet — the checksums are not there — and a release that publishes no skills package
    /// are refused the same way: the pin named, and the directory source that works without a release.
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
        Assert.Contains($"/releases/download/{Tag}/{ReleaseAssets.Skills}", release.Requested);
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
