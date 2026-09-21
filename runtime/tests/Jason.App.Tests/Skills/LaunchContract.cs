namespace Jason.App.Tests.Skills;

/// <summary>
/// The one launch contract, found in a role's skill the way a correction has to find it: between two markers
/// that are invisible to the reader and trivial to locate. HTML comments, because a role reads this file as
/// prose and has no business being shown the seams.
/// </summary>
internal static class LaunchContract
{
    public const string Begin = "<!-- contract:begin -->";

    public const string End = "<!-- contract:end -->";

    /// <summary>The block, line endings normalised, or null when this file carries no block at all.</summary>
    public static string? Extract(string file)
    {
        var text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
        var begin = text.IndexOf(Begin, StringComparison.Ordinal);
        var end = text.IndexOf(End, StringComparison.Ordinal);

        return begin < 0 || end < begin ? null : text[begin..(end + End.Length)];
    }

    /// <summary>The first line that differs, quoted both ways: a diff a person can act on without a tool.</summary>
    public static string FirstDifference(string expected, string actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var left = expected.Split('\n');
        var right = actual.Split('\n');
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return $"line {index + 1} of the block reads '{right[index]}' here and '{left[index]}' there";
            }
        }

        return $"the block is {right.Length} lines here and {left.Length} lines there";
    }
}
