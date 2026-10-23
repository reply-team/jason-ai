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
    public static CanonicalMeasure? Measure(JsonObject? document) => Measure(document, inUtf8: false);

    /// <summary>
    /// The same form, named the same, and measured as the text it holds: its bytes in UTF-8 with nothing escaped, so a
    /// character costs what UTF-8 spells it in — one byte in ASCII, two or three in most scripts, four outside the Basic
    /// Multilingual Plane — rather than the six or twelve of its escape. The size a store's cap is compared with, so the
    /// same words in Cyrillic are not refused where their English twin passes; never more than <see cref="Measure(JsonObject?)"/>.
    /// </summary>
    public static CanonicalMeasure? MeasureInUtf8(JsonObject? document) => Measure(document, inUtf8: true);

    private static CanonicalMeasure? Measure(JsonObject? document, bool inUtf8)
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

        var form = buffer.GetBuffer().AsSpan(0, (int)buffer.Length);
        var bytes = inUtf8 ? Utf8Length(form) : form.Length;
        return new CanonicalMeasure(bytes, $"{PackageDigest.Algorithm}:{Convert.ToHexStringLower(SHA256.HashData(form))}");
    }

    /// <summary>What this document is called: the hash alone, for a caller that does not care how long it is.</summary>
    public static string Hash(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Measure(document)!.Value.Hash;
    }

    /// <summary>
    /// How long the escaped form is once every escape is counted as the UTF-8 of what it spells. Outside a string JSON
    /// has no backslash, so each one starts an escape: <c>\uXXXX</c> for one UTF-16 unit, two of them for a character
    /// outside the Basic Multilingual Plane, or a backslash and one letter. Every other byte is counted as itself. Half of
    /// a surrogate pair, which only code can build, counts the three bytes UTF-8 writes in its place.
    /// </summary>
    private static long Utf8Length(ReadOnlySpan<byte> form)
    {
        long length = 0;
        var at = 0;
        while (at < form.Length)
        {
            if (form[at] != (byte)'\\' || at + 1 >= form.Length)
            {
                length++;
                at++;
            }
            else if (!Unit(form, at, out var unit))
            {
                length++;
                at += 2;
            }
            else if (char.IsHighSurrogate(unit) && Unit(form, at + 6, out var low) && char.IsLowSurrogate(low))
            {
                length += 4;
                at += 12;
            }
            else
            {
                length += unit < 0x80 ? 1 : unit < 0x800 ? 2 : 3;
                at += 6;
            }
        }

        return length;
    }

    /// <summary>Whether a <c>\uXXXX</c> escape starts at <paramref name="at"/>, and the UTF-16 unit it spells.</summary>
    private static bool Unit(ReadOnlySpan<byte> form, int at, out char unit)
    {
        unit = default;
        if (at + 6 > form.Length || form[at] != (byte)'\\' || form[at + 1] != (byte)'u'
            || !ushort.TryParse(form.Slice(at + 2, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        unit = (char)value;
        return true;
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

/// <summary>
/// The canonical form of one document, measured and named in the same pass: <see cref="Bytes"/> as the function that
/// measured it counts them, escaped or in UTF-8, and <see cref="Hash"/> over the escaped form either way.
/// </summary>
public readonly record struct CanonicalMeasure(long Bytes, string Hash);
