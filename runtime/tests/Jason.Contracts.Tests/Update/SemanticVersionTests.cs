using Jason.Contracts;
using Jason.Contracts.Update;

namespace Jason.Contracts.Tests.Update;

/// <summary>
/// Whether one version is newer than another, which is the whole question an update advertisement answers. The
/// order below is SemVer 2.0's, and the cases are the ones this product will really meet: a development build
/// against the first release, a release candidate against its release, and a patch against a minor.
/// </summary>
public class SemanticVersionTests
{
    [Theory]
    [InlineData("0.1.0-dev", "0.1.0")]
    [InlineData("0.1.0", "0.1.1")]
    [InlineData("0.1.9", "0.2.0")]
    [InlineData("0.9.0", "1.0.0-rc.1")]
    [InlineData("1.0.0-rc.1", "1.0.0-rc.2")]
    [InlineData("1.0.0-rc.2", "1.0.0-rc.10")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-rc.2", "1.0.0")]
    public void Older_comes_before_newer(string older, string newer)
    {
        var first = SemanticVersion.Parse(older);
        var second = SemanticVersion.Parse(newer);

        Assert.True(first < second, $"{older} should be older than {newer}");
        Assert.True(second > first, $"{newer} should be newer than {older}");
        Assert.NotEqual(first, second);
    }

    /// <summary>
    /// Build metadata is the commit a build came from. Two builds of one version are one version: comparing it
    /// would make an update available because somebody rebuilt the same release.
    /// </summary>
    [Fact]
    public void Build_metadata_is_ignored()
    {
        Assert.Equal(SemanticVersion.Parse("0.1.0-dev"), SemanticVersion.Parse("0.1.0-dev+1a2b3c4"));
        Assert.Equal(SemanticVersion.Parse("1.2.3"), SemanticVersion.Parse("1.2.3+20260919.7"));
        Assert.False(SemanticVersion.Parse("1.2.3+a") > SemanticVersion.Parse("1.2.3+b"));
    }

    /// <summary>The version this very build reports has to be one the comparison can read, or nothing else here matters.</summary>
    [Fact]
    public void This_builds_own_version_parses()
    {
        Assert.True(SemanticVersion.TryParse(JasonVersion.Current, out var current));
        Assert.Equal(0, current.Major);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    [InlineData("1.2.-3")]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-rc..1")]
    [InlineData("1.2.3-rc.01")]
    [InlineData("1.2.3-rc 1")]
    [InlineData("one.two.three")]
    [InlineData("99999999999999999999.0.0")]
    public void Nonsense_does_not_parse(string? text)
    {
        Assert.False(SemanticVersion.TryParse(text, out _));
        Assert.Throws<FormatException>(() => SemanticVersion.Parse(text!));
    }

    [Fact]
    public void A_version_prints_what_it_was_given_without_its_build_metadata()
    {
        Assert.Equal("1.2.3", SemanticVersion.Parse("1.2.3+deadbeef").ToString());
        Assert.Equal("1.2.3-rc.1", SemanticVersion.Parse("1.2.3-rc.1").ToString());
    }

    [Fact]
    public void Equal_versions_are_equal_in_both_directions()
    {
        var left = SemanticVersion.Parse("2.0.0-rc.1");
        var right = SemanticVersion.Parse("2.0.0-rc.1");

        Assert.Equal(left, right);
        Assert.False(left < right);
        Assert.False(left > right);
        Assert.Equal(0, left.CompareTo(right));
    }
    /// <summary>
    /// A numeric identifier orders by its value however long it is, and a long one is still numeric — so it
    /// still sorts before any identifier that is not.
    /// </summary>
    /// <remarks>
    /// Identifiers wider than nine digits used to be read as text, which happens to give the right answer
    /// against a short number and the wrong one against another long one: compared as text, 999999999999 sorts
    /// after 1234567890123 because '9' comes after '1'. Build stamps and timestamps are exactly how a
    /// thirteen-digit identifier turns up in a real feed.
    /// </remarks>
    [Fact]
    public void A_numeric_pre_release_identifier_too_long_for_an_int_still_orders()
    {
        Assert.True(Version("1.0.0-999999999999") < Version("1.0.0-1234567890123"));
        Assert.True(Version("1.0.0-1234567890123") < Version("1.0.0-1234567890124"));
        Assert.True(Version("1.0.0-2") < Version("1.0.0-1234567890123"));

        // And still a number, so it is older than anything that is not one.
        Assert.True(Version("1.0.0-1234567890123") < Version("1.0.0-alpha"));

        // And the leading-zero rule holds at any width: an all-digit identifier that starts with 0 is not a
        // valid numeric identifier, so the version carrying it is not a version at all.
        Assert.False(SemanticVersion.TryParse("1.0.0-0123456789012", out _));
    }

    private static SemanticVersion Version(string text)
    {
        Assert.True(SemanticVersion.TryParse(text, out var version), $"'{text}' did not parse");
        return version;
    }
}