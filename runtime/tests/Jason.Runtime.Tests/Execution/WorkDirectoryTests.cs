using System.Text.Json.Nodes;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// What an agent finds in the directory it is started in: what it may not do, and how to do the job. Both halves
/// are written before the child exists, because a directory the runtime creates is not a workspace the host
/// trusts and a host that distrusts it says nothing about what it therefore ignored.
/// </summary>
public class WorkDirectoryTests : IDisposable
{
    private const string Role = "researcher";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));

    public WorkDirectoryTests() => Directory.CreateDirectory(_root);

    private string WorkDir => Path.Combine(_root, "work");

    private string SkillsRoot => Path.Combine(_root, "skills", "roles");

    [Fact]
    public void The_work_directory_carries_the_deny_rules_and_never_an_allow_list()
    {
        var report = WorkDirectory.Prepare(WorkDir, ["Bash(rm *)", "WebFetch"], role: null, SkillsRoot, 1024);

        Assert.Null(report.RefusalCode);
        var file = Path.Combine(WorkDir, ".claude", "settings.json");
        var text = File.ReadAllText(file);
        var settings = JsonNode.Parse(text)!;

        Assert.Equal(["Bash(rm *)", "WebFetch"], settings["permissions"]!["deny"]!.AsArray().Select(rule => (string?)rule));

        // A runtime-created directory is not a trusted workspace, so an allow entry written here is ignored
        // without being reported: what the agent may do travels on the command line instead. Writing one anyway
        // would read like permission granted and be nothing of the kind.
        Assert.DoesNotContain("allow", text, StringComparison.OrdinalIgnoreCase);

        // Accepting a trust dialogue on the user's behalf is not the runtime's to do.
        Assert.DoesNotContain("hasTrustDialogAccepted", text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_profile_with_nothing_to_deny_still_says_so()
    {
        WorkDirectory.Prepare(WorkDir, [], role: null, SkillsRoot, 1024);

        var settings = JsonNode.Parse(File.ReadAllText(Path.Combine(WorkDir, ".claude", "settings.json")))!;
        Assert.Empty(settings["permissions"]!["deny"]!.AsArray());
    }

    [Fact]
    public void A_role_is_taught_its_job_under_the_name_the_host_looks_it_up_by()
    {
        Skill($"---\nname: {Role}\ndescription: finds the decision maker\n---\n\nAsk the runtime for the brief.\n");
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(SkillsRoot, Role, "references")).FullName, "sources.md"), "where to look");

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 1024 * 1024);

        Assert.Null(report.RefusalCode);
        Assert.Null(report.Message);
        var copied = Path.Combine(WorkDir, ".claude", "skills", Role);
        Assert.Contains("Ask the runtime for the brief.", File.ReadAllText(Path.Combine(copied, "SKILL.md")), StringComparison.Ordinal);
        Assert.Equal("where to look", File.ReadAllText(Path.Combine(copied, "references", "sources.md")));
    }

    [Fact]
    public void A_role_with_no_skill_of_its_own_is_ready_anyway()
    {
        // The brief travels in the envelope; a role without a skill is a role that was never given one, not a
        // failure. Nothing is created for it.
        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 1024);

        Assert.Null(report.RefusalCode);
        Assert.False(Directory.Exists(Path.Combine(WorkDir, ".claude", "skills")));
        Assert.True(File.Exists(Path.Combine(WorkDir, ".claude", "settings.json")));
    }

    [Fact]
    public void A_skill_that_names_something_other_than_its_role_refuses_the_attempt_and_names_both()
    {
        Skill("---\nname: prospect-researcher\ndescription: finds the decision maker\n---\n\nthe job\n");

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 1024 * 1024);

        Assert.Equal(AttemptErrors.RoleSkillInvalid, report.RefusalCode);
        Assert.Contains("prospect-researcher", report.Message!, StringComparison.Ordinal);
        Assert.Contains(Role, report.Message!, StringComparison.Ordinal);

        // Refused before the child exists, and nothing of the skill was left behind for it to half-read.
        Assert.False(Directory.Exists(Path.Combine(WorkDir, ".claude", "skills")));
    }

    [Fact]
    public void A_skill_whose_frontmatter_names_nothing_is_refused_the_same_way()
    {
        Skill("# The researcher\n\nthe job\n");

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 1024 * 1024);

        Assert.Equal(AttemptErrors.RoleSkillInvalid, report.RefusalCode);
        Assert.Contains(Role, report.Message!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_skill_may_be_quoted_and_may_carry_other_keys()
    {
        Skill($"---\nallowed-tools: Bash\nname: \"{Role}\"\ndescription: finds the decision maker\n---\n\nthe job\n");

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 1024 * 1024);

        Assert.Null(report.RefusalCode);
        Assert.True(File.Exists(Path.Combine(WorkDir, ".claude", "skills", Role, "SKILL.md")));
    }

    /// <summary>
    /// A skill somebody configured and the agent never received is the failure this whole file is careful about:
    /// the role does the job untaught, at the price of a real launch, and the only trace is a log line nobody is
    /// reading at the time. Whether it was left behind because its name did not match or because it was too
    /// large to copy makes no difference to the agent, so it makes none here either. A role with no skill
    /// directory at all is a different thing and still launches: nothing was configured, so nothing is missing.
    /// </summary>
    [Fact]
    public void A_skill_directory_past_the_maximum_refuses_the_attempt_rather_than_running_the_role_untaught()
    {
        Skill($"---\nname: {Role}\ndescription: finds the decision maker\n---\n\n{new string('x', 4096)}\n");

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 1024);

        Assert.Equal("role_skill_invalid", report.RefusalCode);
        Assert.Contains("Roles:MaxSkillBytes", report.Message!, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(WorkDir, ".claude", "skills")));
    }

    [Fact]
    public void A_link_in_a_skill_directory_is_neither_copied_nor_followed()
    {
        Skill($"---\nname: {Role}\ndescription: finds the decision maker\n---\n\nthe job\n");
        var secret = Path.Combine(_root, "secret.txt");
        File.WriteAllText(secret, "not the agent's business");
        try
        {
            File.CreateSymbolicLink(Path.Combine(SkillsRoot, Role, "notes.md"), secret);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("This machine does not let this user create a symbolic link.");
        }

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 1024 * 1024);

        Assert.Null(report.RefusalCode);
        var copied = Path.Combine(WorkDir, ".claude", "skills", Role);
        Assert.True(File.Exists(Path.Combine(copied, "SKILL.md")));
        Assert.False(File.Exists(Path.Combine(copied, "notes.md")));
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Skill(string text) =>
        File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(SkillsRoot, Role)).FullName, "SKILL.md"), text);
}
