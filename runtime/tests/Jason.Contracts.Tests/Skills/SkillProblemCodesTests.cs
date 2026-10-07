using Jason.Contracts.Skills;

namespace Jason.Contracts.Tests.Skills;

/// <summary>
/// The words a skill problem is named by. They are what a diagnostic bundle carries where it withholds the sentence, so
/// they are a closed set, and an exception is named by the closed word it is an instance of, never by its own type name.
/// </summary>
public class SkillProblemCodesTests
{
    [Fact]
    public void The_codes_are_ten_distinct_words()
    {
        Assert.Equal(10, SkillProblemCodes.All.Count);
        Assert.Equal(SkillProblemCodes.All.Count, SkillProblemCodes.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(SkillProblemCodes.All, code => Assert.Matches("^[a-z]+(_[a-z]+)*$", code));
    }

    public static TheoryData<Exception, string> Refusals => new()
    {
        { new UnauthorizedAccessException("x"), "UnauthorizedAccessException" },
        { new IOException("x"), "IOException" },
        { new PathTooLongException("x"), "IOException" },
        { new DirectoryNotFoundException("x"), "IOException" },
        { new ArgumentException("x"), "ArgumentException" },
        { new ArgumentNullException("x"), "ArgumentException" },
        { new InvalidOperationException("x"), "other" },
        { new NotSupportedException("x"), "other" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void An_exception_is_named_by_the_closed_word_it_is_an_instance_of(Exception exception, string word)
    {
        Assert.Equal(word, SkillProblemCodes.ExceptionWord(exception));
        Assert.Contains(word, SkillProblemCodes.Exceptions);
    }

    public static TheoryData<Exception, string> Unreadable => new()
    {
        { new RoleSkillTooDeep("/skills/researcher", RoleSkillRules.MaxDepth), SkillProblemCodes.RoleDirectoryTooDeep },
        { new RoleSkillTreeChanged("/skills/researcher", new DirectoryNotFoundException("x")), SkillProblemCodes.RoleDirectoryChanged },
        { new UnauthorizedAccessException("x"), SkillProblemCodes.RoleDirectoryUnreadable },
        { new IOException("x"), SkillProblemCodes.RoleDirectoryUnreadable },
    };

    [Theory]
    [MemberData(nameof(Unreadable))]
    public void A_role_directory_that_could_not_be_read_through_is_named_by_why(Exception exception, string code) =>
        Assert.Equal(code, SkillProblemCodes.OfUnreadable(exception));
}
