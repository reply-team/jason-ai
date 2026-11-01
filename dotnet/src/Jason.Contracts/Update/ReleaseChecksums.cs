using System.Text.RegularExpressions;

namespace Jason.Contracts.Update;

/// <summary>
/// A release's <see cref="ReleaseAssets.Checksums"/>, read for one file: the lines are what <c>sha256sum</c> writes and
/// Write-Manifest.ps1 writes, "&lt;sha256&gt;  &lt;name&gt;", one per file the release carries beside its feed.
/// </summary>
/// <remarks>
/// The install scripts read it with <c>grep "  $ASSET$"</c>; this is the same rule, written once for the CLI. A name is
/// matched whole, so <c>old-jason-ui.zip</c> is not <c>jason-ui.zip</c>, and a name listed twice is a file nobody can
/// say the digest of.
/// </remarks>
public static partial class ReleaseChecksums
{
    /// <summary>The lowercase SHA-256 the file's one line gives it, or null with <paramref name="problem"/> saying why there is none.</summary>
    public static string? DigestOf(string text, string asset, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);

        var lines = Named(text, asset).ToList();

        if (lines.Count == 0)
        {
            problem = $"{ReleaseAssets.Checksums} lists no {asset}.";
            return null;
        }

        if (lines.Count > 1)
        {
            problem = $"{ReleaseAssets.Checksums} lists {asset} more than once.";
            return null;
        }

        var digest = lines[0].Groups["digest"].Value;
        if (!Sha256().IsMatch(digest))
        {
            problem = $"{ReleaseAssets.Checksums}'s line for {asset} holds no SHA-256.";
            return null;
        }

        problem = null;
        return digest.ToLowerInvariant();
    }

    /// <summary>Whether any line names <paramref name="asset"/>: a release from before it was published lists none.</summary>
    public static bool Lists(string text, string asset)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Named(text, asset).Any();
    }

    /// <summary>
    /// The lines that name <paramref name="asset"/>. A mirror or an editor may have saved the file with a byte-order mark
    /// in front, CRLF endings or spaces after a name, none of which is part of a digest or a name.
    /// </summary>
    private static IEnumerable<Match> Named(string text, string asset) =>
        text.TrimStart('\uFEFF')
            .Split('\n')
            .Select(line => Line().Match(line.TrimEnd()))
            .Where(match => match.Success && match.Groups["name"].Value == asset);

    /// <summary>A digest, the spaces, sha256sum's binary marker if it wrote one, the name.</summary>
    [GeneratedRegex(@"^(?<digest>\S+) [ *](?<name>.+)\z")]
    private static partial Regex Line();

    [GeneratedRegex(@"^[0-9A-Fa-f]{64}\z")]
    private static partial Regex Sha256();
}
