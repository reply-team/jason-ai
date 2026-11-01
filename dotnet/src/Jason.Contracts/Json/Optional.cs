using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jason.Contracts.Json;

/// <summary>
/// A request field that tells "absent" apart from "present, possibly null" — the basis of partial patches:
/// an absent field leaves the entity unchanged, an explicit null clears it.
/// </summary>
[JsonConverter(typeof(OptionalJsonConverterFactory))]
public readonly record struct Optional<T>
{
    private Optional(T value, bool isSet)
    {
        Value = value;
        IsSet = isSet;
    }

    /// <summary>The field was not part of the request at all.</summary>
    public static Optional<T> Absent => default;

    /// <summary>The field was present, carrying <paramref name="value"/> — which may itself be null.</summary>
    public static Optional<T> Of(T value) => new(value, true);

    public bool IsSet { get; }

    public T Value { get; }
}

/// <summary>Binds every closed <see cref="Optional{T}"/> to a converter over its payload type.</summary>
public sealed class OptionalJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        typeToConvert is { IsGenericType: true } && typeToConvert.GetGenericTypeDefinition() == typeof(Optional<>);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(typeof(OptionalJsonConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]))!;
    }
}

internal sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    /// <summary>A present null must still mark the field as set.</summary>
    public override bool HandleNull => true;

    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Optional<T>.Of(JsonSerializer.Deserialize<T>(ref reader, options)!);

    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (value.IsSet)
        {
            JsonSerializer.Serialize(writer, value.Value, options);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
