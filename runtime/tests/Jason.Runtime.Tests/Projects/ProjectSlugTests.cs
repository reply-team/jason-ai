using Jason.Runtime.Domain;
using Jason.Runtime.Projects;

namespace Jason.Runtime.Tests.Projects;

/// <summary>
/// A project's slug names its folder and is a handle a person types, so it is plain: lower-case ASCII letters and digits
/// in runs joined by single hyphens, at least two characters and at most forty, and never a name Windows keeps for a device.
/// </summary>
public class ProjectSlugTests
{
    [Theory]
    [InlineData("Acme", "acme")]
    [InlineData("Acme Corp", "acme-corp")]
    [InlineData("  --Q3 // Marketing   leads--  ", "q3-marketing-leads")]
    [InlineData("Álvaro's Café", "alvaro-s-cafe")]
    [InlineData("Łódź Straße", "lodz-strasse")]
    [InlineData("ClickUp 2.0", "clickup-2-0")]
    [InlineData("Проект Q3", "q3")]
    [InlineData("CON", "con-project")]
    [InlineData("com1", "com1-project")]
    [InlineData("Lpt9", "lpt9-project")]
    [InlineData("console", "console")]
    public void A_name_becomes_its_slug(string name, string slug) => Assert.Equal(slug, ProjectSlug.From(name));

    /// <summary>
    /// A name that leaves fewer than two letters or digits — in another script, say — is named by the first six hexadecimal
    /// characters of the SHA-256 of its fold, so the same name, whatever its case or accents, always makes the same slug.
    /// </summary>
    [Theory]
    [InlineData("Проект", "project-dfb992")]
    [InlineData("東京", "project-130016")]
    [InlineData("A", "project-ca9781")]
    [InlineData("--", "project-d8156b")]
    [InlineData("C++", "project-cedb1b")]
    [InlineData("  c++ ", "project-cedb1b")]
    [InlineData("Новый продукт", "project-f62890")]
    [InlineData("Новый  продукт ", "project-f62890")]
    [InlineData(" Новый	 продукт", "project-f62890")]
    public void A_name_with_too_little_to_keep_is_named_by_the_hash_of_its_fold(string name, string slug) => Assert.Equal(slug, ProjectSlug.From(name));

    /// <summary>At most forty characters, never ending on a hyphen.</summary>
    [Fact]
    public void A_long_name_is_cut_to_forty_characters_without_a_trailing_hyphen()
    {
        var slug = ProjectSlug.From("Quarterly outbound to the mid-market accounts of the northern region");

        Assert.Equal("quarterly-outbound-to-the-mid-market-acc", slug);
        Assert.Equal(ProjectSlug.MaxLength, slug.Length);
        Assert.Equal("one-two-three-four-five-six-seven-eight", ProjectSlug.From("one two three four five six seven eight nine"));
    }

    [Theory]
    [InlineData("acme")]
    [InlineData("acme-eu")]
    [InlineData("q3-2026")]
    [InlineData("a1")]
    [InlineData("con-project")]
    public void A_slug_in_its_own_shape_is_taken_as_given(string slug)
    {
        var errors = new ValidationErrors();

        Assert.Equal(slug, ProjectSlug.Read(slug, errors));
        Assert.False(errors.Any, $"'{slug}' was refused.");
    }

    [Theory]
    [InlineData("Acme", "invalid")]
    [InlineData("acme corp", "invalid")]
    [InlineData("-acme", "invalid")]
    [InlineData("acme-", "invalid")]
    [InlineData("acme--eu", "invalid")]
    [InlineData("café", "invalid")]
    [InlineData("a", "too_short")]
    [InlineData("abcdefghij-abcdefghij-abcdefghij-abcdefgh", "too_long")]
    [InlineData("nul", "reserved")]
    [InlineData("com3", "reserved")]
    public void A_slug_out_of_its_shape_is_refused_by_the_field(string slug, string code)
    {
        var errors = new ValidationErrors();

        ProjectSlug.Read(slug, errors);

        var refused = Assert.Throws<ValidationException>(errors.ThrowIfAny);
        var detail = Assert.Single(refused.Details!);
        Assert.True(detail is { Field: "slug" } && detail.Code == code, $"'{slug}' was refused as {detail.Field}/{detail.Code}, not slug/{code}.");
    }
}
