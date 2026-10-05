using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jason.Contracts.Configuration;

namespace Jason.Cli.Doctor;

/// <summary>
/// The user's settings file as a diagnostic bundle describes it: what it is made of — keys, types, lengths — and a
/// value only where <see cref="SettingsAllowlist"/> says the value may travel. What is typed into a settings file is
/// often a secret that was meant for another line, so a person who reads the description learns how the file is built
/// and never what it says.
/// </summary>
/// <remarks>
/// <para>
/// <b>Keys.</b> A key is printed as typed when it may travel (<see cref="SettingKey"/>: a name the settings, the
/// operations or the installed plugins already hold, a public id, or an index into a list) and otherwise as a
/// placeholder, <c>&lt;key&gt;</c>, numbered <c>&lt;key 2&gt;</c>, <c>&lt;key 3&gt;</c> and so on when one object has
/// several, because a key is text a person typed and a token pasted where a name belonged is still a token. Every
/// member is described, whatever its key is called, but a value is carried only where every key on the way to it was
/// printed as typed. An object of more than sixty keys is a map somebody filled and not a record: only how many keys it
/// has is said, and nothing of what is inside it.
/// </para>
/// <para>
/// <b>Values.</b> A string is its length in code points, whether it holds markup, how many line breaks it has and how
/// many characters outside ASCII — or, when it is a timestamp, the digit pattern of one, <c>dddd-dd-ddTdd:dd:ddZ</c>. A
/// number, a boolean and a null are their type. A list is its length and one shape for all its items. Only a value the
/// allowlist carries is written out, and it is written beside the description, never in place of it. A list of records
/// is never a setting the allowlist names, so nothing inside one is carried. A count that is zero — of markup, of line
/// breaks, of characters outside ASCII — is left out rather than written as 0.
/// </para>
/// <para>
/// <b>Parsing</b> accepts comments and a comma before a closing bracket, as the runtime's JSON provider does, and
/// nesting to the depth it allows. A file that does not parse is described by the line and column the parser stopped
/// at, counted from 1 as an editor counts — the column in bytes, as the runtime says it — and by nothing the parser
/// said, because its message quotes the text it stopped at. A file that is valid JSON and not an object is described
/// as what it is: the runtime refuses it, and the shape says so.
/// </para>
/// </remarks>
public static partial class ConfigShape
{
    /// <summary>The most keys an object may have for them to be printed: more is a map somebody filled, not a record.</summary>
    private const int MaxKeys = 60;

    /// <summary>The character a byte order mark decodes to, at the start of a file some editors save with one.</summary>
    private const char ByteOrderMark = '\uFEFF';

