using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jason.Contracts.Plugins;

/// <summary>
/// A plugin's configuration in the envelope. Its keys are the person's own words, so the envelope's naming policy, which
/// would turn <c>chatId</c> into <c>chat_id</c>, stops at them; on the way in they are matched as a configuration key is,
/// without regard to case.
/// </summary>
public sealed class ConfigMapConverter : JsonConverter<IReadOnlyDictionary<string, string>>
{
    public override IReadOnlyDictionary<string, string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A plugin's configuration is an object of text.");
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var key = reader.GetString()!;
            reader.Read();
            map[key] = reader.TokenType == JsonTokenType.String
                ? reader.GetString()!
                : throw new JsonException("A plugin's configuration is an object of text.");
        }

        return map;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyDictionary<string, string> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        foreach (var (key, text) in value)
        {
            writer.WriteString(key, text);
        }

        writer.WriteEndObject();
    }
}
