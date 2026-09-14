using Jason.Runtime.Execution;
using Jason.Runtime.Hosting;

namespace Jason.Runtime.Tests.Execution;

public class TokenRedactorTests
{
    private const string Token = "tok-4f3a-canary-0123456789";

    [Fact]
    public void The_token_in_the_middle_of_a_line_is_masked()
    {
        var redactor = new TokenRedactor(new RuntimeSecrets(Token));

        Assert.Equal($"authorization: Bearer {TokenRedactor.Mask} ok", redactor.Redact($"authorization: Bearer {Token} ok"));
    }

    [Fact]
    public void Every_occurrence_goes_including_several_on_one_line()
    {
        var redactor = new TokenRedactor(new RuntimeSecrets(Token));

        var redacted = redactor.Redact($"{Token} and again {Token}");

        Assert.Equal($"{TokenRedactor.Mask} and again {TokenRedactor.Mask}", redacted);
        Assert.DoesNotContain(Token, redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_without_the_token_is_handed_back_unchanged()
    {
        var redactor = new TokenRedactor(new RuntimeSecrets(Token));

        Assert.Equal("nothing to see here", redactor.Redact("nothing to see here"));
        Assert.Equal(string.Empty, redactor.Redact(string.Empty));
    }

    [Fact]
    public void A_token_too_short_to_be_one_matches_nothing()
    {
        Assert.Equal("a b a", new TokenRedactor(new RuntimeSecrets("a")).Redact("a b a"));
        Assert.Equal("a b a", new TokenRedactor(new RuntimeSecrets(string.Empty)).Redact("a b a"));
    }

    [Fact]
    public void Matching_is_exact_rather_than_case_insensitive()
    {
        var redactor = new TokenRedactor(new RuntimeSecrets(Token));

        Assert.Equal(Token.ToUpperInvariant(), redactor.Redact(Token.ToUpperInvariant()));
    }
}
