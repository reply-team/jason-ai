using System.Globalization;
using System.Text;

namespace Jason.Cli.Doctor;

/// <summary>
/// Keeps an exception's text to a size a bundle can carry, and says what it left out. An exception's text is the one
/// string in a log that can be as long as somebody's input or as deep as the program: its message lines are cut to a
/// length, all of them to a total, and its stack to a number of frames.
/// </summary>
/// <remarks>
/// <para>
/// A line that is indented and shaped as the runtime writes a frame — <c>at</c>, a dotted method name and an opening
/// bracket — is a frame; every other line is a message line, which is the <c>Type: message</c> line, the lines an inner
/// exception adds after <c>---&gt;</c>, the line that closes its stack, and any line of a message that merely begins
/// with <c>at</c>, such as a sentence or the stack of another language. A message line is kept to
/// <see cref="MaxLineCharacters"/> characters, every message line together to <see cref="MaxMessageCharacters"/>, and
/// the frames of the whole text to <see cref="MaxFrames"/> lines, not that many for each inner exception. What is cut
/// is replaced by a mark, <c>... [truncated N characters]</c> at the end of a line that was cut, or on a line of its own
/// where whole lines were left out, and <c>... [N more frames]</c> where frames were; a run of lines left out one after
/// another is one mark. A text under every limit is returned as it was, line endings and all.
/// </para>
/// <para>
/// <b>Twice is the same as once.</b> The marks are the cap's own words, so a text that already has them is read as
/// capped: a mark on a line of its own is kept and counts for nothing, and a line that ends in one is measured without
/// it and, if it must be cut further, says that much more was cut. Capping the result of a cap changes nothing, which
/// is what lets a text that has been capped once be handed on to be capped again. The cost is that a mark written into
/// a text on purpose is believed: it is kept and counts for nothing, so the cap is no bound against somebody who forges
/// one, and what bounds the size of a bundle is the bounds of its sections. A count is a whole number that a count of
/// characters can be, up to the largest there is; what a sum of counts would take beyond that stays at it, and a number
/// that does not fit is not a count, so a line that ends in one is a line like any other.
/// </para>
/// <para>
/// Characters are what .NET counts, UTF-16 code units, and a cut never falls between the two halves of one character.
/// The cap is meant for text whose names have already been rewritten, see <see cref="IdentityRewriter"/>: a cut through
/// the middle of a path would leave half a name that nothing would recognise afterwards.
/// </para>
/// </remarks>
public static class ExceptionText
{
    /// <summary>The most characters one message line keeps.</summary>
    public const int MaxLineCharacters = 500;

    /// <summary>The most characters all the message lines of a text keep between them.</summary>
    public const int MaxMessageCharacters = 2000;

    /// <summary>The most frames a text keeps.</summary>
    public const int MaxFrames = 60;

    private const string Ellipsis = "...";

    // The words of the two marks, which a later cap reads back.
    private const string TruncatedOpen = " [truncated ";
    private const string TruncatedClose = " characters]";
    private const string MoreFramesOpen = Ellipsis + " [";
    private const string MoreFramesClose = " more frames]";

    /// <summary>The text with its message lines and frames kept to the limits; the text itself when it is under them.</summary>
    public static string Cap(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var capped = new StringBuilder(Math.Min(text.Length, 4096));
        var budget = MaxMessageCharacters;
        var frames = 0;
        var changed = false;

        // What has been left out and not yet marked: a run of frames, or a run of message lines, each with the line
        // ending of the last line in it, which the mark takes over so the text ends as it did. A run is marked when
        // the next line is of the other kind or kept, so a mark stands where what it stands for was.
        var leftOutFrames = 0;
        var endOfFrames = string.Empty;
        var leftOutCharacters = 0L;
        var endOfCharacters = string.Empty;

        void MarkFrames()
        {
            if (leftOutFrames > 0)
            {
                capped.Append(string.Create(CultureInfo.InvariantCulture, $"{MoreFramesOpen}{leftOutFrames}{MoreFramesClose}")).Append(endOfFrames);
                leftOutFrames = 0;
            }
        }

        void MarkCharacters()
        {
            if (leftOutCharacters > 0)
            {
                capped.Append(TruncatedMark(leftOutCharacters)).Append(endOfCharacters);
                leftOutCharacters = 0;
            }
        }

        for (var start = 0; start < text.Length;)
        {
            var newline = text.IndexOf('\n', start);
            var next = newline < 0 ? text.Length : newline + 1;
            var contentEnd = newline > start && text[newline - 1] == '\r' ? newline - 1 : newline < 0 ? text.Length : newline;
            var line = text.AsSpan(start, contentEnd - start);
            var ending = Ending(next - contentEnd);

            if (IsFrame(line))
            {
                MarkCharacters();
                if (frames < MaxFrames)
                {
                    frames++;
                    capped.Append(text, start, next - start);
                }
                else
                {
                    leftOutFrames++;
                    endOfFrames = ending;
                    changed = true;
                }
            }
            else if (IsMark(line))
            {
                // The cap's own mark on a line of its own, from an earlier cap: kept as it stands, and counted for nothing.
                MarkFrames();
                MarkCharacters();
                capped.Append(text, start, next - start);
            }
            else
            {
                MarkFrames();

                // A line that an earlier cap cut ends in its mark: it is measured without it, and says what was cut then too.
                var body = line;
                long alreadyCut = 0;
                if (TryReadTruncatedMark(line, out var bodyLength, out var characters))
                {
                    body = line[..bodyLength];
                    alreadyCut = characters;
                }

                var allowed = Math.Min(MaxLineCharacters, budget);
                if (body.Length <= allowed && budget > 0)
                {
                    MarkCharacters();
                    capped.Append(text, start, next - start);
                    budget -= body.Length;
                }
                else
                {
                    var kept = budget > 0 ? Boundary(body, allowed) : 0;
                    changed = true;
                    if (kept == 0)
                    {
                        // Nothing of it fits: it joins whatever was left out just before, in one mark.
                        leftOutCharacters = Add(leftOutCharacters, Add(alreadyCut, body.Length));
                        endOfCharacters = ending;
                    }
                    else
                    {
                        MarkCharacters();
                        capped.Append(body[..kept]).Append(TruncatedMark(Add(alreadyCut, body.Length - kept))).Append(ending);
                        budget -= kept;
                    }
                }
            }

            start = next;
        }

        MarkFrames();
        MarkCharacters();

        return changed ? capped.ToString() : text;
    }

