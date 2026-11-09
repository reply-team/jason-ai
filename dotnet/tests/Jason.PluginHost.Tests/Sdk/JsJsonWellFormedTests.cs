using Jason.PluginHost.Sdk;

namespace Jason.PluginHost.Tests.Sdk;

/// <summary>
/// <see cref="JsJson.WellFormed"/> read directly, on JSON text as it is written. The engine spells a lone half of a
/// surrogate pair as an escape in lowercase hex and a whole pair raw, so some spellings below never reach it from a
/// plugin's value; each is held here all the same, since the text is what the method is given. A half standing alone,
/// raw or escaped, becomes U+FFFD in the spelling it had; a whole pair, raw or escaped, is kept as written. The rows
/// build every escape from its hex digits, so what each one reads is the text the method sees.
/// </summary>
public sealed class JsJsonWellFormedTests
{
    private const char Backslash = (char)0x5C;

    private const char High = (char)0xD83D;

    private const char Low = (char)0xDE00;

    private const char Replacement = (char)0xFFFD;

    [Fact]
    public void An_escaped_high_then_an_escaped_low_are_kept_as_their_twelve_characters()
    {
        var json = Quoted(Escape("D83D") + Escape("DE00"));

        Assert.Equal(12, json.Length - 2);
        Assert.Equal(json, JsJson.WellFormed(json));
    }

    [Fact]
    public void A_lone_escaped_half_in_the_engine_s_lowercase_spelling_becomes_the_escape_of_the_replacement_character()
    {
        Assert.Equal(Quoted("a" + Escape("FFFD")), JsJson.WellFormed(Quoted("a" + Escape("d83d"))));
    }

    [Fact]
    public void An_escaped_backslash_followed_by_u_and_hex_is_kept_exactly()
    {
        var json = Quoted($"{Backslash}{Backslash}ud83d");

        Assert.Equal(json, JsJson.WellFormed(json));
    }

    [Fact]
    public void A_raw_whole_pair_is_kept()
    {
        var json = Quoted($"busy {High}{Low}");

        Assert.Equal(json, JsJson.WellFormed(json));
    }

    [Fact]
    public void Text_with_escapes_that_spell_no_surrogate_is_equal_to_the_input()
    {
        var json = Quoted($"{Escape("0001")}{Backslash}n{Backslash}\"{Escape("00e9")}");

        Assert.Equal(json, JsJson.WellFormed(json));
    }

    [Fact]
    public void A_key_holding_a_lone_half_has_it_replaced()
    {
        Assert.Equal($"{{{Quoted("k" + Escape("FFFD"))}:1}}", JsJson.WellFormed($"{{{Quoted("k" + Escape("d83d"))}:1}}"));
    }

    [Fact]
    public void A_raw_high_beside_an_escaped_low_has_both_halves_replaced()
    {
        Assert.Equal(Quoted(Replacement + Escape("FFFD")), JsJson.WellFormed(Quoted(High + Escape("de00"))));
    }

    [Fact]
    public void An_escaped_high_beside_a_raw_low_has_both_halves_replaced()
    {
        Assert.Equal(Quoted(Escape("FFFD") + Replacement), JsJson.WellFormed(Quoted(Escape("d83d") + Low)));
    }

    [Fact]
    public void A_reversed_pair_escaped_as_the_engine_writes_it_has_both_halves_replaced()
    {
        Assert.Equal(Quoted(Escape("FFFD") + Escape("FFFD")), JsJson.WellFormed(Quoted(Escape("de00") + Escape("d83d"))));
    }

    [Fact]
    public void A_reversed_pair_written_raw_has_both_halves_replaced()
    {
        Assert.Equal(Quoted($"{Replacement}{Replacement}"), JsJson.WellFormed(Quoted($"{Low}{High}")));
    }

    /// <summary>The six characters of a JSON escape: a backslash, <c>u</c> and the four hex digits as given.</summary>
    private static string Escape(string hex) => $"{Backslash}u{hex}";

    private static string Quoted(string text) => $"\"{text}\"";
}
