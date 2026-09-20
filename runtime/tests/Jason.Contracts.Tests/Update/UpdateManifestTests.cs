using Jason.Contracts.Update;

namespace Jason.Contracts.Tests.Update;

/// <summary>
/// The feed, read. Everything here arrives from a web page this runtime did not write, so the document is
/// validated whole before it becomes a manifest — a caller can never hold one whose asset name is a path or
/// whose digest is not a digest.
/// </summary>
/// <remarks>
/// Every hostile document below is built by the test out of the good one. A fixture file would be a second
/// place for the shape to live, and a fixture nobody edits is a fixture that stops matching the parser.
/// </remarks>
public class UpdateManifestTests
{
    private const string Digest = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    [Fact]
    public void A_release_reads_whole()
    {
        var manifest = UpdateManifest.Read(Good());

        Assert.Equal(1, manifest.Schema);
        Assert.Equal(SemanticVersion.Parse("0.2.0"), manifest.Version);
        Assert.Equal(new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero), manifest.PublishedAt);
        Assert.Equal(3, manifest.Artifacts.Count);
        Assert.Equal("jason-win-x64.zip", manifest.Artifacts["win-x64"].Asset);
        Assert.Equal(Digest, manifest.Artifacts["win-x64"].Sha256);
        Assert.Equal(114_254_557, manifest.Artifacts["win-x64"].Size);
        Assert.Equal("https://github.com/reply-team/jason-ai/releases/tag/v0.2.0", manifest.ReleaseNotesUrl);
        Assert.Null(manifest.MinUpgradeFrom);
    }

    [Fact]
    public void Newer_than_what_is_running_is_the_whole_question_it_answers()
    {
        var manifest = UpdateManifest.Read(Good());

        Assert.True(manifest.IsNewerThan(SemanticVersion.Parse("0.1.0")));
        Assert.True(manifest.IsNewerThan(SemanticVersion.Parse("0.2.0-rc.1")));
        Assert.False(manifest.IsNewerThan(SemanticVersion.Parse("0.2.0")));
        Assert.False(manifest.IsNewerThan(SemanticVersion.Parse("0.3.0")));
    }

    [Fact]
    public void The_oldest_version_that_may_upgrade_directly_is_read_where_a_release_names_one()
    {
        var manifest = UpdateManifest.Read(Good().Replace("\"min_upgrade_from\": null", "\"min_upgrade_from\": \"0.1.5\"", StringComparison.Ordinal));

        Assert.Equal(SemanticVersion.Parse("0.1.5"), manifest.MinUpgradeFrom);
    }

    /// <summary>
    /// A release built for platforms this machine is not one of is a perfectly good release. Refusing it would
    /// make a Linux-only build unreadable on Windows, when the honest answer is "nothing here for you".
    /// </summary>
    [Fact]
    public void A_release_that_names_no_artifact_for_this_machine_is_still_a_release()
    {
        var manifest = UpdateManifest.Read(Good(artifacts: Artifact("linux-x64")));

        Assert.Single(manifest.Artifacts);
        Assert.False(manifest.Artifacts.ContainsKey("win-x64"));
    }

    [Theory]
    [MemberData(nameof(Rubbish))]
    public void A_document_that_is_not_a_manifest_is_refused_rather_than_half_read(string what, string json)
    {
        var refused = Assert.Throws<UpdateFeedException>(() => UpdateManifest.Read(json));

        // `what` names the case for a person reading a failure; it is not evidence, so it is carried in the
        // messages rather than asserted. A test that asserts its own label passes whatever the code does.
        Assert.Equal("update_feed_invalid", refused.Code);
        Assert.False(string.IsNullOrWhiteSpace(refused.Message), $"{what} was refused without saying why");
        Assert.DoesNotContain("Exception", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same instant, written two ways, is the same instant — and what comes out is in UTC whichever way it
    /// went in, because everything else in this product reads a moment as UTC.
    /// </summary>
    [Fact]
    public void A_publication_time_means_the_same_instant_in_every_time_zone()
    {
        var zulu = UpdateManifest.Read(Good().Replace("\"2026-09-19T08:00:00Z\"", "\"2026-09-19T08:00:00Z\"", StringComparison.Ordinal));
        var offset = UpdateManifest.Read(Good().Replace("\"2026-09-19T08:00:00Z\"", "\"2026-09-19T13:00:00+05:00\"", StringComparison.Ordinal));

        Assert.Equal(zulu.PublishedAt, offset.PublishedAt);
        Assert.Equal(TimeSpan.Zero, offset.PublishedAt.Offset);
    }

    /// <summary>
    /// And a time with no zone at all is refused rather than guessed at.
    /// </summary>
    /// <remarks>
    /// Guessed at is what happens by default: a parse of "2026-09-19T08:00:00" takes the *reading* machine's
    /// offset, so one manifest means 08:00 in London and 08:00 in Kyiv — two instants three hours apart, decided
    /// by who happened to read it. A release is published at one moment, and a document that cannot say which is
    /// not a manifest.
    /// </remarks>
    [Fact]
    public void A_publication_time_with_no_zone_is_refused_rather_than_read_as_the_reading_machines_time()
    {
        var refused = Assert.Throws<UpdateFeedException>(
            () => UpdateManifest.Read(Good().Replace("\"2026-09-19T08:00:00Z\"", "\"2026-09-19T08:00:00\"", StringComparison.Ordinal)));

        Assert.Equal("update_feed_invalid", refused.Code);
        Assert.Contains("published_at", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A digest is a number written in hexadecimal, and the case of its letters carries nothing. It is read in
    /// either case and kept in one, so everything that compares digests can compare them as text.
    /// </summary>
    /// <remarks>
    /// This build writes lower case and so does the packaging action, so the rule exists for feeds this project
    /// did not write — and refusing them would report a correct file as a corrupt one, which is the worst
    /// message an updater can give.
    /// </remarks>
    [Fact]
    public void A_digest_is_read_in_either_case_and_kept_in_one()
    {
        var digest = new string('a', 60) + "BEEF";
        var manifest = UpdateManifest.Read(Good(artifacts: Artifact("linux-x64", sha256: digest)));

        Assert.Equal(digest.ToLowerInvariant(), manifest.Artifacts["linux-x64"].Sha256);
    }

    public static TheoryData<string, string> Rubbish()
    {
        var data = new TheoryData<string, string>
        {
            { "not json at all", "<html>404</html>" },
            { "an empty document", "{}" },
            { "an array", "[]" },
            { "a version that is not one", Good().Replace("\"0.2.0\"", "\"two point oh\"", StringComparison.Ordinal) },
            { "no version", Good().Replace("\"version\": \"0.2.0\",", string.Empty, StringComparison.Ordinal) },
            { "no schema", Good().Replace("\"schema\": 1,", string.Empty, StringComparison.Ordinal) },
            { "a schema from the future", Good().Replace("\"schema\": 1", "\"schema\": 2", StringComparison.Ordinal) },
            { "no artifacts", Good().Replace("\"artifacts\"", "\"assets\"", StringComparison.Ordinal) },
            { "no artifacts at all", Good(artifacts: string.Empty) },
            { "a published_at that is not a moment", Good().Replace("\"2026-09-19T08:00:00Z\"", "\"last Tuesday\"", StringComparison.Ordinal) },
            { "an asset that is a path", Good(artifacts: Artifact("win-x64", asset: "../../../etc/passwd")) },
            { "an asset with a separator", Good(artifacts: Artifact("win-x64", asset: "dir/jason.zip")) },
            { "an asset that is empty", Good(artifacts: Artifact("win-x64", asset: string.Empty)) },
            { "a digest that is too short", Good(artifacts: Artifact("win-x64", sha256: "abc123")) },
            { "a digest that is not hexadecimal", Good(artifacts: Artifact("win-x64", sha256: new string('z', 64))) },
            { "a digest that is prefixed the way the plugins are", Good(artifacts: Artifact("win-x64", sha256: "sha256:" + Digest)) },
            { "a size of zero", Good(artifacts: Artifact("win-x64", size: "0")) },
            { "a negative size", Good(artifacts: Artifact("win-x64", size: "-1")) },
            { "a size that is not a number", Good(artifacts: Artifact("win-x64", size: "\"large\"")) },
            { "a min_upgrade_from that is not a version", Good().Replace("\"min_upgrade_from\": null", "\"min_upgrade_from\": \"soon\"", StringComparison.Ordinal) },
            { "a release notes url that is not one", Good().Replace("\"https://github.com/reply-team/jason-ai/releases/tag/v0.2.0\"", "\"javascript:alert(1)\"", StringComparison.Ordinal) },
            { "a body past the bound", "{\"schema\":1,\"pad\":\"" + new string('p', UpdateManifest.MaxBytes) + "\"}" },
        };

        return data;
    }

    private static string Good(string? artifacts = null) =>
        $$"""
        {
          "schema": 1,
          "version": "0.2.0",
          "published_at": "2026-09-19T08:00:00Z",
          "release_notes_url": "https://github.com/reply-team/jason-ai/releases/tag/v0.2.0",
          "min_upgrade_from": null,
          "artifacts": { {{artifacts ?? (Artifact("win-x64") + "," + Artifact("linux-x64") + "," + Artifact("osx-arm64"))}} }
        }
        """;

    private static string Artifact(string rid, string? asset = null, string? sha256 = null, string size = "114254557") =>
        $$"""
          "{{rid}}": { "asset": "{{asset ?? Name(rid)}}", "sha256": "{{sha256 ?? Digest}}", "size": {{size}} }
        """;

    private static string Name(string rid) => rid == "win-x64" ? "jason-win-x64.zip" : $"jason-{rid}.tar.gz";
}
