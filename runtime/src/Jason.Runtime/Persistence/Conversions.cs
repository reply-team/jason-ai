using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jason.Runtime.Persistence;

/// <summary>
/// A converter that reads stored text and can be told which column it reads, so that text it cannot read is refused
/// by that column's name. The model names each one once it has named its columns.
/// </summary>
public interface IStoredColumnConverter
{
    /// <summary>The column this converter reads, as <c>table.column</c>.</summary>
    string Column { get; }

    /// <summary>The same converter, reading the named column.</summary>
    ValueConverter ForColumn(string column);
}

/// <summary>
/// How every stored value is read back: whatever the reader throws — a parser, a converter, a cast — becomes an
/// <see cref="UnreadableStoredValueException"/> naming the column, and the reader's own words go with it.
/// </summary>
internal static class StoredValue
{
    /// <summary>What a converter calls its column before the model has named it.</summary>
    public const string Unnamed = "an unnamed column";

    /// <summary>
    /// A key written twice is refused while the text is read, at any depth. Left to the default, it is accepted, and
    /// fails wherever the object is first looked inside — later, elsewhere, and in an error that names the key.
    /// </summary>
    private static readonly JsonDocumentOptions Documents = new() { AllowDuplicateProperties = false };

    public static JsonObject Object(string text, string column) => Read(column, text, static text => Node(text)!.AsObject());

    public static JsonArray Array(string text, string column) => Read(column, text, static text => Node(text)!.AsArray());

    public static JsonNode Any(string text, string column) => Read(column, text, static text => Node(text)!);

    public static T Typed<T>(string text, string column) => Read(column, text, static text =>
    {
        // The serializer hands a node inside a record on without reading it, so a key written twice there would pass
        // here and fail later: the text is read once as a document first, which refuses one at any depth.
        JsonDocument.Parse(text, Documents).Dispose();
        return JsonSerializer.Deserialize<T>(text, JasonJson.Options)!;
    });

    private static JsonNode? Node(string text) => JsonNode.Parse(text, null, Documents);

    private static T Read<T>(string column, string text, Func<string, T> read)
    {
        try
        {
            return read(text);
        }
        catch (Exception failure) when (failure is not UnreadableStoredValueException)
        {
            throw UnreadableStoredValueException.From(column, failure);
        }
    }
}

/// <summary>
/// Enums are stored as snake_case text. Parsing is exact: unknown text fails instead of guessing, and is not repeated
/// in the failure, because whoever edited the column could have typed anything there.
/// </summary>
public sealed class SnakeCaseEnumConverter<TEnum> : ValueConverter<TEnum, string>, IStoredColumnConverter
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToText = Enum.GetValues<TEnum>().ToDictionary(v => v, v => SnakeCase.Convert(v.ToString()));
    private static readonly Dictionary<string, TEnum> FromText = ToText.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    public SnakeCaseEnumConverter()
        : this(StoredValue.Unnamed)
    {
    }

    public SnakeCaseEnumConverter(string column)
        : base(value => ToText[value], text => Parse(text, column)) => Column = column;

    public string Column { get; }

    public ValueConverter ForColumn(string column) => new SnakeCaseEnumConverter<TEnum>(column);

    public static string Format(TEnum value) => ToText[value];

    public static TEnum Parse(string text) => Parse(text, StoredValue.Unnamed);

    public static TEnum Parse(string text, string column) =>
        FromText.TryGetValue(text, out var value) ? value : throw UnreadableStoredValueException.NotA(column, typeof(TEnum).Name);
}

/// <summary>Schema-less JSON objects are stored as JSON text; the comparer makes EF Core see in-place edits.</summary>
public sealed class JsonObjectConverter : ValueConverter<JsonObject, string>, IStoredColumnConverter
{
    public JsonObjectConverter()
        : this(StoredValue.Unnamed)
    {
    }

    public JsonObjectConverter(string column)
        : base(node => node.ToJsonString(null), text => StoredValue.Object(text, column)) => Column = column;

