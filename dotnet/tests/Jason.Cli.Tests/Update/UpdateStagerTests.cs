using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jason.Cli.Update;
using Jason.Contracts;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Update;

/// <summary>
/// The first step of an update, against archives this test builds — including the ones a hostile feed would
/// serve. Nothing here is checked in: an archive that lives in the repository is one nobody can make hostile.
/// </summary>
public class UpdateStagerTests
{
    private static readonly SemanticVersion Next = SemanticVersion.Parse("0.2.0");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The executable, and each notice the archive carries beside it, staged under the name it is kept under beside the
    /// executable. Nothing else comes out.
    /// </summary>
    [Fact]
    public async Task A_staged_release_is_the_executable_and_the_notices_its_archive_carries()
    {
        using var dir = new TempTree();
        var asset = ReleaseAssets.For(FakeRelease.Rid);
        using var release = new FakeRelease(Next, archive: FakeRelease.ArchiveOf(asset,
        [
            new FakeRelease.Entry(ReleaseAssets.ExecutableName, "jason 0.2.0"),
            new FakeRelease.Entry("LICENSE", "the licence"),
            new FakeRelease.Entry("THIRD-PARTY-NOTICES", "the notices"),
            new FakeRelease.Entry("README.md", "not a notice"),
        ]));

        var staged = await StageAsync(release, dir);

        Assert.Equal(Path.Combine(dir.Root, "update", "staged", "0.2.0", ReleaseAssets.ExecutableName), staged);
        Assert.Equal(
            new Dictionary<string, string>
            {
                [ReleaseAssets.ExecutableName] = "jason 0.2.0",
                ["jason-LICENSE"] = "the licence",
                ["jason-THIRD-PARTY-NOTICES"] = "the notices",
            },
            Staged(staged));
    }

    /// <summary>A release from before 0.6.0 carries its licence alone, and an archive may carry no notice at all.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_release_without_a_notice_stages_none_of_that_name(bool licence)
    {
        using var dir = new TempTree();
        var asset = ReleaseAssets.For(FakeRelease.Rid);
        FakeRelease.Entry[] entries = licence
            ? [new(ReleaseAssets.ExecutableName, "jason 0.2.0"), new("LICENSE", "the licence")]
            : [new(ReleaseAssets.ExecutableName, "jason 0.2.0")];
        using var release = new FakeRelease(Next, archive: FakeRelease.ArchiveOf(asset, entries));

        var staged = await StageAsync(release, dir);

        var expected = new Dictionary<string, string> { [ReleaseAssets.ExecutableName] = "jason 0.2.0" };
        if (licence)
        {
            expected["jason-LICENSE"] = "the licence";
        }

        Assert.Equal(expected, Staged(staged));
    }

