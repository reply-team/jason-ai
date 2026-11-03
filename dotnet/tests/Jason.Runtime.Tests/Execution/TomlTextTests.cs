using Jason.Runtime.Execution.Hosts;

namespace Jason.Runtime.Tests.Execution;

public class TomlTextTests
{
    [Fact]
    public void Every_control_character_the_format_forbids_raw_is_written_as_its_escape()
    {
        // TOML's basic string may not hold U+0000 to U+001F or U+007F raw.
        for (var code = 0; code <= 0x1F; code++)
        {
            Assert.Equal("\"\\u" + code.ToString("X4") + "\"", TomlText.BasicString(((char)code).ToString()));
        }

        Assert.Equal("\"\\u007F\"", TomlText.BasicString("\u007F"));
    }

    [Fact]
    public void A_quote_and_a_backslash_are_escaped()
    {
        Assert.Equal("\"a\\\"b\\\\c\"", TomlText.BasicString("a\"b\\c"));
    }

    [Fact]
    public void A_letter_beyond_ascii_and_a_plain_word_are_kept_as_they_are()
    {
        Assert.Equal("\"na\u00EFve \u0416 \u65E5\u672C\"", TomlText.BasicString("na\u00EFve \u0416 \u65E5\u672C"));
        Assert.Equal("\"\"", TomlText.BasicString(""));
    }
}
