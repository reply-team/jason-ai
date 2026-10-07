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
        Assert.Equal(SkillProblemCodes.SkillTooLarge, problem.Code);
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
    /// One walk, and the file list is the one the total was measured from. Two walks can see two trees — the
    /// skills installer itself renames these directories — and a total measured from a different walk than the
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

    // ---- why a skill names nothing, each reason in its own words with its own repair --------------------------------

    private const int Cap = 1024 * 1024;

    /// <summary>
    /// A skill file that is a link is not copied, so a host would find nothing there. The repair is to replace the link
    /// with the file; telling the operator to fix front matter that is already right was the mistake to avoid.
    /// </summary>
    [Fact]
    public void A_skill_file_that_is_a_link_says_to_replace_the_link()
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "researcher")).FullName;
        var target = Path.Combine(_root, "elsewhere.md");
        File.WriteAllText(target, "---\nname: researcher\n---\n");
        try
        {
            File.CreateSymbolicLink(Path.Combine(directory, RoleSkillRules.SkillFile), target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            Assert.Skip("A file symbolic link needs a privilege this account does not hold.");
        }

        var problem = RoleSkillRules.Read(directory, "researcher", Cap).Problem;

        Assert.NotNull(problem);
        Assert.Equal((SkillProblemCodes.SkillFileIsLink, (string?)null), (problem.Code, problem.Exception));
        Assert.Contains("is a link", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Replace the link with the file itself", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same answer where the system says the skill file is a link, given through the rules' one question about links
    /// so that it is asked on every machine, including one whose account may not make a file link.
    /// </summary>
    [Fact]
    public void A_skill_file_the_system_calls_a_link_says_to_replace_the_link_on_every_machine()
    {
        var directory = Role("researcher", "researcher");

        var problem = RoleSkillRules.Read(directory, "researcher", Cap, _ => Path.Combine(_root, "elsewhere.md")).Problem;

        Assert.NotNull(problem);
        Assert.Equal((SkillProblemCodes.SkillFileIsLink, (string?)null), (problem.Code, problem.Exception));
        Assert.Contains("Replace the link with the file itself", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The operating system refusing to say whether the skill file is a link is not a link: it is a file this account
    /// cannot read, and the repair is the account's access.
    /// </summary>
    [Fact]
    public void A_skill_file_the_system_will_not_describe_says_it_could_not_be_read()
    {
        var directory = Role("researcher", "researcher");

        var problem = RoleSkillRules.Read(directory, "researcher", Cap, _ => throw new UnauthorizedAccessException("denied")).Problem;

        Assert.NotNull(problem);
        Assert.Equal((SkillProblemCodes.SkillFileUnreadable, (string?)"UnauthorizedAccessException"), (problem.Code, problem.Exception));
        Assert.Contains("could not be read", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Let the account the runtime runs as read it", problem.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("denied", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A skill file found gone by the link question is the tree moving, not a file that cannot be read: a deployment
    /// renamed it, and the answer is to read again. Asked through the rules' one question about links, because on some
    /// systems that question answers "no link" for a path that is not there instead of saying it is gone.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_skill_file_the_link_question_finds_gone_is_the_tree_moving(bool directoryGone)
    {
        var directory = Role("researcher", "researcher");
        Exception Gone(string path) => directoryGone ? new DirectoryNotFoundException(path) : new FileNotFoundException(path);

        Assert.Throws<RoleSkillTreeChanged>(() => RoleSkillRules.Read(directory, "researcher", Cap, path => throw Gone(path)));
    }

    /// <summary>A skill file this account may not open says so, and says what to change.</summary>
    [Fact]
    public void A_skill_file_that_cannot_be_opened_says_it_could_not_be_read()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "The superuser reads any file, so nothing here can be denied.");
        var directory = Role("researcher", "researcher");
        var file = Path.Combine(directory, RoleSkillRules.SkillFile);
        using var denial = Deny(file);

        var problem = RoleSkillRules.Read(directory, "researcher", Cap).Problem;

        Assert.NotNull(problem);
        Assert.Equal((SkillProblemCodes.SkillFileUnreadable, (string?)"UnauthorizedAccessException"), (problem.Code, problem.Exception));
        Assert.Contains("could not be read", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Let the account the runtime runs as read it", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>No front matter, no name in it, or an empty name: the repair is to write the name.</summary>
    [Theory]
    [InlineData("no front matter at all\n")]
    [InlineData("---\ndescription: one line\n---\n")]
    [InlineData("---\nname: \n---\n")]
    public void A_skill_that_gives_itself_no_name_says_to_write_it(string text)
    {
        var directory = Directory.CreateDirectory(Path.Combine(_root, "researcher")).FullName;
        File.WriteAllText(Path.Combine(directory, RoleSkillRules.SkillFile), text);

        var problem = RoleSkillRules.Read(directory, "researcher", Cap).Problem;

        Assert.NotNull(problem);
        Assert.Equal((SkillProblemCodes.SkillNameMissing, (string?)null), (problem.Code, problem.Exception));
        Assert.Contains("gives itself no name", problem.Message, StringComparison.Ordinal);
        Assert.Contains("Write `name: researcher` in its front matter", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>A skill that names itself something else says what to do as well as what is wrong.</summary>
    [Fact]
    public void A_skill_that_names_itself_something_else_says_to_rename_it()
    {
        var problem = RoleSkillRules.Read(Role("researcher", "researcher-v2"), "researcher", Cap).Problem;

        Assert.NotNull(problem);
        Assert.Equal((SkillProblemCodes.SkillNameMismatch, (string?)null), (problem.Code, problem.Exception));
        Assert.Contains("Name the skill 'researcher', or deploy it as the role it names", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>A denial of reading for this account, lifted when disposed so the directory can be cleaned up.</summary>
    private static IDisposable Deny(string file) => OperatingSystem.IsWindows() ? DenyOnWindows(file) : DenyByMode(file);

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static IDisposable DenyOnWindows(string file)
    {
        var info = new FileInfo(file);
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            System.Security.AccessControl.FileSystemRights.ReadData,
            System.Security.AccessControl.AccessControlType.Deny);
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        return new Lifted(() => LiftOnWindows(info, rule));
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void LiftOnWindows(FileInfo info, System.Security.AccessControl.FileSystemAccessRule rule)
    {
        var restored = info.GetAccessControl();
        restored.RemoveAccessRule(rule);
        info.SetAccessControl(restored);
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static IDisposable DenyByMode(string file)
    {
        var mode = File.GetUnixFileMode(file);
        File.SetUnixFileMode(file, UnixFileMode.None);
        return new Lifted(() => LiftByMode(file, mode));
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void LiftByMode(string file, UnixFileMode mode) => File.SetUnixFileMode(file, mode);

    private sealed class Lifted(Action lift) : IDisposable
    {
        public void Dispose() => lift();
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
