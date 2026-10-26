using System.Globalization;
using System.Text;

namespace Jason.Contracts.Operations;

/// <summary>
/// The pattern grammar this dialect publishes: every construct a schema's pattern may write, outside a character class
/// and inside one, and what becomes of it. A construct is admitted as written where ECMA-262, the dialect JSON Schema
/// names, and the engine that runs the patterns read it the same way; rewritten to the engine's spelling of ECMA-262's
/// meaning where they differ and one can be written; and refused by name where they differ and none can, or where the
/// engine cannot run it without backtracking. A construct no row names is refused as unknown. The table is the one the
/// contracts README publishes, and a test holds the two to each other row by row.
/// </summary>
internal static class PatternGrammar
{
    /// <summary>What becomes of a construct in one place.</summary>
    internal enum Kind
    {
        Admitted,
        Rewritten,
        Refused,

        /// <summary>The construct cannot stand in this place: a group inside a class, a range outside one.</summary>
        NotApplicable,
    }

    /// <summary>What becomes of a construct in one place, and, for a rewrite, what it is written as.</summary>
    internal sealed record Treatment(Kind Kind, string? Becomes = null)
    {
        public static Treatment Admit { get; } = new(Kind.Admitted);

        public static Treatment Refuse { get; } = new(Kind.Refused);

        public static Treatment None { get; } = new(Kind.NotApplicable);

        public static Treatment To(string becomes) => new(Kind.Rewritten, becomes);

        /// <summary>As the published table shows it.</summary>
        public string Shown => Kind switch
        {
            Kind.Admitted => "admitted",
            Kind.Refused => "refused",
            Kind.NotApplicable => "—",
            _ => $"`{Becomes}`",
        };
    }

    /// <summary>
    /// One row of the published table: the construct as the README writes it, the words a refusal names it by, what
    /// becomes of it outside a class and inside one, and why.
    /// </summary>
    internal sealed record Row(string Key, string Construct, string Name, Treatment Outside, Treatment Inside, string Why);

    /// <summary>A refused construct: its row, the construct as the pattern writes it, and where it starts (from 1).</summary>
    internal sealed record Refusal(Row Row, string Construct, int Position);

    /// <summary>A dot outside a class: every character but the four line terminators ECMA-262 names.</summary>
    internal static readonly string AnyButLineTerminator = "[^" + @"\n\r" + Code(0x2028) + Code(0x2029) + "]";

    /// <summary>
    /// ECMA-262's white space and line terminators, as the members of a class: tab, line feed, vertical tab, form feed,
    /// carriage return, space, no-break space, the Unicode space separators, the line and paragraph separators, and the
    /// byte order mark — U+FEFF is one of them and U+0085 is not, where the engine's own class has it the other way.
    /// </summary>
    internal static readonly string SpaceMembers =
        @"\t\n\v\f\r "
        + Code(0x00A0) + Code(0x1680) + Code(0x2000) + "-" + Code(0x200A)
        + Code(0x2028) + Code(0x2029) + Code(0x202F) + Code(0x205F) + Code(0x3000) + Code(0xFEFF);

    private const string DigitMembers = "0-9";

    private const string WordMembers = "A-Za-z0-9_";

    /// <summary>The characters an escape may stand before to mean that character itself.</summary>
    private const string IdentityEscaped = @"^$\.*+?()[]{}|/-";

    private const string Linear = "the engine that runs patterns cannot run it without backtracking, which keeps a hostile pattern linear";

    private const string NoFlags = "a schema's pattern carries no flags, and ECMA-262 reads it only with one";

