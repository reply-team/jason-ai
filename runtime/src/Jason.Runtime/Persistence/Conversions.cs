using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
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

/// <summary>A list of small objects stored as one JSON column: a decision's options, its references.</summary>
public sealed class JsonArrayConverter() : ValueConverter<JsonArray, string>(
    node => node.ToJsonString(null),
    text => JsonNode.Parse(text, null, default(JsonDocumentOptions))!.AsArray());

public sealed class JsonArrayComparer() : ValueComparer<JsonArray>(
    (left, right) => JsonNode.DeepEquals(left, right),
    node => node.ToJsonString(null).GetHashCode(StringComparison.Ordinal),
    node => node.DeepClone().AsArray());

/// <summary>Journal values are any JSON at all — an object, an array, a scalar — so they map through JsonNode.</summary>
public sealed class JsonNodeConverter() : ValueConverter<JsonNode, string>(
    node => node.ToJsonString(null),
    text => JsonNode.Parse(text, null, default(JsonDocumentOptions))!);

public sealed class JsonNodeComparer() : ValueComparer<JsonNode>(
    (left, right) => JsonNode.DeepEquals(left, right),
    node => node.ToJsonString(null).GetHashCode(StringComparison.Ordinal),
    node => node.DeepClone());

/// <summary>
/// A DTO stored as JSON text in its own column. The comparer works on the serialized form rather than on
/// record equality, because a record whose members include a list compares those by reference.
/// </summary>
public sealed class JsonTextConverter<T>() : ValueConverter<T, string>(
    value => JsonSerializer.Serialize(value, JasonJson.Options),
    text => JsonSerializer.Deserialize<T>(text, JasonJson.Options)!);

public sealed class JsonTextComparer<T>() : ValueComparer<T>(
    (left, right) => string.Equals(JsonSerializer.Serialize(left, JasonJson.Options), JsonSerializer.Serialize(right, JasonJson.Options), StringComparison.Ordinal),
    value => JsonSerializer.Serialize(value, JasonJson.Options).GetHashCode(StringComparison.Ordinal),
    value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JasonJson.Options), JasonJson.Options)!);

/// <summary>A list of strings stored as a JSON array. Order is part of the value: an entry command is a command line.</summary>
public sealed class StringListConverter() : ValueConverter<List<string>, string>(
    value => JsonSerializer.Serialize(value, JasonJson.Options),
    text => JsonSerializer.Deserialize<List<string>>(text, JasonJson.Options)!);

public sealed class StringListComparer() : ValueComparer<List<string>>(
    (left, right) => left!.SequenceEqual(right!),
    value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
    value => value.ToList());
