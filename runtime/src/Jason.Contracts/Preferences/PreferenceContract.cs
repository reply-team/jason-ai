using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Operations;

namespace Jason.Contracts.Preferences;

/// <summary>
/// One named preference of the client: a structured fact a person states — who messages are written as, how the company
/// sounds — with a schema of its own, so that a skill can check that it is set and a screen can show it. A family of its
/// own, beside the canonical operations, the delivery contract and the events contract and never among them: a preference
/// is no work anybody performs, so no work item names one, no route carries one and no plugin is invoked for one.
/// </summary>
/// <remarks>
/// Parsed as strictly as the other families: every field is required and a field this build does not know is refused.
/// The value schema is held to the dialect by the tests rather than as the document loads, as the events contract's is:
/// checking the dialect compiles every pattern a schema writes, and the command-line client loads this assembly too.
/// A key's schema changes only by adding an optional field, which raises the document's version so a skill reading the
/// document knows which fields may be present; anything else is a new key.
/// </remarks>
public sealed record PreferenceContract(string Key, int Version, string Summary, JsonObject ValueSchema)
{
    /// <summary>The longest key: the column that holds it is this wide.</summary>
    public const int MaxKeyLength = 64;

    /// <summary>
    /// What one value may carry, measured over its canonical form as a role's note is: characters outside ASCII are
    /// escaped there, six bytes for a character UTF-8 spells in two.
    /// </summary>
    public const int MaxValueBytes = 64 * 1024;

    private const string Contract = "a named preference";

    private static readonly string[] Fields = ["key", "version", "summary", "value_schema"];

    /// <summary>
    /// Whether <paramref name="key"/> is spelled as a key is: lower-case words of ASCII letters, digits and underscores, each
    /// starting with a letter, joined by single dots — <c>tone_of_voice</c>, <c>sender.identity</c> — and at most
    /// <see cref="MaxKeyLength"/> characters.
    /// </summary>
    public static bool IsWellFormedKey(string? key)
    {
        if (string.IsNullOrEmpty(key) || key.Length > MaxKeyLength)
        {
            return false;
        }

        var wordStart = true;
        foreach (var character in key)
        {
            if (wordStart)
            {
                if (character is < 'a' or > 'z')
                {
                    return false;
                }

                wordStart = false;
            }
            else if (character == '.')
            {
                wordStart = true;
            }
            else if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '_'))
            {
                return false;
            }
        }

        return !wordStart;
    }

    /// <summary>
    /// One document, as published under <c>docs/contracts/preferences/&lt;key&gt;.json</c>. <paramref name="source"/> is the
    /// file's name, which has to be the key's: one document per key, so the file a reader opens is the key they asked about.
    /// </summary>
    public static PreferenceContract Parse(string source, JsonNode? document)
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

        var key = Text("key");
        if (!IsWellFormedKey(key))
        {
            throw new InvalidOperationException(
                $"{source}: `{key}` is not a preference key: lower-case words of letters, digits and underscores, each starting with a letter, joined by dots, at most {MaxKeyLength} characters.");
        }

        if (!string.Equals(source, key + ".json", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{source}: `{key}` is published in {source}, and a key's document is named for it: {key}.json.");
        }

        var version = Required("version") is JsonValue number && number.GetValueKind() == JsonValueKind.Number && number.TryGetValue(out int whole) && whole >= 1
            ? whole
            : throw new InvalidOperationException($"{source}: `version` is a whole number from 1.");

        var summary = Text("summary");

        var schema = Required("value_schema") is JsonObject described
            && described["type"] is JsonValue type && type.TryGetValue(out string? word) && word == "object"
            ? described
            : throw new InvalidOperationException($"{source}: `value_schema` describes a JSON object, which every value is.");

        return new PreferenceContract(key, version, summary, schema);
    }

    /// <summary>What is wrong with a value somebody is setting, against the key's published schema.</summary>
    public IReadOnlyList<SchemaProblem> CheckValue(JsonNode? value) => CheckValue(value, PatternTiming.Default);

    /// <summary>The same check, with the schema's patterns matched under <paramref name="timing"/>.</summary>
    public IReadOnlyList<SchemaProblem> CheckValue(JsonNode? value, PatternTiming timing) => SchemaValidator.Validate(value, ValueSchema, timing);
}
