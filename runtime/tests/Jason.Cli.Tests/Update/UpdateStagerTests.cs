using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jason.Cli.Update;
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

    [Fact]
    public async Task A_staged_release_is_the_one_file_a_release_carries()
    {
        using var dir = new TempTree();
        using var release = new FakeRelease(Next);

        var staged = await StageAsync(release, dir);

        Assert.Equal(Path.Combine(dir.Root, "update", "staged", "0.2.0", ReleaseAssets.ExecutableName), staged);
        Assert.True(File.Exists(staged));

        // The licence travelled in the archive and is not what an update installs: one file comes out.
        Assert.Equal([ReleaseAssets.ExecutableName], Directory.EnumerateFiles(Path.GetDirectoryName(staged)!).Select(Path.GetFileName));
    }

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

    [Fact]
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
    /// Every hostile archive shape, and the answer to all of them is the same: take the one entry whose whole
    /// name is the executable, and ignore the rest. There is no path to sanitise because no path is ever used.
    /// </summary>
    [Theory]
    [MemberData(nameof(HostileArchives))]
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

        // And nothing else came out anywhere: not beside it, not above it.
        Assert.Equal(
            [ReleaseAssets.ExecutableName],
            Directory.EnumerateFiles(Path.GetDirectoryName(executable)!).Select(Path.GetFileName));
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
