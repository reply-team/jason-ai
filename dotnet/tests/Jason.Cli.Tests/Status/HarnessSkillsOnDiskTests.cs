using Jason.Cli.Skills;
using Jason.Cli.Status;
using Jason.Contracts.Skills;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The harness check reads the files a deployment record names, not the record alone. An uninstall keeps the record
/// beside a file the person edited, so a harness root can hold the record of two packs and almost none of their skills:
/// the check answered <c>ok</c> there, naming both packs, while the agent reading that root found nothing to read.
/// </summary>
public sealed class HarnessSkillsOnDiskTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_record_whose_skills_are_gone_fails_the_check_with_the_update_as_the_repair()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var harness = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;
        StatusCommandTests.Deploy(harness);
        Directory.Delete(Path.Combine(harness, "researcher"), recursive: true);

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, harnesses: HarnessLocators.At(harness)), Ct);

        var report = StatusCommandTests.Read(output);
        var check = Assert.Single(report.Checks, check => check.Name == "harness_skills");
        Assert.Equal(CheckState.Failed, check.State);
        Assert.False(check.Required);
        Assert.Equal("jason skills update", check.Fix);
        Assert.Equal($"jason-runtime-skills in {harness}: 1 of the 1 file(s) its record names are not there.", check.Fact);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    /// <summary>And a pack missing some of its files is named with the count, beside a pack that is whole.</summary>
    [Fact]
    public async Task A_pack_missing_part_of_its_files_is_named_and_a_whole_one_is_not()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var harness = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;
        var kept = Path.Combine(harness, "planner", "SKILL.md");
        var gone = Path.Combine(harness, "planner", "notes.md");
        var whole = Path.Combine(harness, "scout", "SKILL.md");
        foreach (var file in new[] { kept, gone, whole })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "made up" + Environment.NewLine, Ct);
        }

        SkillsRecord.Write(harness, new SkillsRecord(SkillsRecord.CurrentVersion,
        [
            new SkillsDeployment("jason-runtime-skills", "/somewhere", "v0.1.0", false, null, DateTimeOffset.UnixEpoch,
                [new DeployedFile("planner/SKILL.md", SkillsRecord.Digest(kept)), new DeployedFile("planner/notes.md", SkillsRecord.Digest(gone))]),
            new SkillsDeployment("jason-business-skills", "/somewhere", "v0.1.0", false, null, DateTimeOffset.UnixEpoch,
                [new DeployedFile("scout/SKILL.md", SkillsRecord.Digest(whole))]),
        ]));
        File.Delete(gone);

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, harnesses: HarnessLocators.At(harness)), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "harness_skills");
        Assert.Equal(CheckState.Failed, check.State);
        Assert.Equal("jason skills update", check.Fix);
        Assert.Equal($"jason-runtime-skills in {harness}: 1 of the 2 file(s) its record names are not there.", check.Fact);
    }

    /// <summary>
    /// The repair writes a missing file back from the source and at the ref the record names, into the roots it names. An
    /// install that names neither would move a pack made from another source, or pinned with --ref, to this build's
    /// default, and deploy into every harness on the machine besides.
    /// </summary>
    [Fact]
    public async Task A_pinned_pack_from_another_source_with_a_file_gone_names_the_update_and_not_the_install()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var harness = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;
        var kept = Path.Combine(harness, "planner", "SKILL.md");
        var gone = Path.Combine(harness, "planner", "notes.md");
        foreach (var file in new[] { kept, gone })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            await File.WriteAllTextAsync(file, "made up" + Environment.NewLine, Ct);
        }

        SkillsRecord.Write(harness, new SkillsRecord(SkillsRecord.CurrentVersion,
        [
            new SkillsDeployment("jason-runtime-skills", "https://example.invalid/fork.git", "v0.0.9", true, null, DateTimeOffset.UnixEpoch,
                [new DeployedFile("planner/SKILL.md", SkillsRecord.Digest(kept)), new DeployedFile("planner/notes.md", SkillsRecord.Digest(gone))]),
        ]));
        File.Delete(gone);

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, harnesses: HarnessLocators.At(harness)), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "harness_skills");
        Assert.Equal(CheckState.Failed, check.State);
        Assert.Equal("jason skills update", check.Fix);
    }

    /// <summary>A file the person edited is still there: the check is about what is missing, never about an edit.</summary>
    [Fact]
    public async Task A_skill_the_person_edited_is_there_and_passes()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        var harness = Directory.CreateDirectory(Path.Combine(dir.Paths.Root, "harness")).FullName;
        StatusCommandTests.Deploy(harness);
        await File.AppendAllTextAsync(Path.Combine(harness, "researcher", "SKILL.md"), "A line of my own." + Environment.NewLine, Ct);

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output, harnesses: HarnessLocators.At(harness)), Ct);

        var check = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "harness_skills");
        Assert.Equal(CheckState.Ok, check.State);
        Assert.Equal($"jason-runtime-skills at v0.1.0 in {harness}.", check.Fact);
    }
}