    /// <summary>The published table, in the order the README lists it.</summary>
    internal static IReadOnlyList<Row> Rows { get; } =
    [
        new("literal", "any character no other row names", "a character", Treatment.Admit, Treatment.Admit, "itself, in both readings"),
        new("caret", "`^`", "the start of the value", Treatment.Admit, Treatment.Admit, "the start of the value; first in a class, the class's negation; elsewhere in a class, itself"),
        new("dollar", "`$`", "the end of the value", Treatment.To(@"\z"), Treatment.Admit, "ECMA-262's end is the end of the value, where the engine's also matches before a final line feed; in a class, itself"),
        new("dot", "`.`", "any character", Treatment.To(AnyButLineTerminator), Treatment.Admit, "ECMA-262 leaves out the four line terminators, where the engine leaves out only the line feed; in a class, itself"),
        new("alternation", "`|`", "an alternation", Treatment.Admit, Treatment.Admit, "the same in both readings; in a class, itself"),
        new("group", "`(` ... `)`", "a group", Treatment.Admit, Treatment.Admit, "the same in both readings; in a class, itself"),
        new("noncapturing", "`(?:` ... `)`", "a non-capturing group", Treatment.Admit, Treatment.None, "the same in both readings"),
        new("lookahead", "`(?=`, `(?!`", "a lookahead", Treatment.Refuse, Treatment.None, Linear),
        new("lookbehind", "`(?<=`, `(?<!`", "a lookbehind", Treatment.Refuse, Treatment.None, Linear),
        new("named", "`(?<`name`>`, `(?'`name`'`", "a named group", Treatment.Refuse, Treatment.None, "a name serves nothing in a pattern that only accepts or refuses a value"),
        new("atomic", "`(?>`", "an atomic group", Treatment.Refuse, Treatment.None, "the engine's own; ECMA-262 has none"),
        new("conditional", "`(?(`", "a conditional", Treatment.Refuse, Treatment.None, "the engine's own; ECMA-262 has none"),
        new("comment", "`(?#`", "a comment", Treatment.Refuse, Treatment.None, "the engine's own; ECMA-262 has none"),
        new("options", "`(?i`, `(?m`, `(?n`, `(?s`, `(?x`, `(?-`", "inline options", Treatment.Refuse, Treatment.None, "the engine's own; " + NoFlags),
        new("unknowngroup", "any other `(?`", "a group this dialect does not know", Treatment.Refuse, Treatment.None, "neither reading has it"),
        new("quantifier", "`*`, `+`, `?`, each also followed by `?`", "a quantifier", Treatment.Admit, Treatment.Admit, "the same in both readings; in a class, itself"),
        new("counted", "`{n}`, `{n,}`, `{n,m}`, each also followed by `?`", "a counted quantifier", Treatment.Admit, Treatment.Admit, "the same in both readings; in a class, itself"),
        new("brace", "`{` that starts no counted quantifier, and `}`", "a brace that is not a quantifier", Treatment.Refuse, Treatment.Admit, "only ECMA-262's older grammar reads it as itself; write `\\{` or `\\}`; in a class, itself"),
        new("closing", "`]` outside a class", "a closing bracket", Treatment.Admit, Treatment.None, "itself, in both readings"),
        new("class", "`[` ... `]`, `[^` ... `]`", "a class", Treatment.Admit, Treatment.Admit, "the same in both readings; in a class, `[` is itself"),
        new("emptyclass", "`[]`, `[^]`", "a class that begins with `]`", Treatment.Refuse, Treatment.None, "ECMA-262 reads `[]` as a class of nothing and `[^]` as any character, where the engine reads the `]` as a member; write `\\]` for a bracket in a class"),
        new("unclosed", "a `[` never closed", "an unclosed class", Treatment.Refuse, Treatment.None, "neither reading has it"),
        new("range", "`a-z`", "a range", Treatment.None, Treatment.Admit, "the same in both readings"),
        new("classrange", "a range with `\\d`, `\\w` or `\\s` at either end", "a range from or to a class", Treatment.None, Treatment.Refuse, "ECMA-262's older grammar reads it as three members, where the engine reads it its own way"),
        new("subtraction", "`-[`", "a class subtraction", Treatment.None, Treatment.Refuse, "the engine's own; in ECMA-262 the `[` is a member"),
        new("digit", "`\\d`", "a digit", Treatment.To("[" + DigitMembers + "]"), Treatment.To(DigitMembers), "ECMA-262's digits are the ten ASCII ones, where the engine's are every script's"),
        new("nondigit", "`\\D`", "a non-digit", Treatment.To("[^" + DigitMembers + "]"), Treatment.Refuse, "its complement of ECMA-262's digits; in a class, a complement cannot be written as members"),
        new("word", "`\\w`", "a word character", Treatment.To("[" + WordMembers + "]"), Treatment.To(WordMembers), "ECMA-262's word characters are ASCII letters, digits and `_`, where the engine's are every script's"),
        new("nonword", "`\\W`", "a non-word character", Treatment.To("[^" + WordMembers + "]"), Treatment.Refuse, "its complement of ECMA-262's word characters; in a class, a complement cannot be written as members"),
        new("space", "`\\s`", "white space", Treatment.To("[" + SpaceMembers + "]"), Treatment.To(SpaceMembers), "ECMA-262's white space and line terminators: U+FEFF is one, U+0085 is not, the other way round from the engine"),
        new("nonspace", "`\\S`", "a non-space", Treatment.To("[^" + SpaceMembers + "]"), Treatment.Refuse, "its complement of ECMA-262's white space; in a class, a complement cannot be written as members"),
        new("boundary", "`\\b`, `\\B`", "a word boundary", Treatment.Refuse, Treatment.Refuse, "a boundary between word characters, which the two readings define differently; in a class ECMA-262 reads `\\b` as a backspace"),
        new("anchors", "`\\A`, `\\Z`, `\\z`, `\\G`", "an anchor of the engine's", Treatment.Refuse, Treatment.Refuse, "ECMA-262 reads it as the letter, the engine as an anchor; write the letter without the backslash, or `^` and `$` for the start and the end"),
        new("property", "`\\p{`...`}`, `\\P{`...`}`", "a Unicode property", Treatment.Refuse, Treatment.Refuse, NoFlags),
        new("backreference", "`\\1` to `\\9`, `\\k`", "a backreference", Treatment.Refuse, Treatment.Refuse, Linear + "; in a class ECMA-262 reads a digit or a letter"),
        new("octal", "`\\0` followed by a digit", "an octal escape", Treatment.Refuse, Treatment.Refuse, "ECMA-262's older grammar reads an octal escape, the engine its own"),
        new("nul", "`\\0` followed by no digit", "the NUL character", Treatment.Admit, Treatment.Admit, "the same in both readings"),
        new("control", "`\\t`, `\\n`, `\\v`, `\\f`, `\\r`", "a control character", Treatment.Admit, Treatment.Admit, "the same in both readings"),
        new("hex", "`\\x` followed by two hex digits", "a character by its code", Treatment.Admit, Treatment.Admit, "the same in both readings"),
        new("unicode", "`\\u` followed by four hex digits", "a character by its code", Treatment.Admit, Treatment.Admit, "the same in both readings"),
        new("badcode", "`\\x` or `\\u` without exactly its hex digits, and `\\u{`", "a malformed character code", Treatment.Refuse, Treatment.Refuse, "a code point in braces needs a flag a schema's pattern does not have; otherwise neither reading has it"),
        new("controlletter", "`\\c`", "a control letter", Treatment.Refuse, Treatment.Refuse, "the two readings do not agree on its letters"),
        new("engineescape", "`\\e`, `\\a`", "an escape of the engine's", Treatment.Refuse, Treatment.Refuse, "the engine's own; ECMA-262 reads the letter"),
        new("identity", "`\\` before one of `^ $ \\ . * + ? ( ) [ ] { } | / -`", "an escaped syntax character", Treatment.Admit, Treatment.Admit, "that character itself, in both readings"),
        new("unknownescape", "`\\` before any other character", "an escape this dialect does not know", Treatment.Refuse, Treatment.Refuse, "the two readings do not agree on what it means, or neither has it"),
        new("trailing", "`\\` at the end", "a backslash with nothing after it", Treatment.Refuse, Treatment.Refuse, "neither reading has it"),
    ];

