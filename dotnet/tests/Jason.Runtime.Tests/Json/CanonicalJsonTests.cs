using System.Text;
using System.Text.Json.Nodes;
using Jason.Runtime.Json;

namespace Jason.Runtime.Tests.Json;

/// <summary>
/// The canonical form is measured two ways under one name. Its escaped bytes are what a route's binding and a report's
/// evidence are held to; the same form unescaped and counted in UTF-8 is what a named preference and a role's note are held
/// to, so a character costs what UTF-8 spells it in rather than the six bytes of its escape. The hash is over the escaped
/// form either way, so no stored hash and no line of the chronicle changes meaning.
/// </summary>
public class CanonicalJsonTests
{
    /// <summary>The escaped canonical form's sha256 of <see cref="Pinned"/>, computed before sizes were counted in UTF-8.</summary>
    internal const string PinnedHash = "sha256:194ca627d108f4400913b6dab0ae038a33a92d0687a5589c3793c981a55cb7b5";

    /// <summary>The same form unescaped, in UTF-8: 552 escaped bytes become 232.</summary>
    internal const long PinnedUtf8Bytes = 232;

    /// <summary>The empty member <c>{"t":""}</c> that every row of the theory below is measured inside.</summary>
    private const int Overhead = 8;

    /// <summary>
    /// The name was computed before the size was counted in UTF-8 and is pinned as it was: the escaped form's length and
    /// hash are what they were, and the UTF-8 measure of the same document carries the same hash.
    /// </summary>
    [Fact]
    [Critical(Family.X1, "a size counted in UTF-8 keeps the escaped form's name")]
    public void The_name_of_a_document_outside_ascii_is_the_hash_of_its_escaped_form_in_either_measure()
    {
        var escaped = CanonicalJson.Measure(Pinned())!.Value;
        var utf8 = CanonicalJson.MeasureInUtf8(Pinned())!.Value;

        Assert.Equal((552L, PinnedHash), (escaped.Bytes, escaped.Hash));
        Assert.Equal((PinnedUtf8Bytes, PinnedHash), (utf8.Bytes, utf8.Hash));
    }

    /// <summary>
    /// Each kind of character, escaped and in UTF-8. The escaped form spells everything outside ASCII as <c>\uXXXX</c>,
    /// a pair of them outside the Basic Multilingual Plane, and so DEL, the controls and seven ASCII marks; UTF-8 spells
    /// each as one to four bytes, never more than its escape.
    /// </summary>
    [Theory]
    [InlineData("a", 1, 1)]
    [InlineData("<", 6, 1)]
    [InlineData(">", 6, 1)]
    [InlineData("&", 6, 1)]
    [InlineData("'", 6, 1)]
    [InlineData("\"", 6, 1)]
    [InlineData("+", 6, 1)]
    [InlineData("`", 6, 1)]
    [InlineData("\u007F", 6, 1)]
    [InlineData("\u0001", 6, 1)]
    [InlineData("\u0080", 6, 2)]
    [InlineData("߿", 6, 2)]
    [InlineData("ࠀ", 6, 3)]
    [InlineData("￿", 6, 3)]
    [InlineData("\n", 2, 1)]
    [InlineData("\\", 2, 1)]
    [InlineData("é", 6, 2)]
    [InlineData("д", 6, 2)]
    [InlineData("€", 6, 3)]
    [InlineData("\U0001D11E", 12, 4)]
    [InlineData(@"\ABCD", 6, 5)]
    public void A_character_costs_what_utf8_spells_it_in(string character, int escapedBytes, int utf8Bytes)
    {
        var document = new JsonObject { ["t"] = character };

        Assert.Equal(Overhead + escapedBytes, CanonicalJson.Measure(document)!.Value.Bytes);
        Assert.Equal(Overhead + utf8Bytes, CanonicalJson.MeasureInUtf8(document)!.Value.Bytes);
    }

    /// <summary>A key is text as a value is, and is measured the same way.</summary>
    [Fact]
    public void A_key_outside_ascii_is_measured_in_utf8_too()
    {
        var document = new JsonObject { ["имя"] = 1 };
        var overhead = "{\"\":1}".Length;

        Assert.Equal(overhead + (3 * 6), CanonicalJson.Measure(document)!.Value.Bytes);
        Assert.Equal(overhead + (3 * 2), CanonicalJson.MeasureInUtf8(document)!.Value.Bytes);
    }

