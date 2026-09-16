using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;

namespace Jason.Runtime.Routing;

/// <summary>
/// The identity of a binding: <c>sha256:</c> over its canonical JSON — keys sorted recursively, compact, UTF-8 —
/// using the same algorithm a package digest uses, so a person reading an attempt row meets one kind of hash.
/// <para>
/// The reason an attempt records a hash rather than a copy: the same identity across attempts is what makes
/// "was this retried against a different account?" answerable, the resolved value is already in the journal and
/// in <c>route.list</c>, and the attempt row stays small and free of anything a vendor might later call
/// sensitive.
/// </para>
/// <para>
/// It is total. Whatever it is handed, it answers — the walk carries its own stack rather than the thread's, and
/// the two things no parser can produce (a depth past any writer's limit, a number JSON cannot spell) are
/// hashed rather than thrown over. A binding is refused at activation, where the route that carries it is named;
/// an identity is never the place to report one.
/// </para>
/// </summary>
public static class BindingIdentity
{
    /// <summary>
    /// Deep enough that no binding is ever refused by the writer: the walk below carries its own stack, so
    /// depth costs heap rather than the thread's, and a binding is bounded where it is validated, not here.
    /// </summary>
    private const int MaxDepth = 1_000_000;

    private static readonly JsonWriterOptions Canonical = new() { Indented = false, MaxDepth = MaxDepth };

    /// <summary>The binding's identity, or null when there is no binding — which is not the identity of an empty one.</summary>
    public static string? Of(JsonObject? binding) => Measure(binding)?.Identity;

    /// <summary>
    /// The canonical form written once, and both facts about it that anybody needs: how many bytes it is, and
    /// what it is called. They are answered together because the caller that bounds a binding is the caller that
    /// records its identity, and serialising the same object twice to learn two things about it is how the two
    /// numbers would eventually come to disagree.
    /// </summary>
    public static BindingMeasure? Measure(JsonObject? binding)
    {
        if (binding is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Canonical))
        {
            Write(writer, binding);
        }

        var bytes = buffer.Length;
        buffer.Position = 0;
        return new BindingMeasure(bytes, $"{PackageDigest.Algorithm}:{Convert.ToHexStringLower(SHA256.HashData(buffer))}");
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
/// One binding's canonical form, measured and named in the same pass: the bytes the protocol would have to carry
/// it in, and the <c>sha256:</c> an attempt records it as.
/// </summary>
public readonly record struct BindingMeasure(long Bytes, string Identity);
