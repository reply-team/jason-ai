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
    /// <summary>
    /// A record whose shape is there and whose contents are not — a list that is null, a pack with no files, a
    /// file with no path — is unreadable, and says which root, rather than throwing a NullReferenceException that
    /// names nothing from inside every verb that reads it. An uninstall that met one could remove nothing at all.
    /// </summary>
    [Theory]
    [InlineData("{\"version\":1,\"packs\":null}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"packs\":[null]}")]
    [InlineData("{\"version\":1,\"packs\":[{\"pack\":\"p\",\"source\":\"s\",\"ref\":\"r\",\"ref_overridden\":false,\"installed_at\":\"2026-09-24T00:00:00Z\",\"files\":null}]}")]
    [InlineData("{\"version\":1,\"packs\":[{\"pack\":null,\"source\":\"s\",\"ref\":\"r\",\"ref_overridden\":false,\"installed_at\":\"2026-09-24T00:00:00Z\",\"files\":[]}]}")]
    [InlineData("{\"version\":1,\"packs\":[{\"pack\":\"p\",\"source\":\"s\",\"ref\":\"r\",\"ref_overridden\":false,\"installed_at\":\"2026-09-24T00:00:00Z\",\"files\":[null]}]}")]
    [InlineData("{\"version\":1,\"packs\":[{\"pack\":\"p\",\"source\":\"s\",\"ref\":\"r\",\"ref_overridden\":false,\"installed_at\":\"2026-09-24T00:00:00Z\",\"files\":[{\"path\":null,\"sha256\":\"aa\"}]}]}")]
    [InlineData("{\"version\":1,\"packs\":[{\"pack\":\"p\",\"source\":\"s\",\"ref\":\"r\",\"ref_overridden\":false,\"installed_at\":\"2026-09-24T00:00:00Z\",\"files\":[{\"path\":\"x/SKILL.md\",\"sha256\":null}]}]}")]
    public void A_record_with_something_missing_is_unreadable_and_names_its_root(string document)
    {
        using var tree = new TempPaths();
        File.WriteAllText(Path.Combine(tree.Paths.Root, SkillsRecord.FileName), document);

        var unreadable = Assert.Throws<SkillsRecordUnreadable>(() => SkillsRecord.Read(tree.Paths.Root));

        Assert.Contains(tree.Paths.Root, unreadable.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A path a record names is inside its root, or the record is unreadable. The uninstall removes what a record
    /// names, and the installer compares against it: a record naming <c>../outside/SKILL.md</c> or a rooted path
    /// had the uninstall remove a file outside the root, and empty the directories above it.
    /// </summary>
    [Theory]
    [InlineData("../outside/SKILL.md")]
    [InlineData("skill/../../outside/SKILL.md")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("/etc/SKILL.md")]
    [InlineData("C:/Windows/SKILL.md")]
    [InlineData("C:SKILL.md")]
    [InlineData("\\\\server\\share\\SKILL.md")]
    public void A_record_naming_a_path_outside_its_root_is_unreadable(string path)
    {
        using var tree = new TempPaths();
        SkillsRecord.Write(tree.Paths.Root, new SkillsRecord(SkillsRecord.CurrentVersion, [Deployment() with { Files = [new DeployedFile(path, new string('a', 64))] }]));

        var unreadable = Assert.Throws<SkillsRecordUnreadable>(() => SkillsRecord.Read(tree.Paths.Root));

        Assert.Contains("outside", unreadable.Message, StringComparison.Ordinal);
    }

    /// <summary>And a path that only looks like it climbs — a directory whose name begins with two dots — is fine.</summary>
    [Fact]
    public void A_path_that_stays_inside_its_root_is_read()
    {
        using var tree = new TempPaths();
        SkillsRecord.Write(tree.Paths.Root, new SkillsRecord(SkillsRecord.CurrentVersion, [Deployment() with { Files = [new DeployedFile("..hidden/SKILL.md", new string('a', 64)), new DeployedFile("a/./b/SKILL.md", new string('a', 64))] }]));

        Assert.Equal(2, Assert.Single(SkillsRecord.Read(tree.Paths.Root)!.Packs).Files.Count);
    }

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
