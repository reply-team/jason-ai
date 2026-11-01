using System.Text;

namespace Jason.Contracts.Operations;

/// <summary>
/// The template standard the contracts README publishes: every construct a template may write, what becomes of it, and
/// the names it may use. The grammar is total — a template is accepted, or refused for the first construct met reading
/// from its start, with that construct's one named reason and the character it starts on. One pass over the text, no
/// regular expression. The tables are the ones the README publishes, and a test holds the two to each other row by row.
/// </summary>
internal static class TemplateGrammar
{
    /// <summary>What becomes of a construct.</summary>
    internal enum Kind
    {
        Accepted,
        Refused,
    }

    /// <summary>One row of the published table: the construct as the README writes it, the words a refusal names it by, and why.</summary>
    internal sealed record Row(string Key, string Construct, string Name, Kind Kind, string Why);

    /// <summary>A refused construct: its row, and the character it starts on (from 1).</summary>
    internal sealed record Refusal(Row Row, int Position);

    /// <summary>
    /// One name a template may use, what it stands for, and on each channel a template is written on what the composed
    /// input holds of it: the pointer the approval's example reads it at, or null where the input does not hold it and the
    /// provider fills it from what it holds.
    /// </summary>
    internal sealed record NameRow(string Name, string What, string? Email, string? Linkedin)
    {
        /// <summary>What the composed input holds of the name on <paramref name="channel"/>; on a channel no template is written on, nothing.</summary>
        internal string? On(string? channel) => channel switch
        {
            "email" => Email,
            "linkedin" => Linkedin,
            _ => null,
        };
    }

    /// <summary>The channels a template is written on, in the order the table of names lists them.</summary>
    internal static IReadOnlyList<string> Channels { get; } = ["email", "linkedin"];

    /// <summary>The deepest a condition may sit inside others.</summary>
    internal const int MaxDepth = 8;

    /// <summary>The longest key a custom field's name may carry after its prefix.</summary>
    private const int MaxCustomKey = 64;

    private const string CustomPrefix = "contact.custom.";

    /// <summary>The published table, in the order the README lists it.</summary>
    internal static IReadOnlyList<Row> Rows { get; } =
    [
        new("text", "any text no other row names", "text", Kind.Accepted, "sent as written; a single `{` or `}` is text too"),
        new("value", "`{{name}}`", "a value", Kind.Accepted, "the name's value; a name with no value leaves the provider nothing to write, and the message is not sent"),
        new("fallback", "`{{name | 'text'}}`", "a value with a fallback", Kind.Accepted, "the name's value, or the quoted text when it has none"),
        new("if", "`{{#if name}}` ... `{{/if}}`", "a condition", Kind.Accepted, "what it holds when the name has a value"),
        new("compare", "`{{#if name == 'text'}}`, `{{#if name != 'text'}}`", "a comparison", Kind.Accepted, "a condition on the value being, or not being, the quoted text"),
        new("else", "`{{else}}`", "the other branch", Kind.Accepted, "once inside a condition: what is written when it does not hold"),
        new("raw", "`{{{`", "a raw value", Kind.Refused, "a template escapes nothing, so there is no second kind of value"),
        new("comment", "`{{!`", "a comment", Kind.Refused, "nobody reads it, and the providers do not all drop it"),
        new("partial", "`{{>`", "a partial", Kind.Refused, "it names text the provider keeps, which nobody approving reads"),
        new("block", "`{{#each`, `{{#with`, `{{#unless`, any other `{{#` and any other `{{/`", "a block other than `#if`", Kind.Refused, "only `#if` is read alike by the providers"),
        new("elseif", "`{{else if`", "an `else if`", Kind.Refused, "the providers mapped first have none; write an `#if` inside `{{else}}`"),
        new("helper", "a word before a name, as `{{toLower name}}`, and a second `|`, as `{{Random | 'a' | 'b'}}`", "a helper or a macro", Kind.Refused, "it is a provider's own computation, which another provider does not have"),
        new("prompt", "`@prompt[[`", "a prompt to a model", Kind.Refused, "the provider would write in a model's answer, which nobody approved"),
        new("comparison", "`=`, `<>`, `<`, `>`, `<=`, `>=` inside a tag", "a comparison other than `==` and `!=`", Kind.Refused, "the providers mapped first read them differently, or not at all"),
        new("logic", "`&&`, `||` inside a tag", "a logical operator", Kind.Refused, "the providers mapped first have none; write one `#if` inside another"),
        new("variable", "a name after `|`, `==` or `!=`", "a name where a quoted text belongs", Kind.Refused, "a fallback and the right side of a comparison are quoted text, so what they say is what was approved"),
        new("name", "a name the table of names does not list", "a name this standard does not list", Kind.Refused, "the provider would fill it with a value of its own, or fail it"),
        new("quoted", "a `'` with no `'` after it in its tag, or one followed by anything but white space or `}}`", "a quoted text not closed", Kind.Refused, "a quoted text runs to the next `'`, so it holds neither `'` nor `}}`"),
        new("spacing", "a tag spaced otherwise, as `{{ name }}` or `{{name|'text'}}`", "a tag spaced otherwise than the standard writes it", Kind.Refused, "each tag has one spelling: one space between its parts and none inside the braces"),
        new("unknown", "any other tag, as `{{}}` or `{{#if}}`", "a tag this standard does not know", Kind.Refused, "the standard has no such tag"),
        new("unbalanced", "an `{{#if` never closed, a `{{/if}}` or `{{else}}` outside a condition, and a second `{{else}}`", "an unbalanced condition", Kind.Refused, "a provider would render it its own way, or refuse it"),
        new("depth", "an `{{#if` inside eight others", "a condition nested deeper than eight", Kind.Refused, "eight levels hold any message a person reads"),
        new("unclosed", "`{{` with no `}}` before the end or before the next `{{`", "a tag never closed", Kind.Refused, "a provider would read the text after it as part of the tag"),
        new("stray", "`}}` outside a tag, or a `}` right after one", "a stray closing brace", Kind.Refused, "a provider would read it as the end of a tag"),
        new("escaped", "`\\{{`", "an escaped brace", Kind.Refused, "the providers mapped first have no escape for `{{`, so a template cannot hold the two braces as text"),
    ];

