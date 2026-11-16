using Jason.Contracts.Discovery;

namespace Jason.Contracts.Tests.Discovery;

/// <summary>
/// Whether a folder can be one entry of PATH: the entries are separated by one character, with no escape, so a folder
/// whose name holds it is two entries to every reader. The one predicate the status check, <c>session open</c> and the
/// runtime's launch read, worded for each of them to follow.
/// </summary>
public sealed class SearchPathEntryTests
{
    [Theory]
    [InlineData(true, ';')]
    [InlineData(false, ':')]
    public void Each_system_separates_the_entries_by_its_own_character(bool windows, char separator)
    {
        Assert.Equal(separator, SearchPathEntry.Separator(windows));
    }

    [Fact]
    public void A_folder_holding_the_separator_is_named_with_the_character_and_why()
    {
        Assert.Equal(
            @"C:\Tools\a;b holds ';', which separates the entries of PATH, so no PATH entry can name it",
            SearchPathEntry.Problem(@"C:\Tools\a;b", ';'));
        Assert.Equal(
            "/opt/a:b holds ':', which separates the entries of PATH, so no PATH entry can name it",
            SearchPathEntry.Problem("/opt/a:b", ':'));
    }

    /// <summary>Only the separator: every other character is the folder's own, the other system's separator included.</summary>
    [Theory]
    [InlineData(@"C:\Users\marta\AppData\Local\Programs\jason", ';')]
    [InlineData(@"C:\Tools\a:b", ';')]
    [InlineData(@"C:\Tools\100%\$x `y", ';')]
    [InlineData("/opt/a;b", ':')]
    [InlineData("/opt/a$b/\"c\"/d\\e", ':')]
    public void Any_other_folder_is_one_entry(string folder, char separator)
    {
        Assert.Null(SearchPathEntry.Problem(folder, separator));
    }

    [Fact]
    public void Without_a_separator_named_it_is_this_systems()
    {
        Assert.NotNull(SearchPathEntry.Problem("a" + Path.PathSeparator + "b"));
        Assert.Null(SearchPathEntry.Problem("ab"));
    }
}
