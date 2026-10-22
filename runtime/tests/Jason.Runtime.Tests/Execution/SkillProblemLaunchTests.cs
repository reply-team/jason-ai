using Jason.Contracts.Skills;
using Jason.Runtime.Execution;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Tests.Hosting;

namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// What a launch does with each skill problem, in the words the release page prints beside each code. One table read by
/// two tests: this one stages each problem the shared rules find and runs it through the launcher, and the page's test
/// holds the page's column to the same table, so the page cannot say a launch refuses what it runs or runs what it
/// refuses.
/// </summary>
internal static class SkillProblemLaunches
{
    public const string RefusedInvalid = "refused: `role_skill_invalid`";

    public const string RefusedUnreadable = "refused after reading it again: `role_skill_unreadable`";

    public const string Fails = "fails: `executor_launch_failed`";

    public const string TeachesNothing = "no; it teaches nothing";

    public const string NotAsked = "not asked: a launch reads its own role's directory";

    public static IReadOnlyDictionary<string, string> ByCode { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SkillProblemCodes.SkillsDirectoryUnreadable] = NotAsked,
        [SkillProblemCodes.RoleDirectoryUnreadable] = Fails,
        [SkillProblemCodes.RoleDirectoryTooDeep] = Fails,
        [SkillProblemCodes.RoleDirectoryChanged] = RefusedUnreadable,
        [SkillProblemCodes.SkillFileMissing] = TeachesNothing,
        [SkillProblemCodes.SkillFileIsLink] = RefusedInvalid,
        [SkillProblemCodes.SkillFileUnreadable] = RefusedInvalid,
        [SkillProblemCodes.SkillNameMissing] = RefusedInvalid,
        [SkillProblemCodes.SkillNameMismatch] = RefusedInvalid,
        [SkillProblemCodes.SkillTooLarge] = RefusedInvalid,
    };
}

public sealed class SkillProblemLaunchTests : IDisposable
{
    private const string Role = "researcher";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-tests", Guid.NewGuid().ToString("N"));

    private string WorkDir => Path.Combine(_root, "work");

    private string SkillsRoot => Path.Combine(_root, "skills", "roles");

    private string RoleDirectory => Path.Combine(SkillsRoot, Role);

    [Fact]
    public void Every_code_has_a_launch_said_beside_it() =>
        Assert.Equal(SkillProblemCodes.All.Order(StringComparer.Ordinal), SkillProblemLaunches.ByCode.Keys.Order(StringComparer.Ordinal));

    /// <summary>
    /// Every problem the shared rules find refuses a launch with <c>role_skill_invalid</c>, in the rules' own sentence, and
    /// the page says so for each of them.
    /// </summary>
    [Theory]
    // Critical: a bad skill refuses the launch.
    [InlineData(SkillProblemCodes.SkillNameMissing, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.R3)])]
    [InlineData(SkillProblemCodes.SkillNameMismatch)]
    [InlineData(SkillProblemCodes.SkillTooLarge)]
    [InlineData(SkillProblemCodes.SkillFileUnreadable)]
    [InlineData(SkillProblemCodes.SkillFileIsLink)]
    public void A_problem_the_rules_find_refuses_the_launch_as_the_page_says(string code)
    {
        using var denial = Stage(code);
        var expected = RoleSkillRules.Read(RoleDirectory, Role, 4096).Problem;
        Assert.True(expected?.Code == code, $"The staging for {code} read as {expected?.Code ?? "no problem"}.");

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 4096);

        Assert.Equal(SkillProblemLaunches.RefusedInvalid, SkillProblemLaunches.ByCode[code]);
        Assert.Equal(AttemptErrors.RoleSkillInvalid, report.RefusalCode);
        Assert.Equal(expected!.Message, report.Message);
    }

    /// <summary>A directory with no SKILL.md is copied and the launch goes ahead: it teaches nothing, and is not refused.</summary>
    [Fact]
    public void A_role_directory_with_no_skill_file_is_not_refused_and_teaches_nothing()
    {
        Directory.CreateDirectory(RoleDirectory);
        File.WriteAllText(Path.Combine(RoleDirectory, "references.md"), "where to look");

        var report = WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 4096);

        Assert.Equal(SkillProblemLaunches.TeachesNothing, SkillProblemLaunches.ByCode[SkillProblemCodes.SkillFileMissing]);
        Assert.Null(report.RefusalCode);
    }

    /// <summary>
    /// A tree too deep, or one the operating system refused part of, is not a refusal the launcher words: preparing the
    /// launch throws, and the attempt ends as any launch that could not be prepared does, <c>executor_launch_failed</c>.
    /// </summary>
    [Theory]
    [InlineData(SkillProblemCodes.RoleDirectoryTooDeep)]
    [InlineData(SkillProblemCodes.RoleDirectoryUnreadable)]
    public void A_role_directory_the_launcher_cannot_read_through_fails_the_launch_as_the_page_says(string code)
    {
        using var denial = Stage(code);

        var thrown = Assert.ThrowsAny<Exception>(() => WorkDirectory.Prepare(WorkDir, [], Role, SkillsRoot, 4096));

        Assert.Equal(SkillProblemLaunches.Fails, SkillProblemLaunches.ByCode[code]);
        Assert.Equal(code, SkillProblemCodes.OfUnreadable(thrown));
    }

    private IDisposable? Stage(string code)
    {
        Directory.CreateDirectory(RoleDirectory);
        var skill = Path.Combine(RoleDirectory, RoleSkillRules.SkillFile);
        switch (code)
        {
            case SkillProblemCodes.SkillNameMissing:
                File.WriteAllText(skill, "# The researcher\n\nthe job\n");
                return null;
            case SkillProblemCodes.SkillNameMismatch:
                File.WriteAllText(skill, "---\nname: planner\n---\n");
                return null;
            case SkillProblemCodes.SkillTooLarge:
                File.WriteAllText(skill, $"---\nname: {Role}\n---\n\n{new string('x', 5_000)}\n");
                return null;
            case SkillProblemCodes.SkillFileUnreadable:
                Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "The superuser reads any file, so nothing here can be denied.");
                File.WriteAllText(skill, $"---\nname: {Role}\n---\n");
                return Denial.Of(skill);
            case SkillProblemCodes.SkillFileIsLink:
                var target = Path.Combine(_root, "elsewhere.md");
                File.WriteAllText(target, $"---\nname: {Role}\n---\n");
                try
                {
                    File.CreateSymbolicLink(skill, target);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                {
                    Assert.Skip("A file symbolic link needs a privilege this account does not hold.");
                }

                return null;
            case SkillProblemCodes.RoleDirectoryTooDeep:
                File.WriteAllText(skill, $"---\nname: {Role}\n---\n");
                var deep = RoleDirectory;
                for (var level = 0; level <= RoleSkillRules.MaxDepth + 2; level++)
                {
                    deep = Path.Combine(deep, $"level-{level}");
                }

                Directory.CreateDirectory(deep);
                return null;
            case SkillProblemCodes.RoleDirectoryUnreadable:
                Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "The superuser reads anything, so nothing here can be denied.");
                File.WriteAllText(skill, $"---\nname: {Role}\n---\n");
                var notes = Directory.CreateDirectory(Path.Combine(RoleDirectory, "notes")).FullName;
                File.WriteAllText(Path.Combine(notes, "kept.md"), "kept");
                return Denial.Of(notes);
            default:
                throw new ArgumentOutOfRangeException(nameof(code), code, "Not a staged problem.");
        }
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A denial a test could not lift leaves its directory behind; the temporary root is the machine's to clear.
        }
    }
}