    /// <summary>The line ending that is this many characters long: none, a line feed, or a carriage return and a line feed.</summary>
    private static string Ending(int length) => length switch
    {
        0 => string.Empty,
        1 => "\n",
        _ => "\r\n",
    };

    /// <summary>Two counts added, held at the largest there is rather than wrapped round to a number below zero, which is what a mark with a count somebody wrote near it would otherwise give.</summary>
    private static long Add(long count, long more) => count > long.MaxValue - more ? long.MaxValue : count + more;

    private static string TruncatedMark(long characters) =>
        Ellipsis + string.Create(CultureInfo.InvariantCulture, $"{TruncatedOpen}{characters}{TruncatedClose}");

    /// <summary>
    /// An indented line that is shaped as the runtime writes a frame: <c>at</c>, a method name with a dot in it, and an
    /// opening bracket straight after, with no space — the form of one frame of a stack trace. A sentence that begins
    /// with <c>at</c>, and a frame of another language, which has a space before its bracket, are not.
    /// </summary>
    private static bool IsFrame(ReadOnlySpan<char> line)
    {
        var indent = 0;
        while (indent < line.Length && char.IsWhiteSpace(line[indent]))
        {
            indent++;
        }

        if (indent == 0 || !line[indent..].StartsWith("at ", StringComparison.Ordinal))
        {
            return false;
        }

        var rest = line[(indent + "at ".Length)..];
        var name = 0;
        while (name < rest.Length && rest[name] != '(' && !char.IsWhiteSpace(rest[name]))
        {
            name++;
        }

        return name < rest.Length && rest[name] == '(' && rest[..name].Contains('.');
    }

    /// <summary>Whether a line is one of the cap's marks and nothing else, from an earlier cap.</summary>
    private static bool IsMark(ReadOnlySpan<char> line) =>
        (TryReadTruncatedMark(line, out var bodyLength, out _) && bodyLength == 0)
        || (line.StartsWith(MoreFramesOpen, StringComparison.Ordinal)
            && line.EndsWith(MoreFramesClose, StringComparison.Ordinal)
            && line.Length > MoreFramesOpen.Length + MoreFramesClose.Length
            && long.TryParse(line[MoreFramesOpen.Length..^MoreFramesClose.Length], NumberStyles.None, CultureInfo.InvariantCulture, out _));

    /// <summary>
    /// Whether a line ends in the mark that says characters were cut from it, and if so how much of the line is before
    /// the mark and how many characters it says were cut.
    /// </summary>
    private static bool TryReadTruncatedMark(ReadOnlySpan<char> line, out int bodyLength, out long characters)
    {
        bodyLength = 0;
        characters = 0;
        if (!line.EndsWith(TruncatedClose, StringComparison.Ordinal))
        {
            return false;
        }

        var withoutClose = line[..^TruncatedClose.Length];
        var open = withoutClose.LastIndexOf(Ellipsis + TruncatedOpen, StringComparison.Ordinal);
        if (open < 0 || !long.TryParse(withoutClose[(open + Ellipsis.Length + TruncatedOpen.Length)..], NumberStyles.None, CultureInfo.InvariantCulture, out characters))
        {
            return false;
        }

        bodyLength = open;
        return true;
    }

    /// <summary>Where to cut a line that may keep <paramref name="allowed"/> characters, so that no character is cut in two.</summary>
    private static int Boundary(ReadOnlySpan<char> line, int allowed) =>
        allowed > 0 && allowed < line.Length && char.IsHighSurrogate(line[allowed - 1]) && char.IsLowSurrogate(line[allowed])
            ? allowed - 1
            : allowed;
}