    public string Column { get; }

    public ValueConverter ForColumn(string column) => new JsonObjectConverter(column);
}

public sealed class JsonObjectComparer() : ValueComparer<JsonObject>(
    (left, right) => JsonNode.DeepEquals(left, right),
    node => node.ToJsonString(null).GetHashCode(StringComparison.Ordinal),
    node => node.DeepClone().AsObject());

/// <summary>A list of small objects stored as one JSON column: a decision's options, its references.</summary>
public sealed class JsonArrayConverter : ValueConverter<JsonArray, string>, IStoredColumnConverter
{
    public JsonArrayConverter()
        : this(StoredValue.Unnamed)
    {
    }

    public JsonArrayConverter(string column)
        : base(node => node.ToJsonString(null), text => StoredValue.Array(text, column)) => Column = column;

    public string Column { get; }

    public ValueConverter ForColumn(string column) => new JsonArrayConverter(column);
}

public sealed class JsonArrayComparer() : ValueComparer<JsonArray>(
    (left, right) => JsonNode.DeepEquals(left, right),
    node => node.ToJsonString(null).GetHashCode(StringComparison.Ordinal),
    node => node.DeepClone().AsArray());

/// <summary>Journal values are any JSON at all — an object, an array, a scalar — so they map through JsonNode.</summary>
public sealed class JsonNodeConverter : ValueConverter<JsonNode, string>, IStoredColumnConverter
{
    public JsonNodeConverter()
        : this(StoredValue.Unnamed)
    {
    }

    public JsonNodeConverter(string column)
        : base(node => node.ToJsonString(null), text => StoredValue.Any(text, column)) => Column = column;

    public string Column { get; }

    public ValueConverter ForColumn(string column) => new JsonNodeConverter(column);
}

public sealed class JsonNodeComparer() : ValueComparer<JsonNode>(
    (left, right) => JsonNode.DeepEquals(left, right),
    node => node.ToJsonString(null).GetHashCode(StringComparison.Ordinal),
    node => node.DeepClone());

/// <summary>
/// A DTO stored as JSON text in its own column. The comparer works on the serialized form rather than on
/// record equality, because a record whose members include a list compares those by reference.
/// </summary>
public sealed class JsonTextConverter<T> : ValueConverter<T, string>, IStoredColumnConverter
{
    public JsonTextConverter()
        : this(StoredValue.Unnamed)
    {
    }

    public JsonTextConverter(string column)
        : base(value => JsonSerializer.Serialize(value, JasonJson.Options), text => StoredValue.Typed<T>(text, column)) => Column = column;

    public string Column { get; }

    public ValueConverter ForColumn(string column) => new JsonTextConverter<T>(column);
}

public sealed class JsonTextComparer<T>() : ValueComparer<T>(
    (left, right) => string.Equals(JsonSerializer.Serialize(left, JasonJson.Options), JsonSerializer.Serialize(right, JasonJson.Options), StringComparison.Ordinal),
    value => JsonSerializer.Serialize(value, JasonJson.Options).GetHashCode(StringComparison.Ordinal),
    value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, JasonJson.Options), JasonJson.Options)!);

/// <summary>A list of strings stored as a JSON array. Order is part of the value: an entry command is a command line.</summary>
public sealed class StringListConverter : ValueConverter<List<string>, string>, IStoredColumnConverter
{
    public StringListConverter()
        : this(StoredValue.Unnamed)
    {
    }

    public StringListConverter(string column)
        : base(value => JsonSerializer.Serialize(value, JasonJson.Options), text => StoredValue.Typed<List<string>>(text, column)) => Column = column;

    public string Column { get; }

    public ValueConverter ForColumn(string column) => new StringListConverter(column);
}

public sealed class StringListComparer() : ValueComparer<List<string>>(
    (left, right) => left!.SequenceEqual(right!),
    value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
    value => value.ToList());
