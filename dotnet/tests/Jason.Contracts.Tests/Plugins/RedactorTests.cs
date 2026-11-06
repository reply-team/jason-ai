using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.Plugins;

public class RedactorTests
{
    [Fact]
    [Critical(Family.S3, "the redactor masks every occurrence")]
    public void Every_secret_it_knows_is_masked_wherever_it_appears()
    {
        var redactor = new Redactor(["first-secret-value", "second-secret-value"]);

        var redacted = redactor.Redact("a=first-secret-value b=second-secret-value a=first-secret-value");

        Assert.Equal($"a={Redactor.Mask} b={Redactor.Mask} a={Redactor.Mask}", redacted);
    }

    [Fact]
    public void A_value_too_short_to_be_a_secret_is_left_alone()
    {
        var redactor = new Redactor(["1234567"]);

        Assert.Equal("1234567 stays", redactor.Redact("1234567 stays"));
    }

    [Fact]
    public void Null_and_blank_values_are_ignored()
    {
        var redactor = new Redactor([null, string.Empty, "   ", "long-enough-secret"]);

        Assert.Equal($"x {Redactor.Mask} y", redactor.Redact("x long-enough-secret y"));
    }

    [Fact]
    [Critical(Family.S3, "no partial secret survives an overlap")]
    public void An_overlapping_pair_masks_the_longer_value_whole()
    {
        var redactor = new Redactor(["abcdefgh", "abcdefghij"]);

        Assert.Equal(Redactor.Mask, redactor.Redact("abcdefghij"));
    }

    /// <summary>
    /// What the host and the runtime redact is mostly a document a JSON writer spelled, and a writer spells these characters
    /// as escapes. The leaf is read back parsed, because a value in its escaped spelling is no more in the raw text masked
    /// than unmasked.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_secret_a_json_writer_escapes_is_masked_in_the_spelling_it_was_given(bool relaxed)
    {
        const string Secret = "to&ken+with\"quote-and-\u00E9-0123456789";
        var document = new JsonObject { ["said"] = "refused " + Secret + " twice " + Secret }.ToJsonString(Writer(relaxed));

        var redacted = new Redactor([Secret]).Redact(document);

        Assert.Equal($"refused {Redactor.Mask} twice {Redactor.Mask}", JsonNode.Parse(redacted)!["said"]!.GetValue<string>());
    }

    /// <summary>A plain secret has one spelling, so the document around it is touched nowhere else.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_plain_secret_has_one_spelling_and_the_rest_of_the_document_stands(bool relaxed)
    {
        const string Secret = "plain-ascii-secret-0123456789";
        var document = new JsonObject { ["said"] = Secret, ["other"] = "a&b" }.ToJsonString(Writer(relaxed));

        Assert.Equal(document.Replace(Secret, Redactor.Mask, StringComparison.Ordinal), new Redactor([Secret]).Redact(document));
    }

    /// <summary>The length rule reads the value itself: an escape does not make a short word long enough to mask.</summary>
    [Fact]
    public void A_short_value_stays_short_however_long_its_escaped_spelling_is()
    {
        var document = new JsonObject { ["said"] = "a&b+c" }.ToJsonString();

        Assert.Equal(document, new Redactor(["a&b+c"]).Redact(document));
    }

    /// <summary>A value UTF-8 cannot carry has no JSON spelling; it stays a secret in its own, and the redactor is still built.</summary>
    [Fact]
    public void A_secret_with_no_json_spelling_is_still_masked_in_its_own()
    {
        const string Secret = "broken-\uD800-surrogate-value";

        Assert.Equal($"x {Redactor.Mask}", new Redactor([Secret]).Redact("x " + Secret));
    }

    [Fact]
    public void The_empty_redactor_hands_text_back_unchanged()
    {
        Assert.Equal("nothing to hide", Redactor.None.Redact("nothing to hide"));
        Assert.Equal(string.Empty, Redactor.None.Redact(string.Empty));
        Assert.Equal(string.Empty, new Redactor(["long-enough-secret"]).Redact(string.Empty));
    }

    [Fact]
    public void Matching_is_exact_rather_than_case_insensitive()
    {
        var redactor = new Redactor(["long-enough-secret"]);

        Assert.Equal("LONG-ENOUGH-SECRET", redactor.Redact("LONG-ENOUGH-SECRET"));
    }

    private static JsonSerializerOptions? Writer(bool relaxed) =>
        relaxed ? new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping } : null;
}
