using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jason.Contracts.Json;

/// <summary>Writes every timestamp as ISO-8601 UTC with millisecond precision and a <c>Z</c> suffix.</summary>
/// <remarks>
/// A text that is not a timestamp is refused as a <see cref="JsonException"/>, the way the serializer refuses every
/// other value it cannot read, and the refusal does not quote the text. The framework's own parse throws a
/// <see cref="FormatException"/> that does, and that is not the failure anything reading JSON looks for: it travels
/// past every place that says a payload could not be read by where the reader stopped, to whatever writes a failure
/// whole — and the value somebody typed goes with it.
/// </remarks>
public sealed class UtcDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString() ?? throw new JsonException("Expected an ISO-8601 timestamp.");
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : throw new JsonException("The value is not an ISO-8601 timestamp.");
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture));
}