    /// <summary>
    /// A notice is taken by the rule the executable is taken by: an entry whose whole name is the notice's, a regular
    /// file, the first such entry. Under either name, a path, a directory, a link and another case of the name are
    /// ignored, and of two regular entries the first is staged.
    /// </summary>
    [Theory]
    [InlineData("LICENSE", "nested")]
    [InlineData("THIRD-PARTY-NOTICES", "nested")]
    [InlineData("LICENSE", "parent")]
    [InlineData("THIRD-PARTY-NOTICES", "parent")]
    [InlineData("LICENSE", "absolute")]
    [InlineData("THIRD-PARTY-NOTICES", "absolute")]
    [InlineData("LICENSE", "directory")]
    [InlineData("THIRD-PARTY-NOTICES", "directory")]
    [InlineData("LICENSE", "link")]
    [InlineData("THIRD-PARTY-NOTICES", "link")]
    [InlineData("LICENSE", "case")]
    [InlineData("THIRD-PARTY-NOTICES", "case")]
    [InlineData("LICENSE", "dot-slash")]
    [InlineData("THIRD-PARTY-NOTICES", "dot-slash")]
    [InlineData("LICENSE", "duplicate")]
    [InlineData("THIRD-PARTY-NOTICES", "duplicate")]
    [InlineData("LICENSE", "link, then the file")]
    [InlineData("THIRD-PARTY-NOTICES", "link, then the file")]
    public async Task A_notice_is_taken_only_as_the_first_regular_entry_of_exactly_its_name(string notice, string kind)
    {
        using var dir = new TempTree();
        var asset = ReleaseAssets.For(FakeRelease.Rid);
        var other = notice == "LICENSE" ? "THIRD-PARTY-NOTICES" : "LICENSE";
        FakeRelease.Entry[] hostile = kind switch
        {
            "nested" => [new("x/" + notice, "hostile")],
            "parent" => [new("../" + notice, "hostile")],
            "absolute" => [new("/" + notice, "hostile")],
            "directory" => [new(notice, string.Empty, FakeRelease.EntryKind.Directory)],
            "link" => [new(notice, other, FakeRelease.EntryKind.SymbolicLink)],
            "case" => [new(notice.ToLowerInvariant(), "hostile")],
            "dot-slash" => [new("./" + notice, "hostile")],
            "duplicate" => [new(notice, "the first"), new(notice, "the second")],
            "link, then the file" => [new(notice, other, FakeRelease.EntryKind.SymbolicLink), new(notice, "the first")],
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        using var release = new FakeRelease(Next, archive: FakeRelease.ArchiveOf(asset,
        [
            new FakeRelease.Entry(ReleaseAssets.ExecutableName, "jason 0.2.0"),
            new FakeRelease.Entry(other, "the other notice"),
            .. hostile,
        ]));

        var staged = await StageAsync(release, dir);

        var expected = new Dictionary<string, string>
        {
            [ReleaseAssets.ExecutableName] = "jason 0.2.0",
            [ReleaseAssets.NoticeFileName(other)] = "the other notice",
        };
        if (kind is "duplicate" or "link, then the file")
        {
            expected[ReleaseAssets.NoticeFileName(notice)] = "the first";
        }

        Assert.Equal(expected, Staged(staged));
        Assert.True(Directory.EnumerateFiles(dir.Root).ToList() is [], $"an archive with {kind} put a file outside the staged directory");
    }

    /// <summary>Every file in the staged directory, by name, with its text.</summary>
    private static Dictionary<string, string> Staged(string executable) =>
        Directory.EnumerateFiles(Path.GetDirectoryName(executable)!)
            .ToDictionary(file => Path.GetFileName(file), File.ReadAllText, StringComparer.Ordinal);

    /// <summary>The download is fetched from the feed's own directory, so a feed cannot say where to download from.</summary>
    [Fact]
    public async Task A_download_comes_from_the_directory_the_feed_is_in()
    {
        using var dir = new TempTree();
        using var release = new FakeRelease(Next);

        await StageAsync(release, dir);

        Assert.Equal(
            ["/releases/latest/download/" + release.Asset],
            release.Requested);
    }

    /// <summary>
    /// The archive is asked for as the feed is, saying which version asks: what this product tells a web page about itself
    /// is one line, whichever of its requests carries it.
    /// </summary>
    [Fact]
    public async Task A_download_says_which_version_is_asking_as_the_feed_does()
    {
        using var dir = new TempTree();
        using var release = new FakeRelease(Next);

        await StageAsync(release, dir);

        Assert.Equal($"jason/{JasonVersion.Current}", release.UserAgents["/releases/latest/download/" + release.Asset]);
    }

    /// <summary>
    /// The files beside the archives, a release's checksums, its UI build and its plugin packages, are asked for through
    /// one routine, and it says which version asks as the feed and the archive do.
    /// </summary>
    [Fact]
    public async Task A_file_beside_the_archives_says_which_version_is_asking_as_the_archive_does()
    {
        using var release = new FakeRelease(Next);
        using var client = new HttpClient(release, disposeHandler: false);
        var path = $"/releases/download/v{Next}/{ReleaseAssets.Checksums}";

        await ReleaseFiles.FetchAsync(
            client,
            new Uri("https://127.0.0.1" + path),
            ReleaseFiles.MaxChecksumsBytes,
            $"a release's {ReleaseAssets.Checksums}",
            TimeSpan.FromSeconds(30),
            message => new InvalidOperationException(message),
            message => new InvalidOperationException(message),
            Ct,
            Ct);

        Assert.Equal($"jason/{JasonVersion.Current}", release.UserAgents[path]);
    }

    [Fact]
    [Critical(Family.I1, "update verifies the checksum")]
    public async Task A_download_whose_digest_does_not_match_is_refused_and_its_bytes_are_gone()
    {
        using var dir = new TempTree();
        using var release = new FakeRelease(Next, digest: new string('a', 64));

        var refused = await Assert.ThrowsAsync<UpdateException>(() => StageAsync(release, dir));

        Assert.Equal(UpdateCodes.ArtifactCorrupt, refused.Code);
        Assert.False(refused.Retryable, "the same bytes will hash the same way, so repeating this is pointless");
        Assert.False(
            Directory.Exists(Path.Combine(dir.Root, "update", "staged", "0.2.0")),
            "bytes that failed their digest were left where a later step could pick them up");
    }

    [Fact]
    public async Task A_download_that_is_not_the_length_it_claimed_is_refused()
    {
        using var dir = new TempTree();
        using var release = new FakeRelease(Next, size: 8);

        var refused = await Assert.ThrowsAsync<UpdateException>(() => StageAsync(release, dir));

        Assert.Equal(UpdateCodes.ArtifactCorrupt, refused.Code);
    }

    [Fact]
    public async Task A_feed_that_does_not_answer_is_a_refusal_worth_repeating()
    {
        using var dir = new TempTree();
        using var release = new FakeRelease(Next) { FailWith = HttpStatusCode.ServiceUnavailable };

        var refused = await Assert.ThrowsAsync<UpdateException>(() => StageAsync(release, dir));

        Assert.Equal(UpdateCodes.ArtifactUnreachable, refused.Code);
        Assert.True(refused.Retryable);
    }

    /// <summary>
    /// Every hostile archive shape, and the answer to all of them is the same: take the entries whose whole names are
    /// the executable's and the notices', and ignore the rest. There is no path to sanitise because no path is ever used.
    /// </summary>
    [Theory]
    [MemberData(nameof(CriticalHostileArchives))]
    public async Task Only_the_one_expected_entry_is_ever_taken_out_of_an_archive(string what, string[] entries, bool staged)
    {
        using var dir = new TempTree();
        var asset = ReleaseAssets.For(FakeRelease.Rid);
        var bytes = FakeRelease.Archive(asset, entries.Select(name => (name, Encoding.UTF8.GetBytes($"content of {name}"))));
        using var release = new FakeRelease(Next, archive: bytes);

        if (!staged)
        {
            var refused = await Assert.ThrowsAsync<UpdateException>(() => StageAsync(release, dir));
            Assert.Equal(UpdateCodes.ArtifactUnexpected, refused.Code);
            Assert.Contains(ReleaseAssets.ExecutableName, refused.Message, StringComparison.Ordinal);
            return;
        }

        var executable = await StageAsync(release, dir);
        Assert.Equal($"content of {ReleaseAssets.ExecutableName}", File.ReadAllText(executable));

        // And nothing else came out anywhere: not beside it but its notices, not above it.
        string[] notices = [.. ReleaseAssets.Notices.Where(entries.Contains).Select(ReleaseAssets.NoticeFileName)];
        Assert.Equal(
            [ReleaseAssets.ExecutableName, .. notices],
            Directory.EnumerateFiles(Path.GetDirectoryName(executable)!).Select(Path.GetFileName).OrderBy(name => name == ReleaseAssets.ExecutableName ? 0 : 1).ThenBy(name => name, StringComparer.Ordinal));
        Assert.True(
            Directory.EnumerateFiles(dir.Root).ToList() is [],
            $"an archive with {what} put a file where nothing but the staged executable should be");
    }

    public static TheoryData<string, string[], bool> HostileArchives()
    {
        var wanted = ReleaseAssets.ExecutableName;
        return new TheoryData<string, string[], bool>
        {
            { "the file and nothing else", [wanted], true },
            { "extra entries beside it", [wanted, "LICENSE", "notes.md", "plugins/evil.js"], true },
            { "a name that climbs out of the directory", ["../" + wanted, wanted], true },
            { "a name that climbs out, and nothing else", ["../" + wanted], false },
            { "an absolute path", ["/etc/cron.d/jason", wanted], true },
            { "a name that is the executable with a directory in front", ["payload/" + wanted], false },
            { "nothing this platform runs", ["README.md", "LICENSE"], false },
        };
    }

    // Critical: archive extraction takes one entry.
    public static TheoryData<string, string[], bool> CriticalHostileArchives() =>
        CriticalRows.Mark(HostileArchives(), Family.X2, data => data is ["the file and nothing else", _, _]);

    /// <summary>A second update finds nothing of the first one's: the staged directory is emptied before it is filled.</summary>
    [Fact]
    public async Task Staging_clears_what_the_last_update_left()
    {
        using var dir = new TempTree();
        var stale = Path.Combine(dir.Root, "update", "staged", "0.1.9");
        Directory.CreateDirectory(stale);
        File.WriteAllText(Path.Combine(stale, "leftover"), "from the update before");

        using var release = new FakeRelease(Next);
        await StageAsync(release, dir);

        Assert.False(Directory.Exists(stale));
    }

    [Fact]
    public async Task A_platform_the_release_publishes_nothing_for_is_said_plainly()
    {
        using var dir = new TempTree();
        using var release = new FakeRelease(Next);
        var manifest = UpdateManifest.Read(release.Manifest());

        var refused = await Assert.ThrowsAsync<UpdateException>(
            () => Stager(release).StageAsync(manifest, "sparc-v9", release.Feed, new UpdatePaths(Paths(dir)), Ct));

        Assert.Equal(UpdateCodes.ArtifactUnexpected, refused.Code);
        Assert.Contains("sparc-v9", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Critical(Family.I1, "the size bound")]
    public async Task An_artifact_larger_than_the_bound_is_refused_before_a_byte_is_fetched()
    {
        using var dir = new TempTree();
        using var release = new FakeRelease(Next, size: UpdateStager.MaxArtifactBytes + 1);

        var refused = await Assert.ThrowsAsync<UpdateException>(() => StageAsync(release, dir));

        Assert.Equal(UpdateCodes.ArtifactUnexpected, refused.Code);
        Assert.Empty(release.Requested);
    }

    [Fact]
    public async Task A_staged_executable_can_be_run_by_the_person_who_staged_it()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Only a platform with an execute bit has one to restore.");
        if (OperatingSystem.IsWindows())
        {
            return;
        }


        using var dir = new TempTree();
        using var release = new FakeRelease(Next);

        var staged = await StageAsync(release, dir);

        Assert.True(Runnable(staged), "the staged executable cannot be run by the person who staged it");
    }

    private static async Task<string> StageAsync(FakeRelease release, TempTree dir)
    {
        var manifest = UpdateManifest.Read(release.Manifest());
        return await Stager(release).StageAsync(manifest, FakeRelease.Rid, release.Feed, new UpdatePaths(Paths(dir)), Ct);
    }

    /// <summary>Whether the owner may run the file. Asked only where there is a bit to ask about.</summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static bool Runnable(string path) => File.GetUnixFileMode(path).HasFlag(UnixFileMode.UserExecute);

    private static UpdateStager Stager(FakeRelease release) => new(new HttpClient(release, disposeHandler: false));

    private static JasonPaths Paths(TempTree dir) => new(dir.Root);
}