    private static readonly Dictionary<string, Row> ByKey = Rows.ToDictionary(row => row.Key, StringComparer.Ordinal);

    /// <summary>
    /// The table as the contracts README publishes it, one line for each row after the header and its rule. A `|` in a
    /// cell is written `\|`, which a table cell needs and a code span inside one still shows as itself.
    /// </summary>
    internal static IReadOnlyList<string> Table()
    {
        static string Cell(string text) => text.Replace("|", @"\|", StringComparison.Ordinal);

        var lines = new List<string>
        {
            "| Construct | Outside a class | Inside a class | Why |",
            "|---|---|---|---|",
        };
        lines.AddRange(Rows.Select(row =>
            $"| {Cell(row.Construct)} | {Cell(row.Outside.Shown)} | {Cell(row.Inside.Shown)} | {Cell(row.Why)} |"));
        return lines;
    }

    /// <summary>
    /// The pattern as the engine is handed it, and, through <paramref name="refusal"/>, the first construct this grammar
    /// refuses in it — null when it refuses none, and the form is then the one to compile. One pass over the pattern as
    /// its author wrote it.
    /// </summary>
    internal static string Translate(string pattern, out Refusal? refusal)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        refusal = null;
        var form = new StringBuilder(pattern.Length + 16);
        var index = 0;
        while (index < pattern.Length && refusal is null)
        {
            var character = pattern[index];
            switch (character)
            {
                case '\\':
                    refusal = Escape(pattern, ref index, inClass: false, form);
                    break;
                case '[':
                    refusal = Class(pattern, ref index, form);
                    break;
                case '(':
                    refusal = Group(pattern, ref index, form);
                    break;
                case '{':
                    refusal = Counted(pattern, ref index, form);
                    break;
                case '}':
                    refusal = Refused("brace", "}", index);
                    break;
                case '$':
                    form.Append(ByKey["dollar"].Outside.Becomes);
                    index++;
                    break;
                case '.':
                    form.Append(ByKey["dot"].Outside.Becomes);
                    index++;
                    break;
                default:
                    form.Append(character);
                    index++;
                    break;
            }
        }

