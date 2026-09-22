using Jason.Contracts.Discovery;

namespace Jason.Contracts.Tests.Discovery;

/// <summary>
/// The one path in this layout composed out of text a person types, and the one the caller deletes before it
/// unpacks into it.
/// </summary>
/// <remarks>
/// <para>
/// A ref arrives on a command line and became a directory name. The filter that made it one kept dots,
/// because dots are ordinary in a tag — so <c>..</c> passed through untouched, the path resolved to the
/// directory holding every deployed skill, and the caller deleted it. One flag value, irreversible, in the
/// verb whose contract is that nothing is written until the whole tree is known to be deliverable.
/// </para>
/// <para>
/// The refusal lives here rather than upstream because this is where the promise that it cannot walk anywhere
/// has to be true: somewhere else may compose a name, and this is what turns a name into a path.
/// </para>
/// </remarks>
public class StagedSourcePathTests
{
    private static readonly JasonPaths Paths = new(OperatingSystem.IsWindows() ? @"C:\data" : "/data");

    [Theory]
    [InlineData("v0.1.0")]
    [InlineData("main")]
    [InlineData("release-1.0")]
    [InlineData("a_b.c-1")]
    public void An_ordinary_name_composes_a_directory_inside_the_staging_root(string name)
    {
        var staged = Paths.SkillsStagedSourceDirectory(name);

        Assert.StartsWith(Paths.SkillsStagedSourcesDirectory, staged, StringComparison.Ordinal);
        Assert.Equal(name, Path.GetFileName(staged));
    }

    /// <summary>
    /// Every spelling that could climb. <c>..</c> is the whole of it and the one a person might type by
    /// accident; the rest are here because a guard that refused only the exact string somebody reproduced is
    /// a guard against that reproduction.
    /// </summary>
    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData("...")]
    [InlineData("../..")]
    [InlineData("..\\..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("/etc")]
    [InlineData("C:")]
    [InlineData("C:\\windows")]
    public void A_name_that_could_climb_is_refused_rather_than_mangled(string name)
    {
        var refused = Assert.Throws<ArgumentException>(() => Paths.SkillsStagedSourceDirectory(name));

        Assert.Contains(name, refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// And the property behind all of them, said once rather than as a list: whatever this returns is inside
    /// the staging root. A list of spellings goes stale; this does not.
    /// </summary>
    [Fact]
    public void Nothing_this_composes_is_ever_outside_the_staging_root()
    {
        string[] names = ["v1", "..", ".", "a/b", "a\\b", "..\\..\\..", "C:\\windows", "~", "$HOME", "  ", "\t"];
        var inside = Path.GetFullPath(Paths.SkillsStagedSourcesDirectory) + Path.DirectorySeparatorChar;

        foreach (var name in names)
        {
            string composed;
            try
            {
                composed = Paths.SkillsStagedSourceDirectory(name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            Assert.True(
                Path.GetFullPath(composed).StartsWith(inside, StringComparison.OrdinalIgnoreCase),
                $"'{name}' composed '{composed}', which is outside '{Paths.SkillsStagedSourcesDirectory}'. "
                + "The caller deletes what this returns.");
        }
    }
}
