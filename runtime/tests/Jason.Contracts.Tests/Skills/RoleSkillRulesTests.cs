using Jason.Contracts.Skills;

namespace Jason.Contracts.Tests.Skills;

/// <summary>
/// The two rules a role's skill must satisfy before a launch can be taught it, read here rather than in the
/// launcher that enforces them.
/// </summary>
/// <remarks>
/// They live in the contracts because two programs need them and only one of them may see the other. The
/// launcher refuses an attempt on them; an installer has to apply them <em>before</em> it writes, because a
/// deployment that fails them turns a role with no skill — which runs — into a role whose every launch is
/// refused. The CLI may never reference the runtime, so a shared rule is the only way both can apply the same
/// one rather than two that agree until somebody edits one.
/// </remarks>
public class RoleSkillRulesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));

    public RoleSkillRulesTests() => Directory.CreateDirectory(_root);

    private string Role(string name, string declared, string body = "Ask the runtime for the brief.")
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        File.WriteAllText(
            Path.Combine(directory, RoleSkillRules.SkillFile),
            $"---\nname: {declared}\ndescription: one line\n---\n\n{body}\n");
        return directory;
    }

    [Fact]
    public void A_skill_that_names_itself_after_its_directory_is_deliverable() =>
        Assert.Null(RoleSkillRules.Read(Role("researcher", "researcher"), "researcher", 1024 * 1024).Problem);

    [Fact]
    public void A_skill_that_names_itself_anything_else_is_refused_and_the_message_names_both()
    {
        var problem = RoleSkillRules.Read(Role("researcher", "researcher-v2"), "researcher", 1024 * 1024).Problem;

        Assert.NotNull(problem);
        Assert.Contains("'researcher-v2'", problem.Message, StringComparison.Ordinal);
        Assert.Contains("'researcher'", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tree_over_the_cap_is_refused_and_the_message_names_the_setting()
    {
        var directory = Role("researcher", "researcher", new string('x', 5_000));

        var problem = RoleSkillRules.Read(directory, "researcher", 4096).Problem;

        Assert.NotNull(problem);
        Assert.Contains("Roles:MaxSkillBytes", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>The name is read before the size, because that is the order the launcher applies them in.</summary>
    [Fact]
    public void A_skill_that_is_both_misnamed_and_too_large_is_refused_for_its_name()
    {
        var directory = Role("researcher", "researcher-v2", new string('x', 5_000));

        var problem = RoleSkillRules.Read(directory, "researcher", 4096).Problem;

        Assert.NotNull(problem);
        Assert.Contains("names itself", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>A role that was never given a skill is not a problem; it is a role with no skill.</summary>
    [Fact]
    public void A_directory_that_is_not_there_is_not_a_problem() =>
        Assert.False(RoleSkillRules.Read(Path.Combine(_root, "nobody"), "nobody", 1024 * 1024).Exists);

    /// <summary>
    /// A directory with no <c>SKILL.md</c>. The name rule cannot fire — there is no file to read a name from —
    /// so the launcher delivers it and a host loads nothing from it: a role that runs untaught with a
    /// deployment on disk saying otherwise. That is not a refusal and this does not make it one; it is
    /// reported, so an installer can refuse to create one and a status verb can name one that already exists.
    /// </summary>
    [Fact]
    public void A_directory_with_no_skill_file_is_deliverable_and_teaches_nothing()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "researcher")).FullName;
        File.WriteAllText(Path.Combine(directory, "references.md"), "where to look");

        var reading = RoleSkillRules.Read(directory, "researcher", 1024 * 1024);

        Assert.True(reading.Exists);
        Assert.False(reading.HasSkillFile);
        Assert.Null(reading.Problem);
        Assert.Single(reading.Files);
    }

    /// <summary>
    /// One walk, and the file list is the one the total was measured from. Two walks can see two trees — this
    /// wave's own installer renames these directories — and a total measured from a different walk than the
    /// list is a measurement of something else.
    /// </summary>
    [Fact]
    public void The_total_is_measured_from_the_files_it_reports()
    {
        var directory = Role("researcher", "researcher");
        File.WriteAllText(Path.Combine(directory, "references.md"), new string('x', 100));

        var reading = RoleSkillRules.Read(directory, "researcher", 1024 * 1024);

        Assert.Equal(2, reading.Files.Count);
        Assert.Equal(reading.Files.Sum(file => new FileInfo(file).Length), reading.Bytes);
    }

    /// <summary>The name a skill gives itself, read the way a host reads it and no further into the file.</summary>
    [Theory]
    [InlineData("---\nname: researcher\n---\n", "researcher")]
    [InlineData("---\nname: \"researcher\"\n---\n", "researcher")]
    [InlineData("---\nname: 'researcher'\n---\n", "researcher")]
    [InlineData("---\ndescription: one line\n---\n", null)]
    [InlineData("no front matter at all\n", null)]
    [InlineData("---\n---\nname: researcher\n", null)]
    public void The_name_is_read_from_the_front_matter_and_nowhere_else(string text, string? expected)
    {
        var file = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "x")).FullName, RoleSkillRules.SkillFile);
        File.WriteAllText(file, text);

        Assert.Equal(expected, RoleSkillRules.Named(file));
    }

    /// <summary>
    /// A skill file that is gone is the tree moving, not a skill that names nothing.
    /// </summary>
    /// <remarks>
    /// The difference is the whole of it. Null here reads to the caller as "this skill calls itself something
    /// other than its directory", which refuses the attempt as invalid and sends the operator to fix front
    /// matter that is already correct. And <c>SKILL.md</c> is the one file every deployment touches, so this
    /// is where a rename is most likely to be met — the raise was applied everywhere except where it mattered
    /// most.
    /// </remarks>
    [Fact]
    public void A_skill_file_that_is_gone_is_the_tree_moving_rather_than_a_skill_that_names_nothing()
    {
        var file = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "gone")).FullName, RoleSkillRules.SkillFile);

        var changed = Assert.Throws<RoleSkillTreeChanged>(() => RoleSkillRules.Named(file));

        Assert.Contains(RoleSkillRules.SkillFile, changed.Message, StringComparison.Ordinal);
    }

    /// <summary>And a directory that is gone under it answers the same way, for the same reason.</summary>
    [Fact]
    public void A_skill_file_whose_directory_is_gone_is_the_tree_moving_too()
    {
        var directory = Path.Combine(_root, "never-made");

        Assert.Throws<RoleSkillTreeChanged>(() => RoleSkillRules.Named(Path.Combine(directory, RoleSkillRules.SkillFile)));
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
}