    /// <summary>In plain ASCII the two measures are one number: nothing is escaped, so nothing is counted differently.</summary>
    [Fact]
    public void A_document_in_plain_ascii_measures_the_same_either_way()
    {
        var document = new JsonObject
        {
            ["summary"] = "Plain and warm; short sentences; no exclamation marks.",
            ["examples"] = new JsonArray("Hi Alex, worth a short call next week? Best, Marta"),
            ["count"] = 3,
            ["ratio"] = 0.25,
            ["kept"] = true,
            ["gone"] = null,
            ["nested"] = new JsonObject { ["empty"] = new JsonArray() },
        };

        var escaped = CanonicalJson.Measure(document)!.Value;
        var utf8 = CanonicalJson.MeasureInUtf8(document)!.Value;

        Assert.Equal(escaped, utf8);
        Assert.Equal(Encoding.UTF8.GetByteCount(document.ToJsonString()), utf8.Bytes);
    }

    /// <summary>
    /// The measure is total, as the name is: a string no parser can produce — half of a surrogate pair, which only code
    /// can build — still answers, at no more than its escaped size, under the escaped form's hash.
    /// </summary>
    [Fact]
    public void Half_a_surrogate_pair_is_measured_rather_than_thrown_over()
    {
        var document = new JsonObject { ["t"] = "a\uD834b" };

        var escaped = CanonicalJson.Measure(document)!.Value;
        var utf8 = CanonicalJson.MeasureInUtf8(document)!.Value;

        Assert.Equal(escaped.Hash, utf8.Hash);
        Assert.True(utf8.Bytes <= escaped.Bytes, $"Measured {utf8.Bytes} bytes in UTF-8 and {escaped.Bytes} escaped.");
    }

    [Fact]
    public void No_document_has_no_measure()
    {
        Assert.Null(CanonicalJson.Measure(null));
        Assert.Null(CanonicalJson.MeasureInUtf8(null));
    }

    /// <summary>A tone of voice in Cyrillic with one character outside the Basic Multilingual Plane, its keys out of order.</summary>
    internal static JsonObject Pinned() => new()
    {
        ["summary"] = "Пишем просто и тепло, без восклицательных знаков.",
        ["formality"] = "informal",
        ["examples"] = new JsonArray("Привет, Марта! Видели ваш новый склад в Казани? \U0001D11E"),
    };

    /// <summary>
    /// What a store's cap counts for <paramref name="value"/>, worked out without the measure under test: the same document
    /// with every string emptied, which is plain ASCII and so the same size in either measure, and each string's own UTF-8.
    /// Its keys are taken to be plain ASCII, as every key a preference's schema names is.
    /// </summary>
    internal static long Utf8SizeOf(JsonObject value)
    {
        var skeleton = (JsonObject)value.DeepClone();
        var text = EmptyStrings(skeleton);
        return CanonicalJson.Measure(skeleton)!.Value.Bytes + text;
    }

    /// <summary>Empties every string under <paramref name="node"/> and answers how many UTF-8 bytes they held.</summary>
    private static long EmptyStrings(JsonNode? node)
    {
        long text = 0;
        if (node is JsonObject members)
        {
            foreach (var name in members.Select(member => member.Key).ToList())
            {
                if (Characters(members[name]) is { } characters)
                {
                    text += Encoding.UTF8.GetByteCount(characters);
                    members[name] = string.Empty;
                }
                else
                {
                    text += EmptyStrings(members[name]);
                }
            }
        }
        else if (node is JsonArray items)
        {
            for (var index = 0; index < items.Count; index++)
            {
                if (Characters(items[index]) is { } characters)
                {
                    text += Encoding.UTF8.GetByteCount(characters);
                    items[index] = string.Empty;
                }
                else
                {
                    text += EmptyStrings(items[index]);
                }
            }
        }

        return text;
    }

    private static string? Characters(JsonNode? node) => node is JsonValue leaf && leaf.TryGetValue(out string? characters) ? characters : null;
}
