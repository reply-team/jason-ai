using Jason.Cli.Skills;
using Jason.Contracts.Skills;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// The record a deployment leaves in every root it writes into: what is there, where it came from, and every
/// path this installer wrote.
/// </summary>
/// <remarks>
/// Two verbs read it. One answers "is this current?"; the other removes exactly the paths it names. A verb
/// that deleted by pattern would eventually delete somebody's own file, and a root that holds other people's
/// skills is precisely where that would happen.
/// </remarks>
public class SkillsRecordTests
{
    private static SkillsDeployment Deployment(string pack = "jason-runtime-skills") =>
        new(
            pack,
            "/somewhere/clone",
            "v0.1.0",
            false,
            "0b08fba",
            DateTimeOffset.UnixEpoch,
            [new DeployedFile("managed-campaign-work/SKILL.md", new string('a', 64))]);

    [Fact]
    public void A_record_round_trips_and_names_every_path_that_was_written()
    {
        using var tree = new TempPaths();
        var record = new SkillsRecord(SkillsRecord.CurrentVersion, [Deployment()]);

        SkillsRecord.Write(tree.Paths.Root, record);

        // Field by field rather than record equality: a record's generated equality compares the list by
        // reference, so `Assert.Equal(record, read)` would fail on two identical documents and pass on none.
        var read = SkillsRecord.Read(tree.Paths.Root);
        Assert.NotNull(read);
        Assert.Equal(record.Version, read.Version);
        var pack = Assert.Single(read.Packs);
        Assert.Equal(record.Packs[0] with { Files = [] }, pack with { Files = [] });
        Assert.Equal(record.Packs[0].Files, pack.Files);
        Assert.True(File.Exists(Path.Combine(tree.Paths.Root, SkillsRecord.FileName)));
    }

    /// <summary>No record is a root nobody installed into. That is an answer, and it is answerable.</summary>
    [Fact]
    public void A_root_with_no_record_reads_as_none()
    {
        using var tree = new TempPaths();

        Assert.Null(SkillsRecord.Read(tree.Paths.Root));
    }

    /// <summary>And a root that is not there at all is the same answer rather than a failure.</summary>
    [Fact]
    public void A_root_that_does_not_exist_reads_as_none()
    {
        using var tree = new TempPaths();

        Assert.Null(SkillsRecord.Read(Path.Combine(tree.Paths.Root, "nowhere")));
    }

    /// <summary>
    /// A file that is there and is not a record is the one thing that must not read as "nothing installed": an
    /// uninstall that took that for absent would remove nothing and report success, and a status verb would
    /// call a root clean that is not.
    /// </summary>
    [Fact]
    public void A_record_that_cannot_be_read_is_not_the_same_as_no_record()
    {
        using var tree = new TempPaths();
        File.WriteAllText(Path.Combine(tree.Paths.Root, SkillsRecord.FileName), "{ not json");

        var refusal = Assert.Throws<SkillsRecordUnreadable>(() => SkillsRecord.Read(tree.Paths.Root));

        Assert.Contains(tree.Paths.Root, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>And so is one this build has no way to understand: a newer writer left it.</summary>
    [Fact]
    public void A_record_from_a_later_version_is_not_guessed_at()
    {
        using var tree = new TempPaths();
        File.WriteAllText(
            Path.Combine(tree.Paths.Root, SkillsRecord.FileName),
            """{"version":99,"packs":[]}""");

        var refusal = Assert.Throws<SkillsRecordUnreadable>(() => SkillsRecord.Read(tree.Paths.Root));

        Assert.Contains("99", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A second pack goes in beside the first rather than over it. One root holds both the interactive runtime
    /// skills and the business pack, and installing one is not uninstalling the other.
    /// </summary>
    [Fact]
    public void A_pack_deployed_into_a_root_that_already_holds_one_joins_it()
    {
        using var tree = new TempPaths();
        SkillsRecord.Write(tree.Paths.Root, new SkillsRecord(SkillsRecord.CurrentVersion, [Deployment()]));

        var before = SkillsRecord.Read(tree.Paths.Root)!;
        SkillsRecord.Write(
            tree.Paths.Root,
            before with { Packs = [.. before.Packs, Deployment("jason-business-skills")] });

        var after = SkillsRecord.Read(tree.Paths.Root)!;
        Assert.Equal(2, after.Packs.Count);
        Assert.Contains(after.Packs, pack => pack.Pack == "jason-business-skills");
    }

    /// <summary>
    /// Written whole or not at all. The record is what says a deployment finished, so a half-written one is
    /// the single state nothing could recover from: it would read as a deployment that is current and name
    /// files that were never written.
    /// </summary>
    [Fact]
    public void Writing_a_record_leaves_no_partial_file_behind()
    {
        using var tree = new TempPaths();
        SkillsRecord.Write(tree.Paths.Root, new SkillsRecord(SkillsRecord.CurrentVersion, [Deployment()]));

        SkillsRecord.Write(tree.Paths.Root, new SkillsRecord(SkillsRecord.CurrentVersion, [Deployment("jason-business-skills")]));

        Assert.Single(SkillsRecord.Read(tree.Paths.Root)!.Packs);
        Assert.DoesNotContain(
            Directory.GetFiles(tree.Paths.Root, SkillsRecord.FileName + "*"),
            file => !file.EndsWith(SkillsRecord.FileName, StringComparison.Ordinal));
    }

    /// <summary>The digest a deployment records is the digest of what is on disk, spelled one way.</summary>
    [Fact]
    public void A_files_digest_is_the_sha256_of_its_bytes_in_lowercase_hex()
    {
        using var tree = new TempPaths();
        var file = Path.Combine(tree.Paths.Root, "SKILL.md");
        File.WriteAllText(file, "body");

        var digest = SkillsRecord.Digest(file);

        Assert.Equal(64, digest.Length);
        Assert.Equal(digest.ToLowerInvariant(), digest);
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))),
            digest);
    }
}
