using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jason.Contracts.Json;

/// <summary>
/// The one JSON dialect of the Runtime API and the CLI: snake_case members, enums as snake_case strings,
/// ISO-8601 UTC timestamps, nulls written explicitly, compact output.
/// </summary>
public static class JasonJson
{
    public static JsonSerializerOptions Options { get; } = Apply(new JsonSerializerOptions());

    public static JsonSerializerOptions Apply(JsonSerializerOptions target)
    {
        target.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        target.DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower;
        target.PropertyNameCaseInsensitive = true;
        target.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        target.WriteIndented = false;
        target.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        target.Converters.Add(new UtcDateTimeOffsetConverter());
        return target;
    }
}
