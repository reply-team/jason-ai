using System.Globalization;

namespace Jason.Contracts.Update;

/// <summary>
/// A version this build can compare: <c>major.minor.patch</c> with an optional pre-release, as SemVer 2.0
/// defines the order. Build metadata is parsed and discarded — two builds of one version are one version, and
/// comparing the commit they came from would advertise an update because somebody rebuilt a release.
/// </summary>
/// <remarks>
/// Parsed by hand rather than with a pattern, and without a package. The input arrives from a web page this
/// runtime did not write, so the reader walks it once, allocates nothing it does not need, and cannot be made
/// to backtrack; and the one assembly the CLI, the runtime and the plugin host all share is the last place to
/// take a dependency for thirty lines of arithmetic.
/// </remarks>
public readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<SemanticVersion>
{
    /// <summary>The version of the build this code is running in.</summary>
    /// <remarks>
    /// Zero if this assembly's informational version is unreadable, which cannot happen in a build produced by
    /// this repository — every assembly is stamped — and which is still not worth throwing from a static
    /// constructor for: a runtime that refuses to start because it cannot read its own version number would be
    /// a worse failure than one that thinks it is very old.
    /// </remarks>
    public static SemanticVersion Current { get; } =
        TryParse(JasonVersion.Current, out var current) ? current : new SemanticVersion(0, 0, 0, null);

    public static bool TryParse(string? text, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();

        // Build metadata is everything after the first '+', and it takes no part in the order.
        var plus = span.IndexOf('+');
        if (plus >= 0)
        {
            span = span[..plus];
        }

        string? preRelease = null;
        var dash = span.IndexOf('-');
        if (dash >= 0)
        {
            var candidate = span[(dash + 1)..];
            if (!IsPreRelease(candidate))
            {
                return false;
            }

            preRelease = candidate.ToString();
            span = span[..dash];
        }

        var first = span.IndexOf('.');
        if (first < 0)
        {
            return false;
        }

        var second = span[(first + 1)..].IndexOf('.');
        if (second < 0)
        {
            return false;
        }

        second += first + 1;
        if (!Number(span[..first], out var major)
            || !Number(span[(first + 1)..second], out var minor)
            || !Number(span[(second + 1)..], out var patch))
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public static SemanticVersion Parse(string text) =>
        TryParse(text, out var version)
            ? version
            : throw new FormatException($"'{text}' is not a version of the form major.minor.patch with an optional -prerelease.");

    public int CompareTo(SemanticVersion other)
    {
        var numbers = Major.CompareTo(other.Major);
        if (numbers != 0)
        {
            return numbers;
        }

        numbers = Minor.CompareTo(other.Minor);
        if (numbers != 0)
        {
            return numbers;
        }

        numbers = Patch.CompareTo(other.Patch);
        if (numbers != 0)
        {
            return numbers;
        }

        // A pre-release is always older than the release it leads to: 1.0.0-rc.1 comes before 1.0.0.
        if (PreRelease is null || other.PreRelease is null)
        {
            return PreRelease is null ? (other.PreRelease is null ? 0 : 1) : -1;
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    public override string ToString() =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{Major}.{Minor}.{Patch}{(PreRelease is null ? string.Empty : "-" + PreRelease)}");

    public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Identifier by identifier: numbers compare as numbers, anything else compares as text, a number is older
    /// than text, and a version that runs out of identifiers first is the older one — <c>1.0.0-alpha</c> before
    /// <c>1.0.0-alpha.1</c>.
    /// </summary>
    private static int ComparePreRelease(string left, string right)
    {
        var mine = left.Split('.');
        var theirs = right.Split('.');
        for (var index = 0; index < Math.Max(mine.Length, theirs.Length); index++)
        {
            if (index >= mine.Length)
            {
                return -1;
            }

            if (index >= theirs.Length)
            {
                return 1;
            }

            var mineIsNumber = IsNumber(mine[index]);
            var theirsIsNumber = IsNumber(theirs[index]);
            var order = (mineIsNumber, theirsIsNumber) switch
            {
                // Digits with no leading zero, so the longer string is the larger number and two of the same
                // length compare digit by digit. No parse, so no width at which this stops being true: a build
                // stamp or a millisecond timestamp is an ordinary identifier rather than a special case.
                (true, true) => mine[index].Length != theirs[index].Length
                    ? mine[index].Length.CompareTo(theirs[index].Length)
                    : string.CompareOrdinal(mine[index], theirs[index]),

                // A numeric identifier is always older than one that is not, which is how SemVer keeps
                // 1.0.0-1 before 1.0.0-alpha.
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(mine[index], theirs[index]),
            };

            if (order != 0)
            {
                return order;
            }
        }

        return 0;
    }

    /// <summary>
    /// A version number: digits, no leading zero, and small enough to be one. Major, minor and patch are
    /// numbers this build does arithmetic and comparisons on, so the width limit is what stops a feed from
    /// overflowing them on purpose. A pre-release identifier is a different thing and has its own rule below.
    /// </summary>
    private static bool Number(ReadOnlySpan<char> text, out int value)
    {
        value = 0;
        if (text.Length is 0 or > 9 || !IsNumber(text))
        {
            return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// A numeric identifier: digits, and no leading zero. Any width, because nothing here converts one to a
    /// number — two of these are compared by length and then by text, which is the same order and cannot
    /// overflow, so a feed cannot make this fail by writing a long enough identifier.
    /// </summary>
    private static bool IsNumber(ReadOnlySpan<char> text)
    {
        if (text.Length == 0 || (text.Length > 1 && text[0] == '0'))
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumber(string text) => IsNumber(text.AsSpan());

    /// <summary>
    /// Dot-separated identifiers of ASCII letters, digits and hyphens; none of them empty, and a numeric one
    /// carries no leading zero.
    /// </summary>
    private static bool IsPreRelease(ReadOnlySpan<char> text)
    {
        if (text.IsEmpty)
        {
            return false;
        }

        foreach (var range in text.Split('.'))
        {
            var identifier = text[range];
            if (identifier.IsEmpty)
            {
                return false;
            }

            var digitsOnly = true;
            foreach (var character in identifier)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character != '-')
                {
                    return false;
                }

                digitsOnly &= char.IsAsciiDigit(character);
            }

            if (digitsOnly && identifier.Length > 1 && identifier[0] == '0')
            {
                return false;
            }
        }

        return true;
    }
}
