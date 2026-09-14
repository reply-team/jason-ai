using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jason.Runtime.Persistence;

/// <summary>Enums are stored as snake_case text. Parsing is exact: unknown text fails instead of guessing.</summary>
public sealed class SnakeCaseEnumConverter<TEnum>() : ValueConverter<TEnum, string>(value => ToText[value], text => Parse(text))
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToText = Enum.GetValues<TEnum>().ToDictionary(v => v, v => SnakeCase.Convert(v.ToString()));
    private static readonly Dictionary<string, TEnum> FromText = ToText.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    public static string Format(TEnum value) => ToText[value];

    public static TEnum Parse(string text) =>
        FromText.TryGetValue(text, out var value) ? value : throw new InvalidOperationException($"'{text}' is not a stored {typeof(TEnum).Name} value.");
}

/// <summary>Schema-less JSON objects are stored as JSON text; the comparer makes EF Core see in-place edits.</summary>
public sealed class JsonObjectConverter() : ValueConverter<JsonObject, string>(
    node => node.ToJsonString(null),
    text => JsonNode.Parse(text, null, default(JsonDocumentOptions))!.AsObject());

public sealed class JsonObjectComparer() : ValueComparer<JsonObject>(
    (left, right) => JsonNode.DeepEquals(left, right),
    node => node.ToJsonString(null).GetHashCode(StringComparison.Ordinal),
    node => node.DeepClone().AsObject());

/// <summary>Journal values are any JSON at all — an object, an array, a scalar — so they map through JsonNode.</summary>
public sealed class JsonNodeConverter() : ValueConverter<JsonNode, string>(
    node => node.ToJsonString(null),
    text => JsonNode.Parse(text, null, default(JsonDocumentOptions))!);

public sealed class JsonNodeComparer() : ValueComparer<JsonNode>(
    (left, right) => JsonNode.DeepEquals(left, right),
    node => node.ToJsonString(null).GetHashCode(StringComparison.Ordinal),
    node => node.DeepClone());
