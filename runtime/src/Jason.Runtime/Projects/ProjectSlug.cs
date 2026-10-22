using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Jason.Runtime.Domain;
using Jason.Runtime.Persistence;

namespace Jason.Runtime.Projects;

/// <summary>
/// A project's slug: made once, from its name or as given, unique, and never changed. It names the project's folder and is
/// a handle a person types, so it is plain — lower-case ASCII letters and digits in runs joined by single hyphens, two to
/// forty characters, which keeps a folder path well inside what Windows allows — and never a name Windows keeps for a
/// device, which no folder can have there.
/// </summary>
public static partial class ProjectSlug
{
    public const int MaxLength = 40;

    public const int MinLength = 2;

    public const string Field = "slug";

    private static readonly HashSet<string> DeviceNames = new(
        ["con", "prn", "aux", "nul", .. Numbered("com"), .. Numbered("lpt")],
        StringComparer.Ordinal);

    /// <summary>
    /// The slug a name makes: folded the way a search folds it — lower case, accents gone, the few letters that carry no
    /// accent spelled as an English keyboard spells them — with every run of anything but an ASCII letter or digit made one
    /// hyphen, and cut at forty characters. A name that leaves fewer than two characters, one in another script for
    /// instance, is named by the first six hexadecimal characters of the SHA-256 of its fold, each run of white space made one space, instead, so the same name always
    /// makes the same slug and a create sent again finds the project the first one made; a device name has
    /// <c>-project</c> added.
    /// </summary>
    public static string From(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var folded = TextFold.Text(name.Trim());
        var spaced = Whitespace().Replace(folded, " ");
        var slug = Separators().Replace(folded, "-").Trim('-');
        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].TrimEnd('-');
        }

        if (slug.Length < MinLength)
        {
            return "project-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(spaced)))[..6];
        }

        return DeviceNames.Contains(slug) ? slug + "-project" : slug;
    }

    /// <summary>A slug given by the caller, taken exactly as given when it has a slug's shape; otherwise refused by the field.</summary>
    public static string? Read(string slug, ValidationErrors errors)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(errors);

        if (slug.Length > MaxLength)
        {
            errors.Add(Field, "too_long", string.Create(CultureInfo.InvariantCulture, $"slug must be at most {MaxLength} characters."));
            return null;
        }

        if (!Shape().IsMatch(slug))
        {
            errors.Add(Field, "invalid", "slug is lower-case ASCII letters and digits, in runs joined by single hyphens.");
            return null;
        }

        if (slug.Length < MinLength)
        {
            errors.Add(Field, "too_short", string.Create(CultureInfo.InvariantCulture, $"slug must be at least {MinLength} characters."));
            return null;
        }

        if (DeviceNames.Contains(slug))
        {
            errors.Add(Field, "reserved", "slug names a device on Windows, which no folder can be called.");
            return null;
        }

        return slug;
    }

    private static IEnumerable<string> Numbered(string device) => Enumerable.Range(1, 9).Select(n => device + n.ToString(CultureInfo.InvariantCulture));

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^[a-z0-9]+(?:-[a-z0-9]+)*\z")]
    private static partial Regex Shape();
}
