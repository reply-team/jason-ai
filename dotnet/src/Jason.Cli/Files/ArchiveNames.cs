using System.IO.Compression;

namespace Jason.Cli.Files;

/// <summary>
/// The rules a name in an archive this CLI unpacks is held to before anything is written: one place, so the skills package
/// <c>jason skills install</c> takes, the archive <c>jason export</c> writes and the archive <c>jason import</c> reads apply
/// the same ones, and a name one of them would write is never a name another refuses.
/// </summary>
/// <remarks>
/// A plain name is a relative path of <c>/</c>-separated segments: never rooted, never with a backslash, never with an
/// empty, <c>.</c> or <c>..</c> segment, and never with a segment a file name on some system cannot hold. Names that start
/// with a dot are plain: a pack's <c>.claude-plugin</c>, a harness folder's <c>.jason</c> marker and the role skills'
/// record are all such names. The plugin and UI installers hold their packages to rules of their own, stricter in other
/// ways, and do not use this.
/// </remarks>
internal static class ArchiveNames
{
    /// <summary>Whether an entry is a directory: a name ending in a separator and holding nothing.</summary>
    public static bool IsDirectory(ZipArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.FullName.EndsWith('/') && entry.Length == 0;
    }

    /// <summary>
    /// Whether a name is a plain relative path, as the remarks say. A directory's trailing separator is the caller's to
    /// take off first.
    /// </summary>
    public static bool Plain(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.Length > 0
            && !name.StartsWith('/')
            && !name.Contains('\\', StringComparison.Ordinal)
            && !Path.IsPathRooted(name)
            && name.Split('/').All(segment => segment.Length > 0 && segment is not "." and not ".." && !Unsafe(segment));
    }

    /// <summary>
    /// A segment no path here carries: one with a control character or a character a file name on some system cannot hold,
    /// or one that ends in a dot or a space, which Windows drops from a name, so it would be written as another.
    /// </summary>
    public static bool Unsafe(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        return segment.Any(character => char.IsControl(character) || character is ':' or '*' or '?' or '"' or '<' or '>' or '|')
            || segment.EndsWith('.') || segment.EndsWith(' ');
    }

    /// <summary>What Unix zip tools write for a link: the entry's mode says it is one.</summary>
    public static bool IsLink(ZipArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
    }
}
