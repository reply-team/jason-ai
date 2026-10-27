using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jason.Contracts.Operations;

/// <summary>
/// The one line a person reads before deciding on an operation, written as text with <c>{/json-pointer}</c>
/// placeholders into the composed input — the document the plugin would receive. There is nothing else: no escapes,
/// no conditionals and no logic, so what a template can say is exactly what its words and the input say.
/// </summary>
/// <remarks>
/// A value renders as follows: a string bare; a number or a boolean as its JSON literal; null, or nothing at the
/// pointer, as <c>—</c>; an object or an array as compact JSON, its text left as written apart from a character
/// outside the Basic Multilingual Plane, which the JSON writes as an escape. Every control character in a rendered
/// value, either Unicode line separator and every format character become a space, one outside the Basic
/// Multilingual Plane included: the summary is one line read in the order it is written, and a value must not be
/// able to forge a second line or turn the first around.
/// </remarks>
public sealed class PreviewTemplate
{
    /// <summary>What a value that is null, or absent, renders as.</summary>
    public const string Nothing = "—";

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IReadOnlyList<Segment> _segments;

    private PreviewTemplate(string text, IReadOnlyList<Segment> segments)
    {
        Text = text;
        _segments = segments;
        Pointers = [.. segments.Where(segment => segment.Pointer is not null).Select(segment => segment.Pointer!.Text)];
    }

    /// <summary>The template as the document writes it.</summary>
    public string Text { get; }

    /// <summary>Every placeholder's pointer, in the order the template uses them.</summary>
    public IReadOnlyList<string> Pointers { get; }

    /// <summary>Reads a template: literal text, and placeholders that each hold one JSON pointer.</summary>
    /// <exception cref="FormatException">
    /// The template is blank, or a brace is anything but the start or the end of a placeholder holding a pointer.
    /// </exception>
    public static PreviewTemplate Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("A preview's summary says something; this one is blank.");
        }

        var segments = new List<Segment>();
        var literal = new StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '}')
            {
                throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"`}}` at {index} closes no placeholder."));
            }

            if (character != '{')
            {
                literal.Append(character);
                continue;
            }

            var close = text.IndexOf('}', index + 1);
            var reopen = text.IndexOf('{', index + 1);
            if (close < 0 || (reopen >= 0 && reopen < close))
            {
                throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"`{{` at {index} opens a placeholder that never closes."));
            }

            if (literal.Length > 0)
            {
                segments.Add(new Segment(literal.ToString(), null));
                literal.Clear();
            }

            segments.Add(new Segment(null, PreviewPointer.Parse(text[(index + 1)..close])));
            index = close;
        }

        if (literal.Length > 0)
        {
            segments.Add(new Segment(literal.ToString(), null));
        }

        return new PreviewTemplate(text, segments);
    }

    /// <summary>The line a person reads, rendered from the composed input.</summary>
    public string Render(JsonNode? input)
    {
        var line = new StringBuilder();
        foreach (var segment in _segments)
        {
            line.Append(segment.Pointer is null ? segment.Literal : Value(segment.Pointer.Resolve(input)));
        }

        return line.ToString();
    }

    /// <inheritdoc/>
    public override string ToString() => Text;

    private static string Value(JsonNode? node) => node switch
    {
        null => Nothing,
        JsonValue value when value.GetValueKind() == JsonValueKind.String => OneLine(value.GetValue<string>()),
        JsonValue value => value.ToJsonString(),
        _ => OneLine(Neutral(node).ToJsonString(Compact)),
    };

    /// <summary>
    /// A copy of an object or an array whose every string has been made one line before it is written as JSON — the
    /// encoder would otherwise escape some of those characters and pass others through, and a value is held to one
    /// rule whatever shape it has.
    /// </summary>
    private static JsonNode Neutral(JsonNode node) => node switch
    {
        JsonObject members => new JsonObject(members.Select(member =>
            KeyValuePair.Create(member.Key, member.Value is null ? null : Neutral(member.Value)))),
        JsonArray items => new JsonArray([.. items.Select(item => item is null ? null : Neutral(item))]),
        JsonValue value when value.GetValueKind() == JsonValueKind.String => JsonValue.Create(OneLine(value.GetValue<string>())),
        _ => node.DeepClone(),
    };

    private static string OneLine(string value)
    {
        var line = new StringBuilder(value.Length);
        var rest = value.AsSpan();
        while (!rest.IsEmpty)
        {
            var whole = Rune.DecodeFromUtf16(rest, out var character, out var length) == OperationStatus.Done;
            if (whole && ChangesTheLine(character))
            {
                line.Append(' ');
            }
            else
            {
                line.Append(rest[..length]);
            }

            rest = rest[length..];
        }

        return line.ToString();
    }

    /// <summary>
    /// A character that changes how the line around it reads rather than adding to it: a control character, the two
    /// Unicode line separators, and every format character — the bidirectional controls, the zero-width characters,
    /// the byte-order mark and the tag characters among them. A right-to-left override inside a person's name would
    /// turn the rest of the summary around. The line is read in whole characters rather than UTF-16 units, so a
    /// format character outside the Basic Multilingual Plane is caught too.
    /// </summary>
    public static bool ChangesTheLine(Rune character) => Rune.GetUnicodeCategory(character) is
        UnicodeCategory.Control
        or UnicodeCategory.LineSeparator
        or UnicodeCategory.ParagraphSeparator
        or UnicodeCategory.Format;

    private sealed record Segment(string? Literal, PreviewPointer? Pointer);
}

