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
    public static string? Extract(string file) => MarkedBlock.Extract(file, Begin, End);

    /// <summary>The first line that differs, quoted both ways: a diff a person can act on without a tool.</summary>
    public static string FirstDifference(string expected, string actual) => MarkedBlock.FirstDifference(expected, actual);
}
