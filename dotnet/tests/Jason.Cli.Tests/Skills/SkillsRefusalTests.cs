using Jason.Cli;
using Jason.Cli.Skills;
using Jason.Cli.Tests.Autostart;
using Jason.Cli.Tests.Process;
using Jason.Contracts.Skills;

namespace Jason.Cli.Tests.Skills;

/// <summary>
/// What a refused replacement leaves behind. It is the case this whole design exists for — a launch reading
/// the role's skill at the moment the deployment reaches it — so it has to be survivable, and survivable means
/// the next run finds a machine it can still work on.
/// </summary>
public class SkillsRefusalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// A role whose replacement was refused keeps the record it had.
    /// </summary>
    /// <remarks>
    /// The record describes what is on disk. A refusal leaves the old tree exactly where it was, so recording
    /// the new digests says the deployment happened when it did not — and the failure is not cosmetic: the
    /// next run compares disk against a record that never matched it, reads the difference as the operator's
    /// own edit, and refuses until somebody types <c>--force</c> about a file Jason wrote. A transient
    /// condition, expected by design, would permanently wedge the verb and blame the person.
    /// </remarks>
    [Fact]
    public async Task A_role_whose_replacement_was_refused_keeps_the_record_it_had()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Holding a file open is what refuses a directory rename, and only Windows refuses it. Elsewhere
            // the replacement succeeds and there is no refusal to have a record for.
            return;
        }

        using var dir = new TempPaths();
        var source = Source(dir, "FIRST");
        await InstallAsync(dir, source);

        var live = Path.Combine(dir.Paths.RoleSkillsDirectory, "researcher");
        var before = SkillsRecord.Read(dir.Paths.RoleSkillsDirectory)!;
        var recordedBefore = before.Packs.SelectMany(pack => pack.Files).Single(file => file.Path.StartsWith("researcher/", StringComparison.Ordinal));

        // A launch is reading this role's skill, which is what refuses the rename.
        using (var held = File.Open(Path.Combine(live, "SKILL.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var refused = await InstallAsync(dir, Source(dir, "SECOND-AND-LONGER"), expectSuccess: false);
            Assert.Contains("something is holding this role's skill open", refused.Error, StringComparison.Ordinal);
        }

        // The live tree is the old one, and so is the record.
        var after = SkillsRecord.Read(dir.Paths.RoleSkillsDirectory)!;
        var recordedAfter = after.Packs.SelectMany(pack => pack.Files).Single(file => file.Path.StartsWith("researcher/", StringComparison.Ordinal));
        Assert.Equal(recordedBefore.Sha256, recordedAfter.Sha256);
        Assert.Equal(SkillsRecord.Digest(Path.Combine(live, "SKILL.md")), recordedAfter.Sha256);

        // And the next run, with nothing held, installs rather than accusing the operator of an edit.
        var third = await InstallAsync(dir, Source(dir, "SECOND-AND-LONGER"));
        Assert.DoesNotContain("you have edited", third.Error, StringComparison.Ordinal);
        Assert.Contains("SECOND-AND-LONGER", await File.ReadAllTextAsync(Path.Combine(live, "SKILL.md"), Ct), StringComparison.Ordinal);
    }

    /// <summary>
    /// A pack with one skill refused and another moved records each as it now is on disk: the held one with the files it
    /// had, the moved one with the files it was given.
    /// </summary>
    /// <remarks>
    /// The pack's record stays the old deployment's, since one of its skills is still the old tree, but its file list has
    /// to be the disk's. Kept whole, the moved skill's digests are the bytes it replaced, and a later deployment from a
    /// source that changed it again reads the difference as the operator's own edit and refuses without
    /// <c>--force</c>. A re-run of the same source cannot show that — the moved skill already matches it — so the last
    /// run here is from a third.
    /// </remarks>
    [Fact]
    public async Task A_pack_with_one_skill_refused_and_one_moved_records_each_as_it_is_on_disk()
    {
        if (!OperatingSystem.IsWindows())
        {
            // As above: only Windows refuses a directory rename while a file inside is open.
            return;
        }

        using var dir = new TempPaths();
        await InstallAsync(dir, Source(dir, "FIRST", "researcher", "writer"));
        var roles = dir.Paths.RoleSkillsDirectory;

        using (var held = File.Open(Path.Combine(roles, "researcher", "SKILL.md"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var refused = await InstallAsync(dir, Source(dir, "SECOND", "researcher", "writer"), expectSuccess: false);
            Assert.Contains("'researcher' could not be replaced", refused.Error, StringComparison.Ordinal);
        }

        Assert.Contains("FIRST", await File.ReadAllTextAsync(Path.Combine(roles, "researcher", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.Contains("SECOND", await File.ReadAllTextAsync(Path.Combine(roles, "writer", "SKILL.md"), Ct), StringComparison.Ordinal);
        var recorded = Assert.Single(SkillsRecord.Read(roles)!.Packs).Files;
        foreach (var role in (string[])["researcher", "writer"])
        {
            var onDisk = SkillsRecord.Digest(Path.Combine(roles, role, "SKILL.md"));
            var file = Assert.Single(recorded, candidate => candidate.Path == $"{role}/SKILL.md");
            Assert.True(file.Sha256 == onDisk, $"The record says {role}/SKILL.md is {file.Sha256}; on disk it is {onDisk}.");
        }

        var third = await InstallAsync(dir, Source(dir, "THIRD", "researcher", "writer"), expectSuccess: false);
        Assert.True(third.Exit == ExitCodes.Success, $"The deployment after the refusal exited {third.Exit}: {third.Error}");
        Assert.DoesNotContain("you have edited", third.Error, StringComparison.Ordinal);
        Assert.Contains("THIRD", await File.ReadAllTextAsync(Path.Combine(roles, "researcher", "SKILL.md"), Ct), StringComparison.Ordinal);
        Assert.Contains("THIRD", await File.ReadAllTextAsync(Path.Combine(roles, "writer", "SKILL.md"), Ct), StringComparison.Ordinal);
    }

    private static string Harness(TempPaths dir) =>
        Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;

    private static async Task<(int Exit, string Output, string Error)> InstallAsync(TempPaths dir, string source, bool expectSuccess = true)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = await CliApp.RunAsync(
            ["skills", "install", "--source", source, "--root", Harness(dir)],
            new CliEnvironment(
                output,
                error,
                dir.Paths,
                Processes: new FakeProcessControl(),
                Autostart: new RecordingRegistrar(),
                Programs: new FakeProgramRunner()),
            Ct);

        if (expectSuccess)
        {
            Assert.True(exit == ExitCodes.Success, $"The deployment failed: {error}");
        }

        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>A directory source whose roles each say <paramref name="marker"/>: the researcher, unless others are named.</summary>
    private static string Source(TempPaths dir, string marker, params string[] roles)
    {
        var root = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, $"source-{marker}")).FullName;
        foreach (var role in roles is [] ? ["researcher"] : roles)
        {
            var skill = Path.Combine(root, "skills", "runtime", "roles", role);
            Directory.CreateDirectory(skill);
            File.WriteAllText(
                Path.Combine(skill, "SKILL.md"),
                $"---\nname: {role}\ndescription: one line\n---\n\n{marker}\n");
        }

        return root;
    }
}
