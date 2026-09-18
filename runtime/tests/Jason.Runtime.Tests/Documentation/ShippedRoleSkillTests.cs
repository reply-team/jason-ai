using Jason.Runtime.Configuration;
using Jason.Runtime.Execution.Hosts;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>
/// The role skills this repository ships, held to the rules the launcher enforces on them. A skill is looked up
/// by the directory it lives in and must name itself the same way; a host answers a mismatch by loading nothing
/// and saying nothing, so a shipped skill that got this wrong would teach nobody and nobody would notice.
/// </summary>
public class ShippedRoleSkillTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_shipped_role_skill_is_named_after_its_directory()
    {
        var packs = Directory.GetDirectories(RoleSkillsPack());
        Assert.NotEmpty(packs);

        foreach (var pack in packs)
        {
            var file = Path.Combine(pack, WorkDirectory.SkillFile);
            Assert.True(File.Exists(file), $"'{pack}' is a role skill directory with no {WorkDirectory.SkillFile} in it.");
            Assert.Equal(Path.GetFileName(pack), FrontMatter(file, "name"));
        }
    }

    /// <summary>
    /// And it really is deliverable: composed into a data directory the way an operator composes one, it is
    /// copied into the work directory rather than refused for its name or its size. The launcher's own code
    /// decides that here, so the pack cannot pass a test and fail a launch.
    /// </summary>
    [Fact]
    public void The_shipped_role_skill_reaches_a_work_directory_rather_than_being_refused()
    {
        using var data = new TempDataDir();
        var composed = Path.Combine(data.Paths.RoleSkillsDirectory, "researcher");
        Directory.CreateDirectory(composed);
        foreach (var file in Directory.GetFiles(Path.Combine(RoleSkillsPack(), "researcher")))
        {
            File.Copy(file, Path.Combine(composed, Path.GetFileName(file)));
        }

        var workDir = data.Paths.AttemptWorkDirectory("wi_A", "att_A");
        var report = WorkDirectory.Prepare(workDir, ["Write"], "researcher", data.Paths.RoleSkillsDirectory, new RolesOptions().MaxSkillBytes);

        Assert.Null(report.RefusalCode);
        Assert.NotNull(report.Skill);
        Assert.True(report.Skill.Copied);
        Assert.Equal("researcher", report.Skill.Name);
        Assert.True(File.Exists(Path.Combine(workDir, ".claude", "skills", "researcher", WorkDirectory.SkillFile)));
    }

    /// <summary>
    /// The two rules a launched role cannot work out for itself and cannot be told twice: the callback is the
    /// plain command word the envelope names, and a note is the role's own memory rather than what is true.
    /// </summary>
    [Fact]
    public async Task The_researchers_skill_teaches_the_plain_callback_and_what_a_note_is_worth()
    {
        var skill = await File.ReadAllTextAsync(Path.Combine(RoleSkillsPack(), "researcher", WorkDirectory.SkillFile), Ct);

        // The callback, in the form the allow rule grants — and the two shapes it does not.
        Assert.Contains("rolenote set", skill, StringComparison.Ordinal);
        Assert.Contains("workitem complete", skill, StringComparison.Ordinal);
        Assert.Contains("redirect", skill, StringComparison.OrdinalIgnoreCase);

        // INV-MEM-001, in the skill the role actually reads rather than only in a document nobody hands it.
        Assert.Contains("not authoritative", skill, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("runtime state wins", skill, StringComparison.OrdinalIgnoreCase);

        // And the answer's shape, which is the other thing a refused completion is usually about.
        Assert.Contains("result_format", skill, StringComparison.Ordinal);
    }

    /// <summary>The name a skill gives itself, read the way the launcher reads it.</summary>
    private static string? FrontMatter(string file, string key)
    {
        var lines = File.ReadLines(file).Take(64).ToList();
        Assert.True(lines.Count > 0 && lines[0].Trim() == "---", $"'{file}' does not open with front matter.");

        foreach (var line in lines.Skip(1))
        {
            if (line.Trim() == "---")
            {
                break;
            }

            if (line.StartsWith(key + ":", StringComparison.Ordinal))
            {
                return line[(key.Length + 1)..].Trim().Trim('"', '\'');
            }
        }

        return null;
    }

    private static string RoleSkillsPack()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "skills", "runtime", "roles");
    }
}
