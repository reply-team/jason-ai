using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration.Json;

namespace Jason.Runtime.Configuration;

/// <summary>
/// One change the settings writer makes, at one path of the settings file as the configuration system spells one
/// (<c>Routes:Operations:contact.find:Plugin</c>). The edits a caller hands over are the whole of what the writer owns:
/// every other byte of the file is the person's, and is kept.
/// </summary>
public abstract record SettingsEdit(string Path)
{
    /// <summary>The path's keys, as the configuration system separates them.</summary>
    public IReadOnlyList<string> Keys { get; } = Path.Split(':');

    /// <summary>Gives the member at <paramref name="path"/> this value, composing whatever objects above it are missing.</summary>
    public static SettingsEdit Set(string path, JsonNode value) => new SetEdit(path, value);

    /// <summary>Takes the member at <paramref name="path"/> away, whatever it holds; a member that is not there is no change.</summary>
    public static SettingsEdit Remove(string path) => new RemoveEdit(path);

    /// <summary>
    /// Takes the object at <paramref name="path"/> away when it holds no member, comments and all: an empty object is a key
    /// of its own to the configuration system, and one a writer emptied should not stay behind as one.
    /// </summary>
    public static SettingsEdit RemoveIfEmpty(string path) => new RemoveIfEmptyEdit(path);
}

public sealed record SetEdit(string Path, JsonNode Value) : SettingsEdit(Path);

public sealed record RemoveEdit(string Path) : SettingsEdit(Path);

public sealed record RemoveIfEmptyEdit(string Path) : SettingsEdit(Path);

/// <summary>Why the settings file could not be edited; each is said by the setting's path and never by what the file holds.</summary>
public enum SettingsRefusal
{
    /// <summary>The runtime's own parser does not read the file: the start and a reload would refuse it too.</summary>
    Unreadable,

    /// <summary>An object on the path, or the member itself, is written twice, in the same or another case.</summary>
    Twice,

    /// <summary>A key holding a colon stands on the path: to the configuration system it is the path, written a second way.</summary>
    ColonKey,

    /// <summary>The path goes through a value that is not an object.</summary>
    NotAnObject,

    /// <summary>The file is not UTF-8, or holds bytes UTF-8 does not allow.</summary>
    NotUtf8,

    /// <summary>The writer's check of its own edit refused it: the edit would have changed a setting it does not own.</summary>
    CheckRefused,

    /// <summary>
    /// The settings file is a link to another file. The rename that writes it would replace the link with a file of the
    /// writer's own and leave the file it pointed to as it was, so it is not written.
    /// </summary>
    Link,
}

/// <summary>
/// A settings file the writer will not change. <see cref="Exception.Message"/> names the refusal and the path the
/// caller asked for, both the code's own words: nothing the file holds is in it, since a secret sits beside a route.
/// </summary>
public sealed class SettingsFileRefusedException(SettingsRefusal refusal, string? setting, SettingsFileFailure? failure = null)
    : Exception(setting is null ? $"The settings file was refused: {refusal}." : $"The settings file was refused at {setting}: {refusal}.")
{
    public SettingsRefusal Refusal { get; } = refusal;

    /// <summary>The path the refusal is about, as the edit spelled it; null for a file refused whole.</summary>
    public string? Setting { get; } = setting;

    /// <summary>For <see cref="SettingsRefusal.Unreadable"/>, where the parser stopped, as a start's refusal says it.</summary>
    public SettingsFileFailure? Failure { get; } = failure;
}

/// <summary>What an edit made of the file: its new bytes, and whether they differ from the ones it was given.</summary>
public sealed record SettingsEditResult(byte[] Bytes, bool Changed);

