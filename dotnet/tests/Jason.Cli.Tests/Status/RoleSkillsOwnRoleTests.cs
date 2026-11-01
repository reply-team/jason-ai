using Jason.Cli.Status;
using Jason.Contracts.Api;
using Jason.Contracts.Skills;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Status;

/// <summary>
/// The role skills check requires a deployed skill for the roles Jason ships, and only for them. A role a person added
/// themselves — with an entry command of their own, or without one — is taught by nothing Jason deploys: its repair,
/// <c>jason skills install --roles-only</c>, writes the shipped roles alone. Failing a required check over it left an
/// installation not ready for good, with a repair that changed nothing.
/// </summary>
public sealed class RoleSkillsOwnRoleTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_role_of_your_own_with_its_own_entry_command_is_reported_and_not_failed()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        StatusCommandTests.OwnRole(dir, "night-watch", "bash", "-c", "true");

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var skills = Assert.Single(report.Checks, check => check.Name == "role_skills");
        Assert.Equal(CheckState.Ok, skills.State);
        Assert.Null(skills.Fix);
        Assert.Contains("'night-watch' is a role of your own: it runs its own entry command, and no skill Jason ships teaches it.", skills.Fact, StringComparison.Ordinal);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    [Fact]
    public async Task A_role_of_your_own_with_no_entry_command_is_reported_as_launching_untaught()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir);
        StatusCommandTests.OwnRole(dir, "scout");

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var report = StatusCommandTests.Read(output);
        var skills = Assert.Single(report.Checks, check => check.Name == "role_skills");
        Assert.Equal(CheckState.Ok, skills.State);
        Assert.Contains("'scout' is a role of your own, and no skill Jason ships teaches it: it launches untaught unless a skill named after it is in /data/skills/roles.", skills.Fact, StringComparison.Ordinal);
        Assert.True(report.Ready);
        Assert.Equal(ExitCodes.Success, exit);
    }

    /// <summary>And a shipped role with no skill still fails, beside a role of your own: the count is of the shipped ones.</summary>
    [Fact]
    public async Task A_shipped_role_with_no_skill_still_fails_beside_one_of_your_own()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir, roles: ["researcher", "planner"]);
        StatusCommandTests.OwnRole(dir, "night-watch", "bash", "-c", "true");

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var skills = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "role_skills");
        Assert.Equal(CheckState.Failed, skills.State);
        Assert.Equal("jason skills install --roles-only", skills.Fix);
        Assert.Contains("1 of 2 seeded roles have no skill, so they would launch untaught: planner.", skills.Fact, StringComparison.Ordinal);
        Assert.DoesNotContain("night-watch", skills.Fact, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.ApiError, exit);
    }

    /// <summary>
    /// A skill of your own role that the launcher would refuse still fails the check, since every launch of that role is
    /// refused. But the repair the shipped roles have writes the shipped roles alone, so none is named; the fact names the
    /// directory the skill is corrected or removed in.
    /// </summary>
    [Fact]
    public async Task A_refused_skill_of_your_own_role_fails_with_no_repair_and_names_the_directory()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir, skills: new SkillsInfo(
            "/data/skills/roles",
            4096,
            [
                StatusCommandTests.Taught("researcher"),
                new DeployedRoleSkill("scout", 12, "'/data/skills/roles/scout' holds no SKILL.md, so a launch copies what is there and the host loads no skill from it.", SkillProblemCodes.SkillFileMissing),
            ]));
        StatusCommandTests.OwnRole(dir, "scout");

        var exit = await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var skills = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "role_skills");
        Assert.Equal(CheckState.Failed, skills.State);
        Assert.Null(skills.Fix);
        Assert.EndsWith(
            "[skill_file_missing] 'scout' is no role Jason ships, so no command of Jason's repairs its skill: correct it or remove it from /data/skills/roles.",
            skills.Fact,
            StringComparison.Ordinal);
        Assert.Equal(ExitCodes.ApiError, exit);
    }

    /// <summary>And beside a refused skill of a shipped role, the repair is named for that one and your own is still said.</summary>
    [Fact]
    public async Task A_refused_shipped_skill_beside_one_of_your_own_names_the_repair_and_both()
    {
        using var dir = new TempPaths();
        var output = new StringWriter();
        StatusCommandTests.Running(dir, skills: new SkillsInfo(
            "/data/skills/roles",
            4096,
            [
                new DeployedRoleSkill("researcher", 5000, "The skill in '/data/skills/roles/researcher' is 5000 bytes and Roles:MaxSkillBytes allows 4096.", SkillProblemCodes.SkillTooLarge),
                new DeployedRoleSkill("scout", 12, "'/data/skills/roles/scout' holds no SKILL.md, so a launch copies what is there and the host loads no skill from it.", SkillProblemCodes.SkillFileMissing),
            ]));
        StatusCommandTests.OwnRole(dir, "scout");

        await CliApp.RunAsync(["status"], StatusCommandTests.Machine(dir, output), Ct);

        var skills = Assert.Single(StatusCommandTests.Read(output).Checks, check => check.Name == "role_skills");
        Assert.Equal(CheckState.Failed, skills.State);
        Assert.Equal("jason skills install --roles-only", skills.Fix);
        Assert.Contains("[skill_too_large]", skills.Fact, StringComparison.Ordinal);
        Assert.Contains("'scout' is no role Jason ships, so no command of Jason's repairs its skill", skills.Fact, StringComparison.Ordinal);
    }
}
