using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Skills;

namespace Jason.Runtime.Tests.Hosting;

/// <summary>
/// Every problem <c>system.info</c> reports about the role skills carries its code beside its sentence, and the
/// operating system's refusals carry the closed word that names them: the code is what a diagnostic bundle keeps where
/// it withholds the sentence. Each reason is staged on a real directory, the way an operator's machine would hold it.
/// </summary>
public sealed class SkillProblemCodesInSystemInfoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("too large", SkillProblemCodes.SkillTooLarge, null)]
    [InlineData("no skill file", SkillProblemCodes.SkillFileMissing, null)]
    [InlineData("too deep", SkillProblemCodes.RoleDirectoryTooDeep, null)]
    [InlineData("misnamed", SkillProblemCodes.SkillNameMismatch, null)]
    [InlineData("unnamed", SkillProblemCodes.SkillNameMissing, null)]
    [InlineData("skill file denied", SkillProblemCodes.SkillFileUnreadable, "UnauthorizedAccessException")]
    [InlineData("part of the tree denied", SkillProblemCodes.RoleDirectoryUnreadable, "UnauthorizedAccessException")]
    public async Task A_role_s_problem_carries_its_code_and_any_refusal_s_word(string reason, string code, string? exception)
    {
        Assert.SkipWhen(
            exception is not null && !OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess,
            "The superuser reads anything, so nothing here can be denied.");
        IDisposable? denial = null;
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                Directory.CreateDirectory(paths.ConfigDirectory);
                File.WriteAllText(paths.UserSettingsFile, """{"Dispatcher":{"Enabled":false},"Roles":{"MaxSkillBytes":4096}}""");
                denial = Stage(Path.Combine(paths.RoleSkillsDirectory, "researcher"), reason);
            });
        try
        {
            var role = (await SkillsAsync(fixture))["roles"]!.AsArray().Single()!.AsObject();

            Assert.True(role["problem"] is not null, $"No problem was reported for '{reason}': {role.ToJsonString()}");
            Assert.Equal(code, role["problem_code"]?.GetValue<string>());
            Assert.Equal(exception, role["problem_exception"]?.GetValue<string>());
        }
        finally
        {
            denial?.Dispose();
        }
    }

    /// <summary>The root itself denied: the section says so for the whole, with its code and the refusal's word.</summary>
    [Fact]
    public async Task A_skills_directory_that_cannot_be_listed_carries_its_code_and_the_refusal_s_word()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "The superuser reads anything, so nothing here can be denied.");
        IDisposable? denial = null;
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                Deploy(Path.Combine(paths.RoleSkillsDirectory, "researcher"), "researcher");
                denial = Denial.Of(paths.RoleSkillsDirectory);
            });
        try
        {
            var skills = await SkillsAsync(fixture);

            Assert.True(skills["problem"] is not null, skills.ToJsonString());
            Assert.Equal(SkillProblemCodes.SkillsDirectoryUnreadable, skills["problem_code"]?.GetValue<string>());
            Assert.Equal("UnauthorizedAccessException", skills["problem_exception"]?.GetValue<string>());
        }
        finally
        {
            denial?.Dispose();
        }
    }

    /// <summary>A role with nothing wrong carries no code at all.</summary>
    [Fact]
    public async Task A_role_with_nothing_wrong_carries_no_code()
    {
        await using var fixture = await RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths => Deploy(Path.Combine(paths.RoleSkillsDirectory, "researcher"), "researcher"));

        var role = (await SkillsAsync(fixture))["roles"]!.AsArray().Single()!.AsObject();

        Assert.Null(role["problem"]);
        Assert.Null(role["problem_code"]);
        Assert.Null(role["problem_exception"]);
    }

    private static async Task<JsonObject> SkillsAsync(RuntimeApiFixture fixture)
    {
        var (status, body) = await fixture.PostAsync(Operations.SystemInfo, null, Ct);
        Assert.Equal(System.Net.HttpStatusCode.OK, status);
        return JsonNode.Parse(body)!["skills"]!.AsObject();
    }

    /// <summary>One role directory holding the reason, and the denial to lift afterwards where there is one.</summary>
    private static IDisposable? Stage(string role, string reason)
    {
        switch (reason)
        {
            case "too large":
                Deploy(role, "researcher", padding: 5_000);
                return null;
            case "no skill file":
                Directory.CreateDirectory(role);
                File.WriteAllText(Path.Combine(role, "references.md"), "where to look");
                return null;
            case "too deep":
                Deploy(role, "researcher");
                var deep = role;
                for (var level = 0; level <= RoleSkillRules.MaxDepth + 2; level++)
                {
                    deep = Path.Combine(deep, $"level-{level}");
                }

                Directory.CreateDirectory(deep);
                return null;
            case "misnamed":
                Deploy(role, "planner");
                return null;
            case "unnamed":
                Directory.CreateDirectory(role);
                File.WriteAllText(Path.Combine(role, RoleSkillRules.SkillFile), "no front matter at all\n");
                return null;
            case "skill file denied":
                Deploy(role, "researcher");
                return Denial.Of(Path.Combine(role, RoleSkillRules.SkillFile));
            case "part of the tree denied":
                Deploy(role, "researcher");
                var notes = Directory.CreateDirectory(Path.Combine(role, "notes")).FullName;
                File.WriteAllText(Path.Combine(notes, "kept.md"), "kept");
                return Denial.Of(notes);
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "Not a staged reason.");
        }
    }

    private static void Deploy(string directory, string role, int padding = 0)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, RoleSkillRules.SkillFile),
            $"---\nname: {role}\ndescription: one line\n---\n\n{new string('x', padding)}\n");
    }
}

/// <summary>Reading denied to this account for one file or directory, lifted when disposed.</summary>
internal static class Denial
{
    public static IDisposable Of(string path) => OperatingSystem.IsWindows() ? OnWindows(path) : ByMode(path);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IDisposable OnWindows(string path)
    {
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.ReadData,
            System.Security.AccessControl.AccessControlType.Deny);
        if (Directory.Exists(path))
        {
            var directory = new DirectoryInfo(path);
            var security = directory.GetAccessControl();
            security.AddAccessRule(rule);
            directory.SetAccessControl(security);
            return new Lifted(() => LiftDirectory(directory, rule));
        }

        var file = new FileInfo(path);
        var fileSecurity = file.GetAccessControl();
        fileSecurity.AddAccessRule(rule);
        file.SetAccessControl(fileSecurity);
        return new Lifted(() => LiftFile(file, rule));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void LiftDirectory(DirectoryInfo directory, System.Security.AccessControl.FileSystemAccessRule rule)
    {
        var security = directory.GetAccessControl();
        security.RemoveAccessRule(rule);
        directory.SetAccessControl(security);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void LiftFile(FileInfo file, System.Security.AccessControl.FileSystemAccessRule rule)
    {
        var security = file.GetAccessControl();
        security.RemoveAccessRule(rule);
        file.SetAccessControl(security);
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static IDisposable ByMode(string path)
    {
        var mode = File.GetUnixFileMode(path);
        File.SetUnixFileMode(path, UnixFileMode.None);
        return new Lifted(() => Restore(path, mode));
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void Restore(string path, UnixFileMode mode) => File.SetUnixFileMode(path, mode);

    private sealed class Lifted(Action lift) : IDisposable
    {
        public void Dispose() => lift();
    }
}
