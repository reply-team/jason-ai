using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.Plugins;

public class RedactorTests
{
    [Fact]
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
    public void An_overlapping_pair_masks_the_longer_value_whole()
    {
        var redactor = new Redactor(["abcdefgh", "abcdefghij"]);

        Assert.Equal(Redactor.Mask, redactor.Redact("abcdefghij"));
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
}