    /// <summary>What the runtime's own provider reads: comments skipped, a trailing comma allowed.</summary>
    private static readonly JsonDocumentOptions ProviderTolerance = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // An ISO-8601 time with a zone: the whole value, not a line of it — a trailing line break makes it text.
    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}(:[0-9]{2})?(\.[0-9]+)?(Z|[+-][0-9]{2}:?[0-9]{2})?\z", RegexOptions.CultureInvariant)]
    private static partial Regex Timestamp();

    // Markup is a less-than sign followed by what starts a tag, a closing tag or a comment: not "a < b", not "<3".
    [GeneratedRegex(@"<[A-Za-z/!]", RegexOptions.CultureInvariant)]
    private static partial Regex Markup();

    [GeneratedRegex(@"^-?(0|[1-9][0-9]*)\z", RegexOptions.CultureInvariant)]
    private static partial Regex Integer();

    [GeneratedRegex("[0-9]", RegexOptions.CultureInvariant)]
    private static partial Regex Digit();

    /// <summary>
    /// Describes the settings file whose text this is: <c>{exists, bytes, parses, error?, shape?}</c>. Never throws, and
    /// never contains a word of the text except those the allowlist carries.
    /// </summary>
    /// <param name="text">The file's text, or null when there is no such file.</param>
    /// <param name="list">Which values may be written out.</param>
    /// <param name="installedPluginIds">
    /// The plugins installed in this data directory as they are now: the only ids that are printed as keys.
    /// </param>
    public static JsonObject Describe(string? text, SettingsAllowlist list, IReadOnlySet<string> installedPluginIds) =>
        Describe(text, list, installedPluginIds, out _);

    /// <summary>
    /// The same description, and the settings whose literal values it carries: each path once, as the file spells it, in
    /// the order the file has them, and a list's items as the list's path and <c>[]</c>, as the allowlist publishes it.
    /// The decision is the description's own, made where each value is written, so the two cannot disagree.
    /// </summary>
    public static JsonObject Describe(string? text, SettingsAllowlist list, IReadOnlySet<string> installedPluginIds, out IReadOnlyList<string> carriedPaths)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(installedPluginIds);

        carriedPaths = [];
        if (text is null)
        {
            return new JsonObject { ["exists"] = false, ["bytes"] = 0, ["parses"] = false };
        }

        // Parsed from its bytes, so that a character that cannot be encoded becomes the replacement character rather
        // than an exception, and without a byte order mark: the runtime reads the file through a reader that drops
        // one, and the parser itself takes it for the start of something that is not JSON.
        var size = Encoding.UTF8.GetByteCount(text);
        var utf8 = Encoding.UTF8.GetBytes(text.StartsWith(ByteOrderMark) ? text[1..] : text);
        var described = new JsonObject { ["exists"] = true, ["bytes"] = size };
        try
        {
            using var document = JsonDocument.Parse(utf8, ProviderTolerance);
            var walker = new Walker(list, installedPluginIds);
            var shape = walker.Node(document.RootElement, string.Empty, carry: true);
            described["parses"] = true;
            described["shape"] = shape;
            carriedPaths = walker.Carried;
        }
        catch (JsonException failure)
        {
            described["parses"] = false;
            described["error"] = new JsonObject
            {
                // The parser counts from zero; an editor counts from one, and so does the runtime's own report.
                ["line"] = failure.LineNumber + 1,
                ["column"] = failure.BytePositionInLine + 1,
            };
        }

        return described;
    }

    /// <summary>
    /// One piece of text described the way a string in the settings file is — its length, whether it holds markup, its
    /// line breaks and characters outside ASCII, or the digit pattern of a timestamp — and never a word of it. For a
    /// setting that arrives as text rather than as JSON, as an environment variable's value does.
    /// </summary>
    public static JsonObject DescribeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Walker.DescribeString(text);
    }

    private static string TypeOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => Integer().IsMatch(element.GetRawText()) ? "integer" : "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => "null",
    };

    private sealed class Walker(SettingsAllowlist list, IReadOnlySet<string> installedPluginIds)
    {
        private const string Unreadable = "\uFFFD";

        /// <summary>The settings a value was written for, in the order they were written; a list's items as the list's path and <c>[]</c>.</summary>
        public List<string> Carried { get; } = [];

        /// <param name="path">
        /// The setting as the configuration system spells it, from the file's first key to this one, with each key the
        /// code does not know written as <see cref="SettingKey.Hidden"/>.
        /// </param>
        /// <param name="carry">
        /// Whether every key on the way here was printed as typed, which is the only place a value may be carried from.
        /// </param>
        public JsonObject Node(JsonElement element, string path, bool carry) => element.ValueKind switch
        {
            JsonValueKind.Object => DescribeObject(element, path, carry),
            JsonValueKind.Array => DescribeArray(element, path, carry),
            _ => DescribeScalar(element, path, carry),
        };

        private JsonObject DescribeScalar(JsonElement element, string path, bool carry)
        {
            var described = element.ValueKind == JsonValueKind.String
                ? DescribeString(TextOf(element))
                : new JsonObject { ["type"] = TypeOf(element) };

            if (carry && Literal(element) is { } literal && list.Carries(path, literal))
            {
                described["value"] = literal;
                Carried.Add(path);
            }

            return described;
        }

        /// <summary>
        /// The value as a node the allowlist can judge, or nothing for a null. Built from the value itself rather than
        /// from the document, so that it outlives the document it was read from.
        /// </summary>
        private static JsonNode? Literal(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => JsonValue.Create(TextOf(element)),
            JsonValueKind.Number => JsonNode.Parse(element.GetRawText()),
            JsonValueKind.True => JsonValue.Create(true),
            JsonValueKind.False => JsonValue.Create(false),
            _ => null,
        };

        private JsonObject DescribeObject(JsonElement element, string path, bool carry)
        {
            var count = element.GetPropertyCount();
            var described = new JsonObject { ["type"] = "object", ["key_count"] = count };

            if (count > MaxKeys)
            {
                described["members_not_shown"] = "more_than_60_keys";
                return described;
            }

            // Keys are matched without regard to case, so a second spelling of one key is the key written twice. Only
            // the first is described: the runtime refuses the file, and a value written second never travels.
            var members = new JsonObject();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = 0;
            var hidden = 0;
            foreach (var property in element.EnumerateObject())
            {
                var name = NameOf(property);
                if (!seen.Add(name))
                {
                    duplicates++;
                    continue;
                }

                // A key the code does not know is printed as a placeholder, and stands as one in the path its own
                // members are judged by, so that no number beneath it is taken for an index and no value beneath it
                // is carried: a value travels only where every key on the way to it was printed as typed.
                var shown = MayTravel(path, name);
                members[shown ? name : KeyPlaceholder.For(++hidden)] =
                    Node(property.Value, Join(path, shown ? name : SettingKey.Hidden), carry && shown);
            }

            if (duplicates > 0)
            {
                described["duplicate_keys"] = duplicates;
            }

            described["members"] = members;
            return described;
        }

        private JsonObject DescribeArray(JsonElement element, string path, bool carry)
        {
            var items = element.EnumerateArray().ToList();
            var described = new JsonObject { ["type"] = "array", ["length"] = items.Count };
            if (items.Count == 0)
            {
                return described;
            }

            described["item_types"] = TypeCounts(items);

            var objects = items.Where(item => item.ValueKind == JsonValueKind.Object).ToList();
            if (objects.Count > 0)
            {
                described["items"] = Union(objects, Join(path, "0"));
            }

            var strings = items.Where(item => item.ValueKind == JsonValueKind.String).Select(item => TextOf(item)).ToList();
            if (strings.Count > 0)
            {
                described["strings"] = Population(strings);
            }

            var arrays = items.Where(item => item.ValueKind == JsonValueKind.Array).ToList();
            if (arrays.Count > 0)
            {
                described["array_lengths"] = Lengths(arrays);
            }

            // The items the allowlist carries, each once, in the order of their text. Nothing carries an item of a
            // list that is not on the list, so most lists have none.
            var carried = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (carry && Literal(item) is { } literal && list.Carries(path + "[]", literal))
                {
                    carried.TryAdd(literal.ToJsonString(), literal);
                }
            }

            if (carried.Count > 0)
            {
                described["values"] = new JsonArray([.. carried.Values]);
                Carried.Add(path + "[]");
            }

            return described;
        }

        /// <summary>
        /// One shape for the objects of a list: for each key, how many have it, how many hold null there, and which types.
        /// A key the code does not know is a placeholder, and the same unknown key in several objects is one member: that
        /// shows the objects share a key, and never which. No value is carried from a list of records.
        /// </summary>
        private JsonObject Union(List<JsonElement> objects, string path)
        {
            var described = new JsonObject { ["objects"] = objects.Count };

            var maps = new List<Dictionary<string, JsonElement>>();
            var order = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in objects)
            {
                if (item.GetPropertyCount() > MaxKeys)
                {
                    described["members_not_shown"] = "more_than_60_keys";
                    return described;
                }

                var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in item.EnumerateObject())
                {
                    var name = NameOf(property);
                    map.TryAdd(name, property.Value);
                    if (seen.Add(name))
                    {
                        order.Add(name);
                    }
                }

                maps.Add(map);
            }

            if (order.Count > MaxKeys)
            {
                described["members_not_shown"] = "more_than_60_keys";
                return described;
            }

            var members = new JsonObject();
            var hidden = 0;
            foreach (var key in order)
            {
                var shown = MayTravel(path, key);
                var present = maps.Where(map => map.ContainsKey(key)).Select(map => map[key]).ToList();
                var values = present.Where(value => value.ValueKind != JsonValueKind.Null).ToList();
                var types = values.Select(TypeOf).Distinct().Order(StringComparer.Ordinal).Select(type => (JsonNode)JsonValue.Create(type)!);
                var member = new JsonObject
                {
                    ["present"] = present.Count,
                    ["null"] = present.Count - values.Count,
                    ["types"] = new JsonArray([.. types]),
                };

                var strings = values.Where(value => value.ValueKind == JsonValueKind.String).Select(value => TextOf(value)).ToList();
                if (strings.Count > 0)
                {
                    member["strings"] = Population(strings);
                }

                var nested = values.Where(value => value.ValueKind == JsonValueKind.Object).ToList();
                if (nested.Count > 0)
                {
                    member["objects"] = Union(nested, Join(path, shown ? key : SettingKey.Hidden));
                }

                var arrays = values.Where(value => value.ValueKind == JsonValueKind.Array).ToList();
                if (arrays.Count > 0)
                {
                    member["array_lengths"] = Lengths(arrays);
                }

                members[shown ? key : KeyPlaceholder.For(++hidden)] = member;
            }

            described["members"] = members;
            return described;
        }

        /// <summary>
        /// Whether a key under this path is printed as typed, by the project's one key rule. The rule reads a whole
        /// path, so the key is judged in place: a number is an index only where a list's own path stands before it. A key
        /// written like a placeholder is never one the code knows, whatever the rule says of a word spelled that way.
        /// </summary>
        private bool MayTravel(string parent, string key)
        {
            if (KeyPlaceholder.Is(key))
            {
                return false;
            }

            var shown = SettingKey.ShownPath(Join(parent, key), installedPluginIds).Split(':');
            var own = key.Split(':').Length;
            return string.Equals(string.Join(':', shown[^own..]), key, StringComparison.Ordinal);
        }

        private static string Join(string parent, string key) => parent.Length == 0 ? key : parent + ":" + key;

        /// <summary>
        /// A string's text. One that cannot be read as text — an escape for half of a surrogate pair, which the parser
        /// accepts and no string holds — is the replacement character, which is also what a file's bytes read as when
        /// they are not text.
        /// </summary>
        private static string TextOf(JsonElement element)
        {
            try
            {
                return element.GetString() ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                return Unreadable;
            }
        }

        private static string NameOf(JsonProperty property)
        {
            try
            {
                return property.Name;
            }
            catch (InvalidOperationException)
            {
                return Unreadable;
            }
        }

        private static JsonObject TypeCounts(List<JsonElement> items)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var type = TypeOf(item);
                counts[type] = counts.GetValueOrDefault(type) + 1;
            }

            var described = new JsonObject();
            foreach (var (type, count) in counts)
            {
                described[type] = count;
            }

            return described;
        }

        private static JsonArray Lengths(List<JsonElement> arrays) =>
            new([.. arrays.Select(array => array.GetArrayLength()).Order().Select(length => (JsonNode)JsonValue.Create(length))]);

        internal static JsonObject DescribeString(string text)
        {
            if (Timestamp().IsMatch(text))
            {
                return new JsonObject { ["type"] = "string", ["timestamp"] = Pattern(text) };
            }

            var described = new JsonObject { ["type"] = "string", ["length"] = CodePoints(text) };
            if (Markup().IsMatch(text))
            {
                described["html"] = true;
            }

            if (LineBreaks(text) is > 0 and var breaks)
            {
                described["line_breaks"] = breaks;
            }

            if (OutsideAscii(text) is > 0 and var wide)
            {
                described["non_ascii"] = wide;
            }

            return described;
        }

        /// <summary>
        /// One description for many strings: the shortest and the longest, how many hold markup, line breaks or
        /// characters outside ASCII — or, when every one of them is a timestamp, the patterns that occur.
        /// </summary>
        private static JsonObject Population(List<string> strings)
        {
            if (strings.All(text => Timestamp().IsMatch(text)))
            {
                var patterns = strings.Select(Pattern).Distinct().Order(StringComparer.Ordinal).Select(pattern => (JsonNode)JsonValue.Create(pattern)!);
                return new JsonObject { ["timestamps"] = new JsonArray([.. patterns]) };
            }

            var lengths = strings.Select(CodePoints).ToList();
            var described = new JsonObject { ["length_min"] = lengths.Min(), ["length_max"] = lengths.Max() };
            if (strings.Count(text => Markup().IsMatch(text)) is > 0 and var markup)
            {
                described["with_html"] = markup;
            }

            if (strings.Count(text => LineBreaks(text) > 0) is > 0 and var broken)
            {
                described["with_line_breaks"] = broken;
            }

            if (strings.Count(text => OutsideAscii(text) > 0) is > 0 and var wide)
            {
                described["with_non_ascii"] = wide;
            }

            return described;
        }

        private static string Pattern(string timestamp) => Digit().Replace(timestamp, "d");

        private static int CodePoints(string text) => text.EnumerateRunes().Count();

        private static int OutsideAscii(string text) => text.EnumerateRunes().Count(rune => rune.Value > 0x7F);

        /// <summary>A line break is a line feed, a carriage return and line feed together, or a carriage return alone.</summary>
        private static int LineBreaks(string text)
        {
            var breaks = 0;
            for (var at = 0; at < text.Length; at++)
            {
                if (text[at] == '\n' || (text[at] == '\r' && (at + 1 == text.Length || text[at + 1] != '\n')))
                {
                    breaks++;
                }
            }

            return breaks;
        }
    }
}
