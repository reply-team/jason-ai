using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Json;

/// <summary>
/// One canonical form for any JSON this runtime has to name: keys sorted recursively in ordinal order, compact,
/// UTF-8 — and a <c>sha256:</c> over it, spelled the way a package digest is. A route's binding and an approval's
/// subject are both named this way, so two documents that are the same are never given two different names.
/// <para>
/// It is total. Whatever it is handed, it answers — the walk carries its own stack rather than the thread's, and
/// the two things no parser can produce (a depth past any writer's limit, a number JSON cannot spell) are hashed
/// rather than thrown over. What a document may contain is refused where the document is validated; naming one
/// is never the place to report it.
/// </para>
/// </summary>
public static class CanonicalJson
{
    /// <summary>
    /// Deep enough that no document is ever refused by the writer: the walk below carries its own stack, so
    /// depth costs heap rather than the thread's, and a document is bounded where it is validated, not here.
    /// </summary>
    private const int MaxDepth = 1_000_000;

    private static readonly JsonWriterOptions Canonical = new() { Indented = false, MaxDepth = MaxDepth };

    /// <summary>The canonical form of one document, and the two facts anybody needs about it.</summary>
    public static CanonicalMeasure? Measure(JsonObject? document)
    {
        if (document is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Canonical))
        {
            Write(writer, document);
        }

        var bytes = buffer.Length;
        buffer.Position = 0;
        return new CanonicalMeasure(bytes, $"{PackageDigest.Algorithm}:{Convert.ToHexStringLower(SHA256.HashData(buffer))}");
    }

    /// <summary>What this document is called: the hash alone, for a caller that does not care how long it is.</summary>
    public static string Hash(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Measure(document)!.Value.Hash;
    }

    /// <summary>
    /// The canonical form, written with an explicit stack: an object's members in ordinal key order, an array's
    /// members in the order they were written, and every leaf as itself.
    /// </summary>
    private static void Write(Utf8JsonWriter writer, JsonNode? node)
    {
        var work = new Stack<object?>();
        work.Push(node);

        while (work.Count > 0)
        {
            switch (work.Pop())
            {
                case Closing closing:
                    if (closing.EndsAnObject)
                    {
                        writer.WriteEndObject();
                    }
                    else
                    {
                        writer.WriteEndArray();
                    }

                    break;

                case JsonObject value:
                    writer.WriteStartObject();
                    work.Push(Closing.OfObject);
                    foreach (var member in value.OrderByDescending(member => member.Key, StringComparer.Ordinal))
                    {
                        work.Push(member.Value);
                        work.Push(new Property(member.Key));
                    }

                    break;

                case JsonArray value:
                    writer.WriteStartArray();
                    work.Push(Closing.OfArray);
                    for (var index = value.Count - 1; index >= 0; index--)
                    {
                        work.Push(value[index]);
                    }

                    break;

                case Property property:
                    writer.WritePropertyName(property.Name);
                    break;

                case JsonValue value:
                    WriteValue(writer, value);
                    break;

                default:
                    writer.WriteNullValue();
                    break;
            }
        }
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonValue value)
    {
        try
        {
            value.WriteTo(writer);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
        {
            // A value JSON cannot spell — a non-finite number is the one that exists — which no parsed binding
            // can hold and only code can build. A scalar writer refuses before it writes anything, so the
            // document is still well formed; it is hashed as its own text rather than thrown over.
            writer.WriteStringValue(value.TryGetValue<double>(out var number)
                ? number.ToString("R", CultureInfo.InvariantCulture)
                : value.GetValueKind().ToString());
        }
    }

    private sealed record Property(string Name);

    private sealed record Closing(bool EndsAnObject)
    {
        public static Closing OfObject { get; } = new(true);

        public static Closing OfArray { get; } = new(false);
    }
}

/// <summary>The canonical form of one document, measured and named in the same pass.</summary>
public readonly record struct CanonicalMeasure(long Bytes, string Hash);
