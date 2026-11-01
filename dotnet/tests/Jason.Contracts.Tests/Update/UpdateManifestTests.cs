using Jason.Contracts.Update;
using Jason.Runtime.Tests;

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
        Assert.Empty(manifest.Plugins);
    }

    /// <summary>
    /// The packages a release publishes beside its archives, each with its version, its asset, its content digest and
    /// the digest of every version the package has recorded — the record a reader holding an installed copy's digest
    /// reads that copy's version from.
    /// </summary>
    [Fact]
    public void A_release_names_its_plugin_packages_with_the_digest_of_every_version()
    {
        var manifest = UpdateManifest.Read(Good(plugins: Plugins(Plugin("reply"))));

        var reply = Assert.Single(manifest.Plugins).Value;
        Assert.Equal(SemanticVersion.Parse("0.9.0"), reply.Version);
        Assert.Equal("jason-plugin-reply.zip", reply.Asset);
        Assert.Equal(Newer, reply.Digest);
        Assert.Equal(2, reply.Recorded.Count);
        Assert.Equal(Older, reply.Recorded[SemanticVersion.Parse("0.8.0")]);
        Assert.Equal(SemanticVersion.Parse("0.8.0"), reply.VersionOf(Older));
        Assert.Equal(SemanticVersion.Parse("0.9.0"), reply.VersionOf(Newer));
        Assert.Null(reply.VersionOf("sha256:" + Digest));
    }

    [Fact]
    public void A_plugin_digest_is_read_in_either_case_and_kept_in_one()
    {
        var upper = "sha256:" + Newer["sha256:".Length..].ToUpperInvariant();
        var manifest = UpdateManifest.Read(Good(plugins: Plugins(Plugin("reply", digest: upper, recorded: Recorded(("0.8.0", Older), ("0.9.0", upper))))));

        Assert.Equal(Newer, manifest.Plugins["reply"].Digest);
        Assert.Equal(Newer, manifest.Plugins["reply"].Recorded[SemanticVersion.Parse("0.9.0")]);
    }

    /// <summary>
    /// A release before 0.20.0 names no package, and a feed CI serves names none either; null is the same answer.
    /// </summary>
    [Fact]
    public void A_release_that_names_no_plugin_package_has_none()
    {
        Assert.Empty(UpdateManifest.Read(Good(plugins: "null")).Plugins);
    }

    /// <summary>
    /// A field this build does not know is read past. Every reader since 0.1.0 reads the root's fields by name, which is
    /// what lets a release add one — <c>plugins</c> is one — without a change of schema, and what this row holds.
    /// </summary>
    [Fact]
    public void A_field_this_build_does_not_know_is_read_past()
    {
        var manifest = UpdateManifest.Read(Good().Replace("\"schema\": 1,", "\"schema\": 1, \"later\": { \"anything\": [1, 2] },", StringComparison.Ordinal));

        Assert.Equal(SemanticVersion.Parse("0.2.0"), manifest.Version);
    }

    [Fact]
    [Critical(Family.I2, "the update decision")]
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
    [MemberData(nameof(CriticalRubbish))]
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
    /// A malformed plugin package is refused by its id, and a line of its record by the version, so the refusal says which
    /// line of which package's list to read.
    /// </summary>
    [Theory]
    [MemberData(nameof(PluginRefusals))]
    public void A_malformed_plugin_package_is_refused_naming_the_package_and_the_line(string json, string reason)
    {
        var refused = Assert.Throws<UpdateFeedException>(() => UpdateManifest.Read(json));

        Assert.EndsWith(reason, refused.Message, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> PluginRefusals() => new()
    {
        { Good(plugins: Plugins(Plugin("reply", without: "version"))), "the plugin package 'reply' carries no version." },
        { Good(plugins: Plugins(Plugin("reply", version: "nine"))), "the plugin package 'reply' carries a version that is not one." },
        { Good(plugins: Plugins(Plugin("reply", without: "asset"))), "the plugin package 'reply' carries no 'asset'." },
        { Good(plugins: Plugins(Plugin("reply", without: "digest"))), "the plugin package 'reply' carries no 'digest'." },
        { Good(plugins: Plugins(Plugin("reply", recorded: Recorded(("0.8.0", "sha256:zz"), ("0.9.0", Newer))))), "the plugin package 'reply' records 0.8.0 with a digest that is not 'sha256:' and 64 hexadecimal characters." },
        { Good(plugins: Plugins(Plugin("reply", recorded: Recorded(("0.8.0", Older), ("0.9.0", Newer), ("0.9.0+b", Newer))))), "the plugin package 'reply' records 0.9.0 twice." },
    };

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

    // Critical: a manifest that is not one; a short digest.
    public static TheoryData<string, string> CriticalRubbish() =>
        CriticalRows.Mark(Rubbish(), Family.I2, data => data is ["not json at all", _] or ["a digest that is too short", _]);

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
            { "plugins that are not an object", Good(plugins: "[]") },
            { "a plugin package that is not an object", Good(plugins: Plugins("\"reply\": \"jason-plugin-reply.zip\"")) },
            { "a plugin id that is not one", Good(plugins: Plugins(Plugin("Reply"))) },
            { "a plugin package with no version", Good(plugins: Plugins(Plugin("reply", without: "version"))) },
            { "a plugin version that is not one", Good(plugins: Plugins(Plugin("reply", version: "nine"))) },
            { "a plugin package with no asset", Good(plugins: Plugins(Plugin("reply", without: "asset"))) },
            { "a plugin asset that is a path", Good(plugins: Plugins(Plugin("reply", asset: "../jason-plugin-reply.zip"))) },
            { "a plugin package with no digest", Good(plugins: Plugins(Plugin("reply", without: "digest"))) },
            { "a plugin digest with no prefix", Good(plugins: Plugins(Plugin("reply", digest: Newer["sha256:".Length..], recorded: Recorded(("0.9.0", Newer["sha256:".Length..]))))) },
            { "a plugin digest that is too short", Good(plugins: Plugins(Plugin("reply", digest: "sha256:abc123", recorded: Recorded(("0.9.0", "sha256:abc123"))))) },
            { "a plugin digest of 64 characters, one not hexadecimal", Good(plugins: Plugins(Plugin("reply", digest: NotHexadecimal, recorded: Recorded(("0.8.0", Older), ("0.9.0", NotHexadecimal))))) },
            { "a plugin package with no record", Good(plugins: Plugins(Plugin("reply", without: "recorded"))) },
            { "a record that is an array", Good(plugins: Plugins(Plugin("reply", recorded: "[]"))) },
            { "a recorded version that is not one", Good(plugins: Plugins(Plugin("reply", recorded: Recorded(("eight", Older), ("0.9.0", Newer))))) },
            { "a recorded digest that is not one", Good(plugins: Plugins(Plugin("reply", recorded: Recorded(("0.8.0", "sha256:zz"), ("0.9.0", Newer))))) },
            { "a version recorded twice, once with build metadata", Good(plugins: Plugins(Plugin("reply", recorded: Recorded(("0.8.0", Older), ("0.9.0", Newer), ("0.9.0+b", Newer))))) },
            { "a package whose version is not recorded", Good(plugins: Plugins(Plugin("reply", recorded: Recorded(("0.8.0", Older))))) },
            { "a package whose digest is not the one recorded for its version", Good(plugins: Plugins(Plugin("reply", recorded: Recorded(("0.8.0", Newer), ("0.9.0", Older))))) },
            { "one digest recorded under two versions", Good(plugins: Plugins(Plugin("reply", recorded: Recorded(("0.8.0", Newer), ("0.9.0", Newer))))) },
        };

        return data;
    }

    private static readonly string Older = "sha256:" + new string('a', 64);

    private static readonly string Newer = "sha256:" + new string('b', 64);

    private static readonly string NotHexadecimal = "sha256:" + new string('b', 63) + "g";

    private static string Good(string? artifacts = null, string? plugins = null) =>
        $$"""
        {
          "schema": 1,
          "version": "0.2.0",
          "published_at": "2026-09-19T08:00:00Z",
          "release_notes_url": "https://github.com/reply-team/jason-ai/releases/tag/v0.2.0",
          "min_upgrade_from": null,
          "artifacts": { {{artifacts ?? (Artifact("win-x64") + "," + Artifact("linux-x64") + "," + Artifact("osx-arm64"))}} }{{(plugins is null ? string.Empty : ", \"plugins\": " + plugins)}}
        }
        """;

    private static string Plugins(params string[] packages) => "{ " + string.Join(", ", packages) + " }";

    /// <summary>
    /// The package <paramref name="id"/> as a release names it, with every field but the one <paramref name="without"/>
    /// names, if any; <paramref name="recorded"/> is the record's whole value.
    /// </summary>
    private static string Plugin(string id, string version = "0.9.0", string asset = "jason-plugin-reply.zip", string? digest = null, string? recorded = null, string? without = null)
    {
        (string Name, string Value)[] fields =
        [
            ("version", $"\"{version}\""),
            ("asset", $"\"{asset}\""),
            ("digest", $"\"{digest ?? Newer}\""),
            ("recorded", recorded ?? Recorded(("0.8.0", Older), ("0.9.0", Newer))),
        ];

        if (without is not null && !fields.Any(field => field.Name == without))
        {
            throw new ArgumentException($"A plugin package has no field '{without}' to leave out.", nameof(without));
        }

        return $"\"{id}\": {{ " + string.Join(", ", fields.Where(field => field.Name != without).Select(field => $"\"{field.Name}\": {field.Value}")) + " }";
    }

    private static string Recorded(params (string Version, string Digest)[] lines) =>
        "{ " + string.Join(", ", lines.Select(line => $"\"{line.Version}\": \"{line.Digest}\"")) + " }";

    private static string Artifact(string rid, string? asset = null, string? sha256 = null, string size = "114254557") =>
        $$"""
          "{{rid}}": { "asset": "{{asset ?? Name(rid)}}", "sha256": "{{sha256 ?? Digest}}", "size": {{size}} }
        """;

    private static string Name(string rid) => rid == "win-x64" ? "jason-win-x64.zip" : $"jason-{rid}.tar.gz";
}
