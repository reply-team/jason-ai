using System.Text.RegularExpressions;

namespace Jason.Runtime.Tests.Documentation;

/// <summary>One place where the tree points into the work that produced it.</summary>
/// <param name="File">The file, repository-relative.</param>
/// <param name="Line">Its line; 0 for a finding about the file as a whole.</param>
/// <param name="Kind">What was found, in words.</param>
/// <param name="Text">The text that matched.</param>
internal sealed record HygieneFinding(string File, int Line, string Kind, string Text)
{
    /// <summary>The finding without its line, which moves as the text around it changes.</summary>
    public string Key => $"{File} [{Kind}] {Text}";

    public override string ToString() => $"{File}:{Line} [{Kind}] {Text}";
}

/// <summary>
/// What no file of this repository carries, since every file of it is read publicly: a tracker key, an Atlassian
/// address, an attribution trailer, a session link, or another repository of ours. Every text file the tree holds is
/// read, code and comments included; a binary file holds no words. Each pattern is written so that it does not match
/// itself, so the file that holds them is read like any other.
/// </summary>
internal static partial class RepositoryHygiene
{
    /// <summary>The patterns, by the words a finding names them with.</summary>
    public static readonly IReadOnlyList<(string Kind, Regex Pattern)> Patterns =
    [
        ("a tracker key", TrackerKey()),
        ("an Atlassian address", AtlassianAddress()),
        ("an attribution trailer", AttributionTrailer()),
        ("a session link", SessionLink()),
        ("another repository of ours", OtherRepository()),
        ("the private repository by name", PrivateRepository()),
    ];

    /// <summary>Every finding in the text files of the tree under <paramref name="root"/>, file by file.</summary>
    public static IReadOnlyList<HygieneFinding> Findings(string root) =>
    [
        .. RepositoryTree.Files(root)
            .Where(file => RepositoryTree.IsText(root, file))
            .SelectMany(file => Read(file, File.ReadAllText(Path.Combine(root, file.Replace('/', Path.DirectorySeparatorChar))).ReplaceLineEndings("\n"))),
    ];

    /// <summary>Every pattern's matches in one text, in the order of the text.</summary>
    public static List<HygieneFinding> Read(string file, string text)
    {
        var starts = LineStarts(text);
        return [.. Patterns
            .SelectMany(pattern => pattern.Pattern.Matches(text).Select(match => (pattern.Kind, match.Index, match.Value)))
            .OrderBy(match => match.Index)
            .Select(match => new HygieneFinding(file, LineOf(starts, match.Index), match.Kind, match.Value))];
    }

    private static int[] LineStarts(string text) => [0, .. text.Index().Where(pair => pair.Item == '\n').Select(pair => pair.Index + 1)];

    private static int LineOf(int[] starts, int offset)
    {
        var found = Array.BinarySearch(starts, offset);
        return (found >= 0 ? found : ~found - 1) + 1;
    }

    /// <summary>
    /// A key of the tracker, wherever it stands — after a word, a hyphen or an underscore too; an architecture
    /// register's id, three capitals and a hyphen before the key, is not one.
    /// </summary>
    [GeneratedRegex(@"(?<![A-Z]{3}-)REPL[Y]-[0-9]+")]
    private static partial Regex TrackerKey();

    [GeneratedRegex(@"atlassia[n]\.net", RegexOptions.IgnoreCase)]
    private static partial Regex AtlassianAddress();

    /// <summary>The trailers an assistant adds to a commit, in any case as git reads trailers, and the footer it adds to a body.</summary>
    [GeneratedRegex(@"Co-Authored-B[y]:|Claude-Sessio[n]:|Generated wit[h] \[Claude", RegexOptions.IgnoreCase)]
    private static partial Regex AttributionTrailer();

    [GeneratedRegex(@"claude\.a[i]/code", RegexOptions.IgnoreCase)]
    private static partial Regex SessionLink();

    /// <summary><c>reply-team/jason-</c> and any name but <c>ai</c>.</summary>
    [GeneratedRegex(@"reply-team/jaso[n]-(?!ai(?![\w-]))[\w-]+")]
    private static partial Regex OtherRepository();

    /// <summary>The private repository's name on its own, never part of a longer name or a path.</summary>
    [GeneratedRegex(@"(?<![\w/-])jason-interna[l](?![\w-])")]
    private static partial Regex PrivateRepository();
}