    /// <summary>The names a template may use, in the order the README lists them.</summary>
    internal static IReadOnlyList<NameRow> Names { get; } =
    [
        new("contact.first_name", "the person's first name", "/contacts/0/first_name", "/contacts/0/first_name"),
        new("contact.last_name", "the person's last name", "/contacts/0/last_name", "/contacts/0/last_name"),
        new("contact.company", "the person's company", "/contacts/0/company", "/contacts/0/company"),
        new("contact.title", "the person's title", "/contacts/0/title", "/contacts/0/title"),
        new("contact.email", "the person's email address, which on an email is the address the message goes to", "/contacts/0/channels/0/value", null),
        new(CustomPrefix + "<key>", "the field the provider's account holds for the person under `<key>`: a letter or `_`, then at most 63 letters, digits and `_`", null, null),
        new("sender.first_name", "the sending account's first name, as the provider holds the account", null, null),
        new("sender.last_name", "the sending account's last name, as the provider holds the account", null, null),
        new("sender.name", "the sending account's full name, as the provider holds the account", null, null),
        new("sender.address", "the sending account's address, which on LinkedIn is its profile", "/args/sender/address", "/args/sender/address"),
    ];

    private const string Prompt = "@prompt[[";

    private static readonly Dictionary<string, Row> ByKey = Rows.ToDictionary(row => row.Key, StringComparer.Ordinal);