/// <summary>
/// The settings file as bytes, edited surgically: the members a caller owns are replaced, inserted or removed, and every
/// other byte — comments, order, whitespace, line endings, the byte order mark, the spelling of every key — is kept. It
/// never serializes the file: a writer that did would drop the person's comments and reorder what they wrote.
/// <para>
/// No I/O: bytes in, bytes out. The writer around it reads and writes the file (<see cref="SettingsWriter"/>).
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>What the file may be.</b> Whatever the runtime's own parser reads, since that is what the start and every reload
/// read: comments of both kinds, trailing commas, keys in any case, escapes in keys. Two places the parser lets one
/// setting be written twice are refused here rather than guessed at — two objects of one name in different cases, which
/// it merges, and a key holding a colon, which it reads as a path. An edit of one copy could collide with the other.
/// </para>
/// <para>
/// <b>The post-condition.</b> Whatever the span logic does, the result is flattened by the same parser and compared with
/// the original: every key outside the edited paths must be there with the same value, and nothing else may have come or
/// gone, except an empty object above an edited path (one composed to hold a new member, or one an edit emptied) and the
/// empty object a removal of an empty object takes away, which the parser reads as a key without a value. At and under
/// each set path the keys must be exactly the value's, with the value's values, so an empty object or a null where a
/// value was written is refused; at and under each removed path there must be none, an empty object included. An edit
/// that fails this is refused whole and nothing is written:
/// this is the net under every case the span logic does not foresee.
/// </para>
/// <para>
/// <b>What it lets through.</b> Only what the parser's flattened view cannot see: a comment, whitespace and line ends, the
/// order of members, and a value's spelling where two spellings flatten to one text — <c>true</c> and <c>"True"</c>, or a
/// string and the same string with its characters escaped. None of these changes a setting the runtime reads, and none
/// is a byte this writer touches outside the spans it edits; that the bytes are kept is held by the tests, byte for byte,
/// and not by this check.
/// </para>
/// </remarks>
public static class SettingsDocument
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    private static readonly JsonSerializerOptions ValueOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, WriteIndented = false };

    /// <summary>
    /// Applies the edits in order. <paramref name="original"/> null is a file that is not there, made as an object
    /// holding the edits; a file whose edits change nothing is answered with its own bytes and <c>Changed</c> false.
    /// </summary>
    /// <exception cref="SettingsFileRefusedException">The file cannot be read or edited safely; nothing was changed.</exception>
    public static SettingsEditResult Edit(byte[]? original, IReadOnlyList<SettingsEdit> edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var start = original ?? "{}\n"u8.ToArray();
        var before = Flatten(start);
        RefuseAnotherEncoding(start);

        var bom = start.AsSpan().StartsWith(Utf8Bom);
        var text = bom ? start[Utf8Bom.Length..] : start;
        var changed = false;
        foreach (var edit in edits)
        {
            var next = Apply(text, edit);
            if (next is not null)
            {
                text = next;
                changed = true;
            }
        }

        if (!changed)
        {
            return new SettingsEditResult(start, Changed: false);
        }

        var result = bom ? [.. Utf8Bom, .. text] : text;
        Verify(before, result, edits);
        return new SettingsEditResult(result, Changed: true);
    }

    /// <summary>
    /// The file as the configuration system holds it: every key its parser makes, with its value — null for an object
    /// with no member and for a JSON null, as the parser reads them. Keys are matched without regard to case.
    /// </summary>
    /// <exception cref="SettingsFileRefusedException"><see cref="SettingsRefusal.Unreadable"/>, with where the parser stopped.</exception>
    public static IReadOnlyDictionary<string, string?> Flatten(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        try
        {
            var provider = new Flattener();
            using var stream = new MemoryStream(bytes, writable: false);
            provider.Load(stream);
            return provider.Keys;
        }
        catch (Exception unparsed) when (unparsed is FormatException or JsonException or InvalidOperationException)
        {
            throw new SettingsFileRefusedException(SettingsRefusal.Unreadable, setting: null, SettingsFileFailure.Of(unparsed));
        }
    }

    /// <summary>
    /// The post-condition (see the remarks on the type): the result is held, by the runtime's own parser, to the original
    /// changed at the edited paths and nowhere else.
    /// </summary>
    internal static void Verify(IReadOnlyDictionary<string, string?> before, byte[] result, IReadOnlyList<SettingsEdit> edits)
    {
        IReadOnlyDictionary<string, string?> after;
        try
        {
            after = Flatten(result);
        }
        catch (SettingsFileRefusedException)
        {
            throw new SettingsFileRefusedException(SettingsRefusal.CheckRefused, edits.Count > 0 ? edits[0].Path : null);
        }

        var expected = new Dictionary<string, string?>(before, StringComparer.OrdinalIgnoreCase);
        foreach (var edit in edits)
        {
            switch (edit)
            {
                case SetEdit set:
                    RemoveAtOrUnder(expected, set.Path);
                    foreach (var (key, value) in Flatten(Encoding.UTF8.GetBytes(Nested(set.Keys, set.Value))))
                    {
                        expected[key] = value;
                    }

                    break;
                case RemoveEdit remove:
                    RemoveAtOrUnder(expected, remove.Path);
                    break;
                case RemoveIfEmptyEdit emptied:
                    if (expected.TryGetValue(emptied.Path, out var held) && held is null && !expected.Keys.Any(key => Under(key, emptied.Path)))
                    {
                        expected.Remove(emptied.Path);
                    }

                    break;
            }
        }

        // An object with no member is a key without a value; one above an edited path may come into being (to hold a new
        // member) or go (when an edit emptied it), and is no setting of anybody's. So may the empty object a removal of
        // an empty object names. At an edited path itself a key without a value is a value like any other, held to the
        // expected one.
        bool Container(string key, string? value) => value is null && edits.Any(edit =>
            Under(edit.Path, key) || (edit is RemoveIfEmptyEdit && string.Equals(edit.Path, key, StringComparison.OrdinalIgnoreCase)));

        bool Same(IReadOnlyDictionary<string, string?> keys, string key, string? value) =>
            keys.TryGetValue(key, out var held) && string.Equals(held, value, StringComparison.Ordinal);

        var unexpected = after.Where(pair => !Container(pair.Key, pair.Value) && !Same(expected, pair.Key, pair.Value));
        var missing = expected.Where(pair => !Container(pair.Key, pair.Value) && !Same(after, pair.Key, pair.Value));
        if (unexpected.Any() || missing.Any())
        {
            throw new SettingsFileRefusedException(SettingsRefusal.CheckRefused, edits.Count > 0 ? edits[0].Path : null);
        }
    }

    private static void RemoveAtOrUnder(Dictionary<string, string?> keys, string path)
    {
        foreach (var key in keys.Keys.Where(key => AtOrUnder(key, path)).ToList())
        {
            keys.Remove(key);
        }
    }

    private static bool AtOrUnder(string key, string path) =>
        string.Equals(key, path, StringComparison.OrdinalIgnoreCase) || Under(key, path);

    private static bool Under(string key, string path) =>
        key.Length > path.Length && key[path.Length] == ':' && key.StartsWith(path, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A byte order mark of UTF-16 or UTF-32. The runtime reads such a file, but an edit in place would have to keep an
    /// encoding nobody writes a settings file in, and rewriting it would change every byte.
    /// </summary>
    private static void RefuseAnotherEncoding(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        if (span.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || span.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || span.StartsWith((ReadOnlySpan<byte>)[0x00, 0x00, 0xFE, 0xFF]))
        {
            throw new SettingsFileRefusedException(SettingsRefusal.NotUtf8, setting: null);
        }
    }

    /// <summary>One edit over the text, or null when it changes nothing.</summary>
    private static byte[]? Apply(byte[] text, SettingsEdit edit)
    {
        var root = Scan(text);
        var found = Locate(root, edit);
        switch (edit)
        {
            case SetEdit set:
                if (found.Member is { } member)
                {
                    var written = Encoding.UTF8.GetBytes(Value(set.Value));
                    return Holds(text, edit.Path, set) ? null : Splice(text, [new Piece(member.ValueStart, member.ValueEnd, written)]);
                }

                return Insert(text, root, found.Object, set.Keys.Skip(found.Depth).ToList(), set.Value);
            case RemoveEdit:
                return found.Member is { } removed ? Splice(text, Deletion(text, found.Object, removed)) : null;
            case RemoveIfEmptyEdit:
                return found.Member is { Object.Members.Count: 0 } empty ? Splice(text, Deletion(text, found.Object, empty)) : null;
            default:
                throw new ArgumentOutOfRangeException(nameof(edit), edit.GetType().Name, "an edit of no known kind.");
        }
    }

    /// <summary>Whether the file already holds exactly this value at the path, so that the set changes nothing and keeps its bytes.</summary>
    private static bool Holds(byte[] text, string path, SetEdit set)
    {
        var now = Flatten(text).Where(pair => AtOrUnder(pair.Key, path)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var wanted = Flatten(Encoding.UTF8.GetBytes(Nested(set.Keys, set.Value)));
        return now.Count == wanted.Count
            && wanted.All(pair => now.TryGetValue(pair.Key, out var value) && string.Equals(value, pair.Value, StringComparison.Ordinal));
    }

    // ---- Where a path is ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Where the edit's path ends: the member it names, or the deepest object on the path and how many of its keys were
    /// found. Keys are matched without regard to case, as the configuration matches them, so a file that says
    /// <c>routes</c> is edited where it says it.
    /// </summary>
    private static (ObjectSpan Object, MemberSpan? Member, int Depth) Locate(ObjectSpan root, SettingsEdit edit)
    {
        var keys = edit.Keys;
        var current = root;
        for (var depth = 0; depth < keys.Count; depth++)
        {
            var key = keys[depth];
            var here = string.Join(':', keys.Take(depth + 1));

            // A key holding a colon is a path to the configuration system, so one whose first key is this one writes this
            // very setting, or one under it, a second way.
            if (current.Members.Any(member => member.Name.Contains(':', StringComparison.Ordinal)
                && string.Equals(member.Name.Split(':')[0], key, StringComparison.OrdinalIgnoreCase)))
            {
                throw new SettingsFileRefusedException(SettingsRefusal.ColonKey, here);
            }

            var matches = current.Members.Where(member => string.Equals(member.Name, key, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count > 1)
            {
                throw new SettingsFileRefusedException(SettingsRefusal.Twice, here);
            }

            if (matches.Count == 0)
            {
                return (current, null, depth);
            }

            if (depth == keys.Count - 1)
            {
                return (current, matches[0], depth + 1);
            }

            current = matches[0].Object ?? throw new SettingsFileRefusedException(SettingsRefusal.NotAnObject, here);
        }

        throw new ArgumentException("an edit with no path.", nameof(edit));
    }

    // ---- Splicing ------------------------------------------------------------------------------------------------------

    /// <summary>A range of the text replaced by new bytes; an insertion is a range of no length.</summary>
    private readonly record struct Piece(int Start, int End, byte[] With);

    private static byte[] Splice(byte[] text, IEnumerable<Piece> pieces)
    {
        var result = text.ToList();
        foreach (var piece in pieces.OrderByDescending(piece => piece.Start))
        {
            result.RemoveRange(piece.Start, piece.End - piece.Start);
            result.InsertRange(piece.Start, piece.With);
        }

        return [.. result];
    }

    /// <summary>
    /// What deleting one member takes away: the member, from its name to the end of its value, and the comma that
    /// separated it from a neighbour — the one after it, or the one before it where it was the last. Where it stood alone
    /// on its lines, the whole lines go, so no empty line is left; otherwise only the member and the spaces beside its
    /// comma. A comment outside that span stays, even one that was about the member.
    /// </summary>
    private static List<Piece> Deletion(byte[] text, ObjectSpan owner, MemberSpan member)
    {
        var index = owner.Members.IndexOf(member);
        var previous = index > 0 ? owner.Members[index - 1] : null;
        var pieces = new List<Piece>();

        var lineStart = LineStart(text, member.NameStart);
        var aloneBefore = Blank(text, lineStart, member.NameStart);
        var commaAfterOnLine = member.Comma >= 0 && !HasNewline(text, member.ValueEnd, member.Comma);
        var end = commaAfterOnLine ? member.Comma + 1 : member.ValueEnd;
        var lineEnd = SkipBlanks(text, end);
        var aloneAfter = lineEnd < text.Length && text[lineEnd] is (byte)'\n' or (byte)'\r';

        if (aloneBefore && aloneAfter)
        {
            pieces.Add(new Piece(lineStart, lineEnd + NewlineLength(text, lineEnd), []));
            if (!commaAfterOnLine)
            {
                AddSeparatingComma(pieces, member, previous);
            }

            return pieces;
        }

        if (commaAfterOnLine)
        {
            pieces.Add(new Piece(member.NameStart, SkipBlanks(text, member.Comma + 1), []));
            return pieces;
        }

        if (member.Comma < 0 && previous is { Comma: >= 0 } && Blank(text, previous.Comma + 1, member.NameStart))
        {
            pieces.Add(new Piece(previous.Comma, member.ValueEnd, []));
            return pieces;
        }

        pieces.Add(new Piece(member.NameStart, member.ValueEnd, []));
        AddSeparatingComma(pieces, member, previous);
        return pieces;
    }

    private static void AddSeparatingComma(List<Piece> pieces, MemberSpan member, MemberSpan? previous)
    {
        if (member.Comma >= 0)
        {
            pieces.Add(new Piece(member.Comma, member.Comma + 1, []));
        }
        else if (previous is { Comma: >= 0 })
        {
            pieces.Add(new Piece(previous.Comma, previous.Comma + 1, []));
        }
    }

    /// <summary>
    /// Inserts a member for the keys still missing below <paramref name="owner"/>, composing an object for each but the
    /// last. In an object written one member to a line it goes on a line of its own at its siblings' indentation, after
    /// the line of the last member (and after a comment ending that line, never inside it); in an object written on one
    /// line it goes after the last member on that line. The file's own newline is used. Where the last member ends in a
    /// trailing comma the new one ends in one too, and where it does not the new one does not: so a file in plain JSON,
    /// which the desktop's own writer needs, stays plain JSON, and deleting what was inserted gives back the same bytes.
    /// </summary>
    private static byte[] Insert(byte[] text, ObjectSpan root, ObjectSpan owner, List<string> keys, JsonNode value)
    {
        var newline = Newline(text);
        var unit = Unit(text, root);
        if (owner.Members.Count > 0)
        {
            var last = owner.Members[^1];
            var lineStart = LineStart(text, last.NameStart);
            var afterLast = last.Comma >= 0 ? last.Comma + 1 : last.ValueEnd;
            if (Blank(text, lineStart, last.NameStart) && HasNewline(text, afterLast, owner.Close))
            {
                var indent = Encoding.UTF8.GetString(text, lineStart, last.NameStart - lineStart);
                var at = LineEnd(text, afterLast);
                var member = Encoding.UTF8.GetBytes(newline + indent + Composed(keys, value, indent, unit, newline) + (last.Comma >= 0 ? "," : string.Empty));
                var pieces = new List<Piece> { new(at, at, member) };
                if (last.Comma < 0)
                {
                    pieces.Add(new Piece(last.ValueEnd, last.ValueEnd, ","u8.ToArray()));
                }

                return Splice(text, pieces);
            }

            var inline = Encoding.UTF8.GetBytes(last.Comma >= 0 ? " " + Composed(keys, value, indent: null, unit, newline) + "," : ", " + Composed(keys, value, indent: null, unit, newline));
            return Splice(text, [new Piece(afterLast, afterLast, inline)]);
        }

        var parentStart = LineStart(text, owner.Open);
        var parentIndent = Encoding.UTF8.GetString(text, parentStart, Indentation(text, parentStart) - parentStart);
        var inner = parentIndent + unit;
        var opened = newline + inner + Composed(keys, value, inner, unit, newline)
            + (HasNewline(text, owner.Open + 1, owner.Close) ? string.Empty : newline + parentIndent);
        return Splice(text, [new Piece(owner.Open + 1, owner.Open + 1, Encoding.UTF8.GetBytes(opened))]);
    }

    /// <summary>
    /// <c>"key": value</c> for the last key, each key before it an object holding the next: on lines of their own at
    /// <paramref name="indent"/> and one <paramref name="unit"/> deeper per object, or on one line where the indent is null.
    /// </summary>
    private static string Composed(IReadOnlyList<string> keys, JsonNode value, string? indent, string unit, string newline)
    {
        var name = JsonSerializer.Serialize(keys[0], ValueOptions);
        if (keys.Count == 1)
        {
            return $"{name}: {Value(value)}";
        }

        if (indent is null)
        {
            return $"{name}: {{{Composed([.. keys.Skip(1)], value, null, unit, newline)}}}";
        }

        var deeper = indent + unit;
        return $"{name}: {{{newline}{deeper}{Composed([.. keys.Skip(1)], value, deeper, unit, newline)}{newline}{indent}}}";
    }

    /// <summary>A value as the writer writes it: compact, on one line, with no escape a person would not have typed.</summary>
    private static string Value(JsonNode value) => value.ToJsonString(ValueOptions);

    /// <summary>The value nested under the keys, as a whole document: what the parser reads a set as.</summary>
    private static string Nested(IReadOnlyList<string> keys, JsonNode value)
    {
        var document = new StringBuilder();
        foreach (var key in keys)
        {
            document.Append('{').Append(JsonSerializer.Serialize(key, ValueOptions)).Append(':');
        }

        document.Append(Value(value)).Append('}', keys.Count);
        return document.ToString();
    }

    // ---- The text's own layout -----------------------------------------------------------------------------------------

    /// <summary>The file's newline: the first one it holds, <c>\n</c> where it holds none.</summary>
    private static string Newline(byte[] text)
    {
        var at = Array.IndexOf(text, (byte)'\n');
        return at > 0 && text[at - 1] == '\r' ? "\r\n" : "\n";
    }

    /// <summary>One step of indentation: the indentation of the top object's first member where it stands on a line of its own, two spaces otherwise.</summary>
    private static string Unit(byte[] text, ObjectSpan root)
    {
        if (root.Members.Count > 0)
        {
            var first = root.Members[0];
            var lineStart = LineStart(text, first.NameStart);
            var parentStart = LineStart(text, root.Open);
            var parentIndent = Indentation(text, parentStart) - parentStart;
            if (first.NameStart - lineStart > parentIndent && Blank(text, lineStart, first.NameStart))
            {
                return Encoding.UTF8.GetString(text, lineStart + parentIndent, first.NameStart - lineStart - parentIndent);
            }
        }

        return "  ";
    }

    private static int LineStart(byte[] text, int at)
    {
        var newline = at > 0 ? Array.LastIndexOf(text, (byte)'\n', at - 1) : -1;
        return newline + 1;
    }

    /// <summary>Where the whitespace that begins the line at <paramref name="lineStart"/> ends.</summary>
    private static int Indentation(byte[] text, int lineStart)
    {
        var at = lineStart;
        while (at < text.Length && text[at] is (byte)' ' or (byte)'\t')
        {
            at++;
        }

        return at;
    }

    private static bool Blank(byte[] text, int from, int to)
    {
        for (var at = from; at < to; at++)
        {
            if (text[at] is not ((byte)' ' or (byte)'\t'))
            {
                return false;
            }
        }

        return true;
    }

    private static int SkipBlanks(byte[] text, int at)
    {
        while (at < text.Length && text[at] is (byte)' ' or (byte)'\t')
        {
            at++;
        }

        return at;
    }

    private static int NewlineLength(byte[] text, int at) =>
        at >= text.Length ? 0 : text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n' ? 2 : 1;

    /// <summary>Whether a line ends between the two places, outside any comment.</summary>
    private static bool HasNewline(byte[] text, int from, int to)
    {
        var at = from;
        while (at < to)
        {
            if (text[at] == '\n')
            {
                return true;
            }

            // A line comment runs to its newline, which is the one this looks for; a block comment's newlines are inside it.
            at = text[at] == '/' && at + 1 < to && text[at + 1] == '/' ? LineCommentEnd(text, at)
                : text[at] == '/' && at + 1 < to && text[at + 1] == '*' ? BlockCommentEnd(text, at)
                : at + 1;
        }

        return false;
    }

    /// <summary>
    /// Where the line that <paramref name="at"/> is on ends — the newline's first byte, or the end of the text — passing
    /// over a line comment to its end and over a block comment whole, so that nothing is ever inserted inside a comment.
    /// </summary>
    private static int LineEnd(byte[] text, int at)
    {
        while (at < text.Length && text[at] is not ((byte)'\n' or (byte)'\r'))
        {
            if (text[at] == '/' && at + 1 < text.Length && text[at + 1] == '/')
            {
                return LineCommentEnd(text, at);
            }

            if (text[at] == '/' && at + 1 < text.Length && text[at + 1] == '*')
            {
                at = BlockCommentEnd(text, at);
                continue;
            }

            at++;
        }

        return at;
    }

    /// <summary>Where a line comment ends: at its newline's first byte, or the end of the text.</summary>
    private static int LineCommentEnd(byte[] text, int start)
    {
        var at = start + 2;
        while (at < text.Length && text[at] is not ((byte)'\n' or (byte)'\r'))
        {
            at++;
        }

        return at;
    }

    private static int BlockCommentEnd(byte[] text, int start)
    {
        for (var at = start + 2; at + 1 < text.Length; at++)
        {
            if (text[at] == '*' && text[at + 1] == '/')
            {
                return at + 2;
            }
        }

        return text.Length;
    }

    // ---- The scan ------------------------------------------------------------------------------------------------------

    /// <summary>An object of the text: where its braces are, and its members in order.</summary>
    private sealed class ObjectSpan(int open)
    {
        public int Open { get; } = open;

        public int Close { get; set; }

        public List<MemberSpan> Members { get; } = [];
    }

    /// <summary>
    /// One member: its name unescaped, where its name starts, where its value starts and ends, the object the value is
    /// when it is one, and the comma that follows it, or -1.
    /// </summary>
    private sealed record MemberSpan(string Name, int NameStart, int ValueStart, int ValueEnd, ObjectSpan? Object, int Comma);

    /// <summary>
    /// The text's objects and members, by their byte offsets, read with the parser's own allowances: comments, trailing
    /// commas, a depth of 64. The parser has read the same text without refusing it before this runs; what this refuses
    /// besides is text that is not UTF-8, which the parser reads through a decoder that replaces what it cannot decode.
    /// </summary>
    private static ObjectSpan Scan(byte[] text)
    {
        var reader = new Utf8JsonReader(text, new JsonReaderOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 64 });
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            {
                throw new SettingsFileRefusedException(SettingsRefusal.Unreadable, setting: null);
            }

            return ReadObject(ref reader, text);
        }
        catch (Exception undecoded) when (undecoded is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new SettingsFileRefusedException(SettingsRefusal.NotUtf8, setting: null);
        }
    }

    private static ObjectSpan ReadObject(ref Utf8JsonReader reader, byte[] text)
    {
        var span = new ObjectSpan((int)reader.TokenStartIndex);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                span.Close = (int)reader.TokenStartIndex;
                return span;
            }

            var name = reader.GetString()!;
            var nameStart = (int)reader.TokenStartIndex;
            reader.Read();
            var valueStart = (int)reader.TokenStartIndex;
            ObjectSpan? child = null;
            int valueEnd;
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    child = ReadObject(ref reader, text);
                    valueEnd = child.Close + 1;
                    break;
                case JsonTokenType.StartArray:
                    reader.Skip();
                    valueEnd = (int)reader.TokenStartIndex + 1;
                    break;
                default:
                    // A string is decoded here so that bytes UTF-8 does not allow are met now, not in a value written back.
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        _ = reader.GetString();
                    }

                    valueEnd = (int)reader.BytesConsumed;
                    break;
            }

            span.Members.Add(new MemberSpan(name, nameStart, valueStart, valueEnd, child, CommaAfter(text, valueEnd)));
        }

        throw new SettingsFileRefusedException(SettingsRefusal.Unreadable, setting: null);
    }

    /// <summary>The comma after a value, past whitespace and comments, or -1 where the next thing is not one.</summary>
    private static int CommaAfter(byte[] text, int at)
    {
        while (at < text.Length)
        {
            switch (text[at])
            {
                case (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n':
                    at++;
                    break;
                case (byte)'/' when at + 1 < text.Length && text[at + 1] == '/':
                    at = LineEnd(text, at);
                    break;
                case (byte)'/' when at + 1 < text.Length && text[at + 1] == '*':
                    at = BlockCommentEnd(text, at);
                    break;
                case (byte)',':
                    return at;
                default:
                    return -1;
            }
        }

        return -1;
    }

    /// <summary>The configuration system's own JSON provider, read for the keys it makes.</summary>
    private sealed class Flattener() : JsonStreamConfigurationProvider(new JsonStreamConfigurationSource())
    {
        public IReadOnlyDictionary<string, string?> Keys => new Dictionary<string, string?>(Data, StringComparer.OrdinalIgnoreCase);
    }
}
