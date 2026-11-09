using System.IO.Compression;

namespace Jason.Cli.Files;

/// <summary>Which rule refused a name in an archive: one value per rule, so every rule has a row that names it.</summary>
internal enum NameRefusal
{
    /// <summary>The name is empty.</summary>
    Empty,

    /// <summary>The name starts at the root.</summary>
    Rooted,

    /// <summary>The name holds a backslash, a separator on Windows and a character elsewhere.</summary>
    Backslash,

    /// <summary>The name starts with a drive.</summary>
    Drive,

    /// <summary>A segment is empty.</summary>
    EmptySegment,

    /// <summary>A segment is <c>.</c>.</summary>
    DotSegment,

    /// <summary>A segment is <c>..</c>.</summary>
    ParentSegment,

    /// <summary>A segment is a name Windows keeps for a device, with or without an extension: CON, PRN, AUX, NUL, COM1-COM9, LPT1-LPT9.</summary>
    DeviceName,

    /// <summary>A segment holds a control character.</summary>
    ControlCharacter,

    /// <summary>A segment holds a colon.</summary>
    Colon,

    /// <summary>A segment holds an asterisk.</summary>
    Asterisk,

    /// <summary>A segment holds a question mark.</summary>
    QuestionMark,

    /// <summary>A segment holds a quotation mark.</summary>
    QuotationMark,

    /// <summary>A segment holds a less-than sign.</summary>
    LessThan,

    /// <summary>A segment holds a greater-than sign.</summary>
    GreaterThan,

    /// <summary>A segment holds a vertical bar.</summary>
    VerticalBar,

    /// <summary>A segment ends in a dot.</summary>
    TrailingDot,

    /// <summary>A segment ends in a space.</summary>
    TrailingSpace,
}

/// <summary>
/// The rules a name in an archive this CLI unpacks is held to before anything is written: one place, so the skills package
/// <c>jason skills install</c> takes, the archive <c>jason export</c> writes and the archive <c>jason import</c> reads apply
/// the same ones, and a name one of them would write is never a name another refuses.
/// </summary>
/// <remarks>
/// A plain name is a relative path of <c>/</c>-separated segments: never rooted, never with a drive or a backslash, never
/// with an empty, <c>.</c> or <c>..</c> segment, and never with a segment a file name on some system cannot hold. Names
/// that start with a dot are plain: a pack's <c>.claude-plugin</c>, a harness folder's <c>.jason</c> marker and the role
/// skills' record are all such names. The plugin and UI installers hold their packages to rules of their own, stricter in
/// other ways, and do not use this.
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
    public static bool Plain(string name) => Refusal(name) is null;

    /// <summary>The first rule that refuses a name, in the order the remarks give them; null when the name is plain.</summary>
    public static NameRefusal? Refusal(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length == 0)
        {
            return NameRefusal.Empty;
        }

        if (name.StartsWith('/'))
        {
            return NameRefusal.Rooted;
        }

        if (name.Contains('\\', StringComparison.Ordinal))
        {
            return NameRefusal.Backslash;
        }

        // Spelt out rather than asked of the platform, so the rule is the same on every system an archive is read on.
        if (name.Length >= 2 && char.IsAsciiLetter(name[0]) && name[1] == ':')
        {
            return NameRefusal.Drive;
        }

        foreach (var segment in name.Split('/'))
        {
            if (SegmentRefusal(segment) is { } refusal)
            {
                return refusal;
            }
        }

        return null;
    }

    /// <summary>
    /// The first rule that refuses one segment of a name; null when it is plain. Past the empty, <c>.</c> and <c>..</c>
    /// segments, a segment is refused for a name Windows keeps for a device, for a control character or a character a file name on some system cannot hold, or
    /// for ending in a dot or a space, which Windows drops from a name, so it would be written as another.
    /// </summary>
    private static NameRefusal? SegmentRefusal(string segment)
    {
        if (segment.Length == 0)
        {
            return NameRefusal.EmptySegment;
        }

        if (segment == ".")
        {
            return NameRefusal.DotSegment;
        }

        if (segment == "..")
        {
            return NameRefusal.ParentSegment;
        }

        if (Device(segment))
        {
            return NameRefusal.DeviceName;
        }

        foreach (var character in segment)
        {
            if (char.IsControl(character))
            {
                return NameRefusal.ControlCharacter;
            }

            if (character == ':')
            {
                return NameRefusal.Colon;
            }

            if (character == '*')
            {
                return NameRefusal.Asterisk;
            }

            if (character == '?')
            {
                return NameRefusal.QuestionMark;
            }

            if (character == '"')
            {
                return NameRefusal.QuotationMark;
            }

            if (character == '<')
            {
                return NameRefusal.LessThan;
            }

            if (character == '>')
            {
                return NameRefusal.GreaterThan;
            }

            if (character == '|')
            {
                return NameRefusal.VerticalBar;
            }
        }

        if (segment.EndsWith('.'))
        {
            return NameRefusal.TrailingDot;
        }

        if (segment.EndsWith(' '))
        {
            return NameRefusal.TrailingSpace;
        }

        return null;
    }

    /// <summary>
    /// Whether a segment is a name Windows keeps for a device: what comes before its first dot, in any case, is CON, PRN,
    /// AUX, NUL, COM1 to COM9 or LPT1 to LPT9. Windows opens the device for such a name, so a file another system wrote
    /// under it cannot be placed there.
    /// </summary>
    private static bool Device(string segment)
    {
        var dot = segment.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? segment : segment[..dot];
        return stem.ToUpperInvariant() switch
        {
            "CON" or "PRN" or "AUX" or "NUL" => true,
            [ 'C', 'O', 'M', >= '1' and <= '9' ] or [ 'L', 'P', 'T', >= '1' and <= '9' ] => true,
            _ => false,
        };
    }

    /// <summary>What Unix zip tools write for a link: the entry's mode says it is one.</summary>
    public static bool IsLink(ZipArchiveEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;
    }
}