        return form.ToString();
    }

    /// <summary>A class from its `[` to its `]`, member by member.</summary>
    private static Refusal? Class(string pattern, ref int index, StringBuilder form)
    {
        var start = index;
        form.Append('[');
        index++;
        if (index < pattern.Length && pattern[index] == '^')
        {
            form.Append('^');
            index++;
        }

        if (index < pattern.Length && pattern[index] == ']')
        {
            return Refused("emptyclass", pattern[start..(index + 1)], start);
        }

        var members = 0;
        var afterClassEscape = false;
        while (index < pattern.Length)
        {
            var character = pattern[index];
            if (character == ']')
            {
                form.Append(']');
                index++;
                return null;
            }

            if (character == '\\')
            {
                var shorthand = index + 1 < pattern.Length && pattern[index + 1] is 'd' or 'w' or 's';
                if (Escape(pattern, ref index, inClass: true, form) is { } refused)
                {
                    return refused;
                }

                members++;
                afterClassEscape = shorthand;
                continue;
            }

            if (character == '-')
            {
                var next = index + 1 < pattern.Length ? pattern[index + 1] : '\0';
                if (next == '[')
                {
                    return Refused("subtraction", "-[", index);
                }

                var isRange = members > 0 && next != ']' && index + 1 < pattern.Length;
                if (isRange && (afterClassEscape || (next == '\\' && index + 2 < pattern.Length && pattern[index + 2] is 'd' or 'w' or 's' or 'D' or 'W' or 'S')))
                {
                    return Refused("classrange", "-", index);
                }

                // As written: a range's hyphen, or a hyphen first or last in the class, which both readings take as itself.
                form.Append('-');
                index++;
                afterClassEscape = false;
                continue;
            }

            // A member as written: `$`, `.`, `^`, `(`, `|`, `*`, `{`, `[` and the rest are themselves in a class in both
            // readings — a `[` is more only after a `-`, which is the subtraction refused above.
            form.Append(character);
            index++;
            members++;
            afterClassEscape = false;
        }

        return Refused("unclosed", "[", start);
    }

    /// <summary>A `(`: a group, a non-capturing group, or one of the `(?` forms this dialect refuses.</summary>
    private static Refusal? Group(string pattern, ref int index, StringBuilder form)
    {
        if (index + 1 >= pattern.Length || pattern[index + 1] != '?')
        {
            form.Append('(');
            index++;
            return null;
        }

        var start = index;
        var kind = index + 2 < pattern.Length ? pattern[index + 2] : '\0';
        var after = index + 3 < pattern.Length ? pattern[index + 3] : '\0';
        switch (kind)
        {
            case ':':
                form.Append("(?:");
                index += 3;
                return null;
            case '=' or '!':
                return Refused("lookahead", pattern.Substring(start, 3), start);
            case '<' when after is '=' or '!':
                return Refused("lookbehind", pattern.Substring(start, 4), start);
            case '<' or '\'':
                return Refused("named", pattern.Substring(start, 3), start);
            case '>':
                return Refused("atomic", "(?>", start);
            case '(':
                return Refused("conditional", "(?(", start);
            case '#':
                return Refused("comment", "(?#", start);
            case 'i' or 'm' or 'n' or 's' or 'x' or '-':
                return Refused("options", pattern.Substring(start, 3), start);
            case '\0':
                return Refused("unknowngroup", "(?", start);
            default:
                return Refused("unknowngroup", pattern.Substring(start, 3), start);
        }
    }

    /// <summary>A `{`: a counted quantifier as written, or a brace that starts none, refused.</summary>
    private static Refusal? Counted(string pattern, ref int index, StringBuilder form)
    {
        var at = index + 1;
        var first = Digits(pattern, ref at);
        if (first > 0 && at < pattern.Length && pattern[at] == ',')
        {
            at++;
            _ = Digits(pattern, ref at);
        }

        if (first == 0 || at >= pattern.Length || pattern[at] != '}')
        {
            return Refused("brace", "{", index);
        }

        form.Append(pattern, index, at + 1 - index);
        index = at + 1;
        return null;
    }

    private static int Digits(string pattern, ref int at)
    {
        var start = at;
        while (at < pattern.Length && char.IsAsciiDigit(pattern[at]))
        {
            at++;
        }

        return at - start;
    }

    /// <summary>A backslash and what follows it, in a class or outside one.</summary>
    private static Refusal? Escape(string pattern, ref int index, bool inClass, StringBuilder form)
    {
        var start = index;
        if (index + 1 >= pattern.Length)
        {
            return Refused("trailing", @"\", start);
        }

        var escaped = pattern[index + 1];
        var construct = pattern.Substring(start, 2);
        Row? row = escaped switch
        {
            'd' => ByKey["digit"],
            'D' => ByKey["nondigit"],
            'w' => ByKey["word"],
            'W' => ByKey["nonword"],
            's' => ByKey["space"],
            'S' => ByKey["nonspace"],
            'b' or 'B' => ByKey["boundary"],
            'A' or 'Z' or 'z' or 'G' => ByKey["anchors"],
            'p' or 'P' => ByKey["property"],
            'k' or (>= '1' and <= '9') => ByKey["backreference"],
            'c' => ByKey["controlletter"],
            'e' or 'a' => ByKey["engineescape"],
            _ => null,
        };

        if (row is not null)
        {
            var treatment = inClass ? row.Inside : row.Outside;
            if (treatment.Kind == Kind.Refused)
            {
                return new Refusal(row, construct, start + 1);
            }

            form.Append(treatment.Becomes);
            index += 2;
            return null;
        }

        switch (escaped)
        {
            case '0' when index + 2 < pattern.Length && char.IsAsciiDigit(pattern[index + 2]):
                return Refused("octal", pattern.Substring(start, 3), start);
            case '0' or 't' or 'n' or 'v' or 'f' or 'r':
                form.Append(construct);
                index += 2;
                return null;
            case 'x':
                return Code(pattern, ref index, form, digits: 2, start);
            case 'u' when index + 2 < pattern.Length && pattern[index + 2] == '{':
                return Refused("badcode", @"\u{", start);
            case 'u':
                return Code(pattern, ref index, form, digits: 4, start);
            default:
                if (IdentityEscaped.Contains(escaped, StringComparison.Ordinal))
                {
                    form.Append(construct);
                    index += 2;
                    return null;
                }

                return Refused("unknownescape", construct, start);
        }
    }

    /// <summary>`\x` or `\u` and exactly its hex digits, as written; anything short of them is refused.</summary>
    private static Refusal? Code(string pattern, ref int index, StringBuilder form, int digits, int start)
    {
        var end = index + 2 + digits;
        if (end > pattern.Length || !pattern[(index + 2)..end].All(char.IsAsciiHexDigit))
        {
            return Refused("badcode", pattern.Substring(start, 2), start);
        }

        form.Append(pattern, index, end - index);
        index = end;
        return null;
    }

    private static Refusal Refused(string key, string construct, int start) => new(ByKey[key], construct, start + 1);

    /// <summary>A character by its code, as the engine's escape: written here rather than typed, so that no source of
    /// this table carries a line or paragraph separator as itself.</summary>
    private static string Code(int code) => @"\u" + code.ToString("X4", CultureInfo.InvariantCulture);
}
