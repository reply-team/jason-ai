namespace Jason.App.Tests.Skills;

/// <summary>
/// A block of a skill held between two markers, found the way a correction has to find it. Two blocks are kept this
/// way — the launch contract every role carries, and the judgement a role shares with its session file — and one
/// reader serves both, so the two guards cannot come to disagree about where a block begins.
/// </summary>
internal static class MarkedBlock
{
    /// <summary>The block, markers included, line endings normalised, or null when the file carries none.</summary>
    public static string? Extract(string file, string begin, string end)
    {
        var text = File.ReadAllText(file).Replace("\r\n", "\n", StringComparison.Ordinal);
        var from = text.IndexOf(begin, StringComparison.Ordinal);
        var to = text.IndexOf(end, StringComparison.Ordinal);

        return from < 0 || to < from ? null : text[from..(to + end.Length)];
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

    /// <summary>How many times a marker occurs in a text.</summary>
    public static int Occurrences(string text, string marker)
    {
        ArgumentNullException.ThrowIfNull(text);

        var count = 0;
        for (var at = text.IndexOf(marker, StringComparison.Ordinal); at >= 0;
             at = text.IndexOf(marker, at + marker.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
