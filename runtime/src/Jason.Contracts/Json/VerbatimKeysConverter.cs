using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jason.Contracts.Json;

/// <summary>
/// A string map whose keys are somebody else's words, written and read exactly as they were given.
/// </summary>
/// <remarks>
/// The dialect spells Jason's own names in snake_case, and that policy reaches dictionary keys as well — which is
/// right for a map Jason names and wrong for one a provider does. There the key is evidence of what was said, and
/// a record that rewrites what it claims to have kept verbatim is worse than one that keeps nothing: a kind the
/// contract never declared is precisely the kind nothing else will ever mention again, so the spelling here is
/// the only copy of it there is.
/// </remarks>
public sealed class VerbatimKeysConverter : JsonConverter<IReadOnlyDictionary<string, string>>
{
    public override IReadOnlyDictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A map of identifiers is a JSON object.");
        }

        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return entries;
            }

            var key = reader.GetString()!;
            if (!reader.Read() || reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException($"The value under `{key}` is a string.");
            }

            entries[key] = reader.GetString()!;
        }

        throw new JsonException("A map of identifiers ended before it was closed.");
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        foreach (var (key, identifier) in value)
        {
            writer.WriteString(key, identifier);
        }

        writer.WriteEndObject();
    }
}
