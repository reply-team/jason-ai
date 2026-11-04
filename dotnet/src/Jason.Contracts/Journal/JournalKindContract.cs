using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Journal;

/// <summary>Whether a line of a kind always names an entity, or names it only on some lines.</summary>
public enum JournalEntityPresence
{
    Always,
    Sometimes,
}

/// <summary>
/// Where a line names one entity: a JSON pointer into the line as <c>journal.list</c> serializes it — one of the four
/// entity columns, or a place inside <c>key</c>, <c>old</c> or <c>new</c> — whether every line names it, and whether the
/// pointer names many of them (an array of ids, or a member of every item of one, written <c>*</c>).
/// </summary>
public sealed record JournalKindEntity(string At, JournalEntityPresence Presence, bool Each);

/// <summary>
/// The contract of one kind the runtime reserves for its own journal lines: the release that first wrote it, the one
/// sentence of what it means, the entities a line names and where, and its body — the line's <c>key</c>, <c>old</c> and
/// <c>new</c> together — as a schema in the dialect <see cref="SchemaValidator"/> publishes. A family of its own beside
/// the operations, the delivery contract, the events contract, the named preferences and the reserved context keys, and
/// never among them: a journal line is a fact the runtime wrote, not work anybody performs.
/// </summary>
/// <remarks>
/// Parsed as strictly as the other families: every field is required and a field this build does not know is refused.
/// The body schema is held to the dialect by the tests rather than as the document loads, as the other families' are:
/// checking the dialect compiles every pattern a schema writes, and the command-line client loads this assembly too.
/// A body grows by optional fields only, each named in <see cref="FieldSince"/> with the release that added it, so the
/// schema holds every line of the kind the journal can hold, whichever release wrote it.
/// </remarks>
public sealed record JournalKindContract(
    string Kind,
    int Version,
    string Since,
    string Summary,
    bool Subscribable,
    IReadOnlyDictionary<string, JournalKindEntity> Entities,
    IReadOnlyDictionary<string, string> FieldSince,
    JsonObject Body)
{
    /// <summary>The longest kind: the column that holds a line's kind is this wide.</summary>
    public const int MaxKindLength = 64;

    /// <summary>The three members of a line a body is made of, every one present in every line, null where the line says nothing there.</summary>
    public static IReadOnlyList<string> BodyMembers { get; } = ["key", "old", "new"];

    /// <summary>
    /// The entities a line may name. The design's nine — a campaign, a project, a contact, a work item, an attempt, an
    /// approval, a role, a plugin and an account — and the six more a line already carries an id of, each with a read
    /// behind it: a decision, a report, an execution profile, a provider block, a provider events source and a backfill.
    /// </summary>
    public static IReadOnlyList<string> EntityNames { get; } =
    [
        "campaign", "project", "contact", "work_item", "attempt", "approval", "role", "plugin", "account",
        "decision", "report", "profile", "provider_block", "event_source", "backfill",
    ];

    /// <summary>The entity columns of a line, each with the one entity it names.</summary>
    public static IReadOnlyDictionary<string, string> Columns { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["/campaign_id"] = "campaign",
        ["/work_item_id"] = "work_item",
        ["/attempt_id"] = "attempt",
        ["/contact_id"] = "contact",
    };

    private const string Contract = "a journal kind's document";

    private static readonly string[] Fields = ["kind", "version", "since", "summary", "subscribable", "entities", "field_since", "body"];

    private static readonly string[] EntityFields = ["at", "presence", "each"];

    /// <summary>
    /// Whether <paramref name="kind"/> is spelled as a reserved kind is: lower-case ASCII letters, digits and underscores,
    /// starting with a letter, at most <see cref="MaxKindLength"/> characters.
    /// </summary>
    public static bool IsWellFormedKind(string? kind) =>
        !string.IsNullOrEmpty(kind)
        && kind.Length <= MaxKindLength
        && kind[0] is >= 'a' and <= 'z'
        && kind.All(character => character is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_');

    /// <summary>Whether <paramref name="version"/> is a release's version as <c>since</c> writes it: three whole numbers joined by dots, none with a leading zero.</summary>
    public static bool IsWellFormedVersion(string? version) => TryParseVersion(version, out _);

    /// <summary>Orders two well-formed versions by their numbers, not their spelling: <c>0.9.0</c> comes before <c>0.10.0</c>.</summary>
    public static int CompareVersions(string left, string right)
    {
        if (!TryParseVersion(left, out var a) || !TryParseVersion(right, out var b))
        {
            throw new ArgumentException("Both versions are three whole numbers joined by dots.");
        }

        return a.CompareTo(b);
    }

    /// <summary>
    /// One document, as published under <c>docs/contracts/journal/&lt;kind&gt;.json</c>. <paramref name="source"/> is the
    /// file's name, which has to be the kind's: one document per kind, so the file a reader opens is the kind they asked about.
    /// </summary>
    public static JournalKindContract Parse(string source, JsonNode? document)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (document is not JsonObject fields)
        {
            throw new InvalidOperationException($"{source}: {Contract} is a JSON object.");
        }

        foreach (var (name, _) in fields)
        {
            if (!Fields.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{source}: `{name}` is not part of {Contract}.");
            }
        }

        JsonNode Required(string name) =>
            fields.TryGetPropertyValue(name, out var node) && node is not null
                ? node
                : throw new InvalidOperationException($"{source}: `{name}` is missing, and absence is not a default.");

        string Text(string name) =>
            Required(name) is JsonValue value && value.GetValueKind() == JsonValueKind.String && value.TryGetValue(out string? text) && text.Trim().Length > 0
                ? text
                : throw new InvalidOperationException($"{source}: `{name}` is a string with something in it.");

        var kind = Text("kind");
        if (!IsWellFormedKind(kind))
        {
            throw new InvalidOperationException(
                $"{source}: `{kind}` is not a journal kind: lower-case letters, digits and underscores, starting with a letter, at most {MaxKindLength} characters.");
        }

        if (!string.Equals(source, kind + ".json", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{source}: `{kind}` is published in {source}, and a kind's document is named for it: {kind}.json.");
        }

        var version = Required("version") is JsonValue number && number.GetValueKind() == JsonValueKind.Number && number.TryGetValue(out int whole) && whole >= 1
            ? whole
            : throw new InvalidOperationException($"{source}: `version` is a whole number from 1.");

        var since = Text("since");
        if (!IsWellFormedVersion(since))
        {
            throw new InvalidOperationException($"{source}: `since` is the release that first wrote the kind, three whole numbers joined by dots such as 0.12.0, and `{since}` is not one.");
        }

        var summary = Text("summary");
        if (summary.Contains('\n', StringComparison.Ordinal) || summary.Contains('\r', StringComparison.Ordinal) || !summary.EndsWith('.'))
        {
            throw new InvalidOperationException($"{source}: `summary` is one sentence of what the line means, on one line, ending with a full stop.");
        }

        var subscribable = Required("subscribable") is JsonValue flag && flag.GetValueKind() is JsonValueKind.True or JsonValueKind.False
            ? flag.GetValue<bool>()
            : throw new InvalidOperationException($"{source}: `subscribable` is true or false.");

        var body = ReadBody(source, Required("body"));
        var entities = ReadEntities(source, Required("entities"), body);
        var fieldSince = FieldSinceOf(source, Required("field_since"), body, since);

        return new JournalKindContract(kind, version, since, summary, subscribable, entities, fieldSince, body);
    }

    /// <summary>What is wrong with a line's body against the kind's published schema; an empty list means it holds.</summary>
    public IReadOnlyList<SchemaProblem> CheckBody(string? key, JsonNode? old, JsonNode? updated) => CheckBody(key, old, updated, PatternTiming.Default);

    /// <summary>The same check, with the schema's patterns matched under <paramref name="timing"/>.</summary>
    public IReadOnlyList<SchemaProblem> CheckBody(string? key, JsonNode? old, JsonNode? updated, PatternTiming timing) =>
        SchemaValidator.Validate(BodyOf(key, old, updated), Body, timing);

    /// <summary>
    /// A line's body as one JSON object: <c>key</c>, <c>old</c> and <c>new</c>, every one present and JSON null where the
    /// line has nothing. Copies of the members, so a node that belongs to the line stays the line's.
    /// </summary>
    public static JsonObject BodyOf(string? key, JsonNode? old, JsonNode? updated) => new()
    {
        ["key"] = key is null ? null : JsonValue.Create(key),
        ["old"] = old?.DeepClone(),
        ["new"] = updated?.DeepClone(),
    };

    private static JsonObject ReadBody(string source, JsonNode node)
    {
        if (node is not JsonObject body
            || body["type"] is not JsonValue type || !type.TryGetValue(out string? word) || word != "object"
            || body["additionalProperties"] is not JsonValue closed || closed.GetValueKind() != JsonValueKind.False
            || body["properties"] is not JsonObject properties
            || body["required"] is not JsonArray required)
        {
            throw new InvalidOperationException(
                $"{source}: `body` is a closed JSON object — `type` object and `additionalProperties` false — with `properties` and `required` naming `key`, `old` and `new`.");
        }

        var named = properties.Select(property => property.Key).Order(StringComparer.Ordinal).ToList();
        var listed = required.Select(item => item is JsonValue value && value.TryGetValue(out string? name) ? name : null).Order(StringComparer.Ordinal).ToList();
        var members = BodyMembers.Order(StringComparer.Ordinal).ToList();
        if (!named.SequenceEqual(members) || !listed.SequenceEqual(members))
        {
            throw new InvalidOperationException(
                $"{source}: `body` describes exactly `key`, `old` and `new` and requires all three, a member the kind never carries being `{{\"type\": \"null\"}}`.");
        }

        return body;
    }

    private static Dictionary<string, JournalKindEntity> ReadEntities(string source, JsonNode node, JsonObject body)
    {
        if (node is not JsonObject named)
        {
            throw new InvalidOperationException($"{source}: `entities` is a JSON object, from an entity's name to where the line names it; `{{}}` where it names none.");
        }

        var entities = new Dictionary<string, JournalKindEntity>(StringComparer.Ordinal);
        foreach (var (name, value) in named)
        {
            if (!EntityNames.Contains(name, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"{source}: `{name}` is not an entity a line names; the entities are {string.Join(", ", EntityNames)}.");
            }

            if (value is not JsonObject place)
            {
                throw new InvalidOperationException($"{source}: `entities.{name}` is a JSON object: `at`, `presence` and, for many of them, `each`.");
            }

            foreach (var (field, _) in place)
            {
                if (!EntityFields.Contains(field, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException($"{source}: `entities.{name}.{field}` is not part of where a line names an entity.");
                }
            }

            var at = place["at"] is JsonValue pointer && pointer.TryGetValue(out string? text) && text.StartsWith('/')
                ? text
                : throw new InvalidOperationException($"{source}: `entities.{name}.at` is a JSON pointer into the line, starting with `/`.");

            var presence = place["presence"] is JsonValue said && said.TryGetValue(out string? word)
                ? word switch
                {
                    "always" => JournalEntityPresence.Always,
                    "sometimes" => JournalEntityPresence.Sometimes,
                    _ => throw new InvalidOperationException($"{source}: `entities.{name}.presence` is `always` or `sometimes`, and `{word}` is neither."),
                }
                : throw new InvalidOperationException($"{source}: `entities.{name}.presence` is `always` or `sometimes`.");

            var each = false;
            if (place.TryGetPropertyValue("each", out var many))
            {
                each = many is JsonValue flag && flag.GetValueKind() == JsonValueKind.True
                    ? true
                    : throw new InvalidOperationException($"{source}: `entities.{name}.each` is true where present; a line naming one of them leaves it out.");
            }

            if (Columns.TryGetValue(at, out var column))
            {
                if (column != name || each)
                {
                    throw new InvalidOperationException($"{source}: `{at}` is the column that names one {column}, so `entities.{name}` cannot point at it.");
                }
            }
            else
            {
                var segments = Segments(at);
                if (segments.Count < 1 || !BodyMembers.Contains(segments[0], StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{source}: `entities.{name}.at` is `{at}`, which is neither an entity column ({string.Join(", ", Columns.Keys)}) nor a place inside `key`, `old` or `new`.");
                }

                if (Resolve(body, segments) is not { Count: > 0 })
                {
                    throw new InvalidOperationException($"{source}: `entities.{name}.at` is `{at}`, and the body describes nothing there.");
                }

                if (segments.Contains("*", StringComparer.Ordinal) && !each)
                {
                    throw new InvalidOperationException($"{source}: `entities.{name}.at` is `{at}`, which names one in every item of an array, so it says `each`.");
                }
            }

            entities.Add(name, new JournalKindEntity(at, presence, each));
        }

        return entities;
    }

    private static Dictionary<string, string> FieldSinceOf(string source, JsonNode node, JsonObject body, string since)
    {
        if (node is not JsonObject named)
        {
            throw new InvalidOperationException($"{source}: `field_since` is a JSON object, from a place in the body to the release that added it; `{{}}` where nothing was added.");
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (pointer, value) in named)
        {
            var version = value is JsonValue text && text.TryGetValue(out string? word) && IsWellFormedVersion(word)
                ? word
                : throw new InvalidOperationException($"{source}: `field_since` names `{pointer}` with something other than a release's version.");

            if (CompareVersions(version, since) <= 0)
            {
                throw new InvalidOperationException($"{source}: `field_since` says `{pointer}` was added in {version}, which is not after {since}, when the kind was first written.");
            }

            var segments = pointer.StartsWith('/') ? Segments(pointer) : [];
            if (segments.Count < 2 || !BodyMembers.Contains(segments[0], StringComparer.Ordinal) || segments[^1] == "*")
            {
                throw new InvalidOperationException($"{source}: `field_since` names `{pointer}`, which is not a member of an object inside `key`, `old` or `new`.");
            }

            // A `*` stands for any index of an array, so the place before it is an array the body describes.
            for (var star = segments.IndexOf("*"); star > 0; star = segments.IndexOf("*", star + 1))
            {
                var arrays = Resolve(body, segments[..star]) ?? [];
                if (!arrays.Exists(schema => schema["items"] is JsonObject))
                {
                    throw new InvalidOperationException(
                        $"{source}: `field_since` names `{pointer}`, where `*` stands for any index of an array, and the body describes no array at `/{string.Join('/', segments.Take(star))}`.");
                }
            }

            var parents = Resolve(body, segments[..^1]) ?? [];
            var name = segments[^1];
            var holders = parents.Where(parent => parent["properties"] is JsonObject properties && properties.ContainsKey(name)).ToList();
            if (holders.Count == 0)
            {
                throw new InvalidOperationException($"{source}: `field_since` names `{pointer}`, and the body describes nothing there.");
            }

            if (holders.Exists(holder => holder["required"] is JsonArray required && required.Any(item => item is JsonValue listed && listed.TryGetValue(out string? member) && member == name)))
            {
                throw new InvalidOperationException($"{source}: `{pointer}` was added after the kind was first written, so a line of an earlier release lacks it and the body cannot require it.");
            }

            fields.Add(pointer, version);
        }

        return fields;
    }

    /// <summary>
    /// Every schema object the body gives the place <paramref name="segments"/> names, walking <c>properties</c>,
    /// <c>items</c> for a <c>*</c>, and into every branch of <c>anyOf</c> and <c>allOf</c> and every <c>$ref</c> into the
    /// body's own <c>$defs</c>; null when the body describes nothing there.
    /// </summary>
    private static List<JsonObject>? Resolve(JsonObject body, IReadOnlyList<string> segments)
    {
        List<JsonObject> current = [body];
        foreach (var segment in segments)
        {
            var next = new List<JsonObject>();
            foreach (var schema in Expand(body, current))
            {
                var child = segment == "*" ? schema["items"] : (schema["properties"] as JsonObject)?[segment];
                if (child is JsonObject found)
                {
                    next.Add(found);
                }
            }

            if (next.Count == 0)
            {
                return null;
            }

            current = next;
        }

        return Expand(body, current);
    }

    private static List<JsonObject> Expand(JsonObject body, List<JsonObject> schemas)
    {
        var all = new List<JsonObject>();
        var pending = new Stack<JsonObject>(schemas);
        while (pending.Count > 0 && all.Count < 256)
        {
            var schema = pending.Pop();
            if (all.Exists(seen => ReferenceEquals(seen, schema)))
            {
                continue;
            }

            all.Add(schema);
            foreach (var combinator in new[] { "anyOf", "allOf" })
            {
                if (schema[combinator] is JsonArray branches)
                {
                    foreach (var branch in branches.OfType<JsonObject>())
                    {
                        pending.Push(branch);
                    }
                }
            }

            if (schema["$ref"] is JsonValue reference && reference.TryGetValue(out string? target)
                && target.StartsWith("#/$defs/", StringComparison.Ordinal)
                && (body["$defs"] as JsonObject)?[target["#/$defs/".Length..]] is JsonObject defined)
            {
                pending.Push(defined);
            }
        }

        return all;
    }

    private static List<string> Segments(string pointer) =>
        [.. pointer[1..].Split('/').Select(segment => segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal))];

    private static bool TryParseVersion(string? version, out (int Major, int Minor, int Patch) parsed)
    {
        parsed = default;
        var parts = version?.Split('.');
        if (parts is not { Length: 3 })
        {
            return false;
        }

        var numbers = new int[3];
        for (var index = 0; index < 3; index++)
        {
            var part = parts[index];
            if (part.Length == 0 || part.Length > 9 || (part.Length > 1 && part[0] == '0') || !part.All(char.IsAsciiDigit)
                || !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out numbers[index]))
            {
                return false;
            }
        }

        parsed = (numbers[0], numbers[1], numbers[2]);
        return true;
    }
}