    private static readonly HashSet<string> FixedNames =
        Names.Select(row => row.Name).Where(name => !name.StartsWith(CustomPrefix, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);

    /// <summary>What an accepted template is made of.</summary>
    internal abstract record Node;

    /// <summary>Text, as written.</summary>
    internal sealed record Literal(string Text) : Node;

    /// <summary>`{{name}}`, or `{{name | 'text'}}` with its fallback.</summary>
    internal sealed record Value(string Name, string? Fallback) : Node;

    /// <summary>
    /// `{{#if name}}`, or a comparison with its operator and its quoted text, and what each branch holds — the other
    /// branch null when the condition writes no `{{else}}`.
    /// </summary>
    internal sealed record Condition(string Name, string? Operator, string? Text, IReadOnlyList<Node> Then, IReadOnlyList<Node>? Otherwise) : Node;

    /// <summary>
    /// The table as the contracts README publishes it, one line for each row after the header and its rule. A `|` in a
    /// cell is written `\|`, which a table cell needs and a code span inside one still shows as itself.
    /// </summary>
    internal static IReadOnlyList<string> Table()
    {
        var lines = new List<string>
        {
            "| Construct | Treatment | Why |",
            "|---|---|---|",
        };
        lines.AddRange(Rows.Select(row =>
            $"| {Escaped(row.Construct)} | {(row.Kind == Kind.Accepted ? "accepted" : "refused")} | {Escaped(row.Why)} |"));
        return lines;
    }

    /// <summary>The table of names as the contracts README publishes it.</summary>
    internal static IReadOnlyList<string> NamesTable()
    {
        var lines = new List<string>
        {
            "| Name | What it is | On an email | On LinkedIn |",
            "|---|---|---|---|",
        };
        lines.AddRange(Names.Select(row =>
            $"| `{row.Name}` | {Escaped(row.What)} | {string.Join(" | ", Channels.Select(channel => Held(row.On(channel))))} |"));
        return lines;
    }

    /// <summary>The first construct this standard refuses in <paramref name="template"/>, or null when it refuses none.</summary>
    internal static Refusal? Check(string template)
    {
        _ = Parse(template, out var refusal);
        return refusal;
    }

    /// <summary>
    /// The template as what it is made of, and, through <paramref name="refusal"/>, the first construct this standard
    /// refuses in it reading from the start — null when it refuses none; the nodes are then empty. One pass.
    /// </summary>
    internal static IReadOnlyList<Node> Parse(string template, out Refusal? refusal)
    {
        ArgumentNullException.ThrowIfNull(template);
        refusal = null;
        var root = new List<Node>();
        var open = new Stack<Frame>();
        var text = new StringBuilder();
        var index = 0;
        while (index < template.Length && refusal is null)
        {
            if (StartsAt(template, index, "\\{{"))
            {
                refusal = Refused("escaped", index);
            }
            else if (StartsAt(template, index, "}}"))
            {
                refusal = Refused("stray", index);
            }
            else if (StartsAt(template, index, Prompt))
            {
                refusal = Refused("prompt", index);
            }
            else if (StartsAt(template, index, "{{"))
            {
                Flush(text, Current(root, open));
                refusal = Tag(template, ref index, root, open);
            }
            else
            {
                text.Append(template[index]);
                index++;
            }
        }

        if (refusal is null)
        {
            Flush(text, Current(root, open));
            if (open.Count > 0)
            {
                // The stack lists its innermost condition first, so the last is the one opened first.
                refusal = Refused("unbalanced", open.Last().Start);
            }
        }

        return refusal is null ? root : [];
    }

    /// <summary>One tag, from its `{{` to its `}}`.</summary>
    private static Refusal? Tag(string template, ref int index, List<Node> root, Stack<Frame> open)
    {
        var start = index;
        var end = template.IndexOf("}}", start + 2, StringComparison.Ordinal);
        if (end < 0)
        {
            return Refused("unclosed", start);
        }

        var content = template[(start + 2)..end];
        if (content.StartsWith('{'))
        {
            return Refused("raw", start);
        }

        if (content.Contains("{{", StringComparison.Ordinal))
        {
            return Refused("unclosed", start);
        }

        if (content.StartsWith('!'))
        {
            return Refused("comment", start);
        }

        if (content.StartsWith('>'))
        {
            return Refused("partial", start);
        }

        var tokens = Tokenize(content, start + 2, out var canonical, out var problem);
        if (problem is not null)
        {
            return problem;
        }

        index = end + 2;
        var placed = Place(tokens, canonical, start, root, open);

        // A `}` right after the tag makes `}}}`, which a provider may read as a raw value's close, not as text.
        return placed is null && index < template.Length && template[index] == '}' ? Refused("stray", index) : placed;
    }

    /// <summary>What a tag says, once its parts are read: a condition opened, a branch, a close, or a value.</summary>
    private static Refusal? Place(List<Token> tokens, bool canonical, int start, List<Node> root, Stack<Frame> open)
    {
        if (tokens.Count == 0 || tokens[0].Kind != TokenKind.Word)
        {
            return Refused("unknown", start);
        }

        var word = tokens[0].Text;
        if (word == "#if")
        {
            if (tokens.Count == 1)
            {
                return Refused("unknown", start);
            }

            var condition = Expression(tokens, 1, inCondition: true, start, out var problem);
            if (problem is not null)
            {
                return problem;
            }

            if (Settled(canonical, condition.Name, start) is { } unsettled)
            {
                return unsettled;
            }

            if (open.Count == MaxDepth)
            {
                return Refused("depth", start);
            }

            open.Push(new Frame(start, condition.Name, condition.Operator, condition.Text));
            return null;
        }

        if (word.StartsWith('#') || word.StartsWith('/'))
        {
            if (word is "#" or "/" || (word == "/if" && tokens.Count > 1))
            {
                return Refused("unknown", start);
            }

            if (word != "/if")
            {
                return Refused("block", start);
            }

            if (!canonical)
            {
                return Refused("spacing", start);
            }

            if (open.Count == 0)
            {
                return Refused("unbalanced", start);
            }

            var closed = open.Pop();
            Current(root, open).Add(new Condition(closed.Name, closed.Operator, closed.Text, closed.Then, closed.Otherwise));
            return null;
        }

        if (word == "else")
        {
            if (tokens.Count > 1)
            {
                return Refused(tokens[1] is { Kind: TokenKind.Word, Text: "if" } ? "elseif" : "unknown", start);
            }

            if (!canonical)
            {
                return Refused("spacing", start);
            }

            if (open.Count == 0 || open.Peek().Otherwise is not null)
            {
                return Refused("unbalanced", start);
            }

            open.Peek().Otherwise = [];
            return null;
        }

        var value = Expression(tokens, 0, inCondition: false, start, out var refused);
        if ((refused ?? Settled(canonical, value.Name, start)) is { } refusal)
        {
            return refusal;
        }

        Current(root, open).Add(new Value(value.Name, value.Text));
        return null;
    }

    /// <summary>
    /// A name, alone or with what follows it: a fallback outside a condition, a comparison inside one. Whatever else
    /// follows the name is refused by what it is.
    /// </summary>
    private static (string Name, string? Operator, string? Text) Expression(
        List<Token> tokens, int at, bool inCondition, int start, out Refusal? problem)
    {
        problem = null;
        if (tokens[at].Kind != TokenKind.Word)
        {
            problem = Refused("unknown", start);
            return default;
        }

        var name = tokens[at].Text;
        if (tokens.Count == at + 1)
        {
            return (name, null, null);
        }

        var second = tokens[at + 1];
        string? key = second switch
        {
            { Kind: TokenKind.Word } => "helper",
            { Kind: TokenKind.Quoted } => "unknown",
            { Text: "&&" or "||" } => "logic",
            { Text: "=" or "<>" or "<" or ">" or "<=" or ">=" } => "comparison",
            { Text: "|" } when !inCondition => null,
            { Text: "==" or "!=" } when inCondition => null,
            _ => "unknown",
        };
        if (key is null)
        {
            key = tokens.Count == at + 2 ? "unknown" : tokens[at + 2] switch
            {
                { Kind: TokenKind.Word } => "variable",
                { Kind: TokenKind.Operator } => "unknown",
                _ when tokens.Count == at + 3 => null,
                _ => tokens[at + 3] switch
                {
                    { Kind: TokenKind.Operator, Text: "&&" or "||" } => "logic",
                    { Kind: TokenKind.Operator, Text: "|" } when !inCondition => "helper",
                    _ => "unknown",
                },
            };
        }

        if (key is not null)
        {
            problem = Refused(key, start);
            return default;
        }

        return (name, second.Text, tokens[at + 2].Text);
    }

    /// <summary>A tag whose shape stands: refused still when it is spaced otherwise, or names what the table does not list.</summary>
    private static Refusal? Settled(bool canonical, string name, int start) =>
        !canonical ? Refused("spacing", start)
        : !IsName(name) ? Refused("name", start)
        : null;

    /// <summary>Whether the table of names lists <paramref name="name"/>, a custom field's key checked character by character.</summary>
    internal static bool IsName(string name)
    {
        if (!name.StartsWith(CustomPrefix, StringComparison.Ordinal))
        {
            return FixedNames.Contains(name);
        }

        var key = name.AsSpan(CustomPrefix.Length);
        if (key.Length is 0 or > MaxCustomKey || !(char.IsAsciiLetter(key[0]) || key[0] == '_'))
        {
            return false;
        }

        foreach (var character in key)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character == '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A tag's parts: words, operators and quoted texts. Whether the tag is spaced as the standard writes it — one space
    /// between two parts and none inside the braces — comes back through <paramref name="canonical"/>; a quoted text not
    /// closed, or one holding a prompt, through <paramref name="problem"/>, at the character it starts on.
    /// </summary>
    private static List<Token> Tokenize(string content, int offset, out bool canonical, out Refusal? problem)
    {
        var tokens = new List<Token>();
        canonical = content.Length > 0 && !char.IsWhiteSpace(content[0]) && !char.IsWhiteSpace(content[^1]);
        problem = null;
        var separated = true;
        var index = 0;
        while (index < content.Length)
        {
            var character = content[index];
            if (char.IsWhiteSpace(character))
            {
                var run = index;
                while (index < content.Length && char.IsWhiteSpace(content[index]))
                {
                    index++;
                }

                canonical &= index - run == 1 && character == ' ';
                separated = true;
                continue;
            }

            canonical &= tokens.Count == 0 || separated;
            separated = false;
            var from = index;
            if (character == '\'')
            {
                var close = content.IndexOf('\'', index + 1);
                if (close < 0 || (close + 1 < content.Length && !char.IsWhiteSpace(content[close + 1])))
                {
                    problem = Refused("quoted", offset + index);
                    return tokens;
                }

                var quoted = content[(index + 1)..close];
                var prompt = quoted.IndexOf(Prompt, StringComparison.Ordinal);
                if (prompt >= 0)
                {
                    problem = Refused("prompt", offset + index + 1 + prompt);
                    return tokens;
                }

                tokens.Add(new Token(TokenKind.Quoted, quoted));
                index = close + 1;
            }
            else if (IsOperator(character))
            {
                while (index < content.Length && IsOperator(content[index]))
                {
                    index++;
                }

                tokens.Add(new Token(TokenKind.Operator, content[from..index]));
            }
            else
            {
                while (index < content.Length && !char.IsWhiteSpace(content[index]) && content[index] != '\'' && !IsOperator(content[index]))
                {
                    index++;
                }

                tokens.Add(new Token(TokenKind.Word, content[from..index]));
            }
        }

        return tokens;
    }

    private static bool IsOperator(char character) => character is '|' or '=' or '!' or '<' or '>' or '&';

    private static bool StartsAt(string template, int index, string text) =>
        index + text.Length <= template.Length && string.CompareOrdinal(template, index, text, 0, text.Length) == 0;

    private static List<Node> Current(List<Node> root, Stack<Frame> open) =>
        open.Count == 0 ? root : open.Peek().Otherwise ?? open.Peek().Then;

    private static void Flush(StringBuilder text, List<Node> into)
    {
        if (text.Length > 0)
        {
            into.Add(new Literal(text.ToString()));
            text.Clear();
        }
    }

    private static Refusal Refused(string key, int index) => new(ByKey[key], index + 1);

    /// <summary>What the table of names writes for what the input holds of a name on one channel.</summary>
    private static string Held(string? pointer) => pointer switch
    {
        null => "not held",
        _ => $"`{pointer}`",
    };

    private static string Escaped(string text) => text.Replace("|", @"\|", StringComparison.Ordinal);

    private enum TokenKind
    {
        Word,
        Operator,
        Quoted,
    }

    private sealed record Token(TokenKind Kind, string Text);

    /// <summary>A condition still open: where it starts, what it tests, and what each branch holds so far.</summary>
    private sealed class Frame(int start, string name, string? @operator, string? text)
    {
        public int Start { get; } = start;

        public string Name { get; } = name;

        public string? Operator { get; } = @operator;

        public string? Text { get; } = text;

        public List<Node> Then { get; } = [];

        public List<Node>? Otherwise { get; set; }
    }
}