/// <summary>
/// A JSON pointer (RFC 6901) into the composed input, as a preview writes one: <c>/</c>-separated tokens, with
/// <c>~1</c> for a slash and <c>~0</c> for a tilde inside a token.
/// </summary>
public sealed class PreviewPointer
{
    private PreviewPointer(string text, IReadOnlyList<string> tokens)
    {
        Text = text;
        Tokens = tokens;
    }

    /// <summary>The pointer as written.</summary>
    public string Text { get; }

    /// <summary>The tokens it steps through, unescaped.</summary>
    public IReadOnlyList<string> Tokens { get; }

    /// <exception cref="FormatException">The text is not a pointer to a member.</exception>
    public static PreviewPointer Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.StartsWith('/'))
        {
            throw new FormatException($"`{text}` is not a JSON pointer: a pointer starts with `/`.");
        }

        var tokens = new List<string>();
        foreach (var raw in text[1..].Split('/'))
        {
            for (var index = raw.IndexOf('~', StringComparison.Ordinal); index >= 0; index = raw.IndexOf('~', index + 1))
            {
                if (index + 1 >= raw.Length || raw[index + 1] is not ('0' or '1'))
                {
                    throw new FormatException($"`{text}` is not a JSON pointer: `~` is written only as `~0` or `~1`.");
                }
            }

            tokens.Add(raw.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
        }

        return new PreviewPointer(text, tokens);
    }

    /// <summary>The node at this pointer, or null when the input holds nothing there (a JSON null included).</summary>
    public JsonNode? Resolve(JsonNode? input)
    {
        var node = input;
        foreach (var token in Tokens)
        {
            node = node switch
            {
                JsonObject members => members.TryGetPropertyValue(token, out var member) ? member : null,
                JsonArray items => Index(token) is { } index && index < items.Count ? items[index] : null,
                _ => null,
            };

            if (node is null)
            {
                return null;
            }
        }

        return node;
    }

    /// <summary>
    /// Why the input schema cannot hold anything at this pointer, or null when it can — or when it cannot tell. The
    /// walk steps through plain <c>properties</c> by name and <c>items</c> by index, and stops, accepting, at a
    /// <c>$ref</c> or at an <c>anyOf</c> or <c>allOf</c> that could define the member: what lies behind those is held by
    /// the preview fixtures instead, each of which renders the template against an input the schema accepts.
    /// </summary>
    public string? Unaddressable(JsonObject schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        JsonNode? node = schema;
        foreach (var token in Tokens)
        {
            if (node is not JsonObject step || step.ContainsKey("$ref"))
            {
                return null;
            }

            if (step["properties"] is JsonObject properties && properties.TryGetPropertyValue(token, out var member))
            {
                node = member;
                continue;
            }

            if (step["items"] is JsonObject items)
            {
                if (Index(token) is not { } index)
                {
                    return $"`{token}` is not an index, and the schema holds an array there";
                }

                if (step["maxItems"] is JsonValue most && most.TryGetValue(out int maximum) && index >= maximum)
                {
                    return string.Create(CultureInfo.InvariantCulture, $"index {index} is past the {maximum} item(s) the schema allows there");
                }

                node = items;
                continue;
            }

            if (step["additionalProperties"] is JsonValue additional && additional.GetValueKind() == JsonValueKind.False)
            {
                return $"`{token}` is not a member the schema allows there";
            }

            if (step.ContainsKey("anyOf") || step.ContainsKey("allOf"))
            {
                return null;
            }

            if (step["type"] is JsonValue type && type.TryGetValue(out string? scalar) && scalar is "string" or "integer" or "number" or "boolean" or "null")
            {
                return $"the schema holds a {scalar} there, which has no `{token}`";
            }

            return null;
        }

        return null;
    }

    /// <summary>A token that is an array index as RFC 6901 writes one: digits, with no leading zero.</summary>
    private static int? Index(string token) =>
        token.Length > 0
        && (token == "0" || token[0] != '0')
        && token.All(char.IsAsciiDigit)
        && int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : null;
}
