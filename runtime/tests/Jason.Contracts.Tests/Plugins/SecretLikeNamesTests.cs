using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.Plugins;

/// <summary>
/// The rule is "secret-like", and the fragment list is how it is asked rather than what it says. A name is
/// therefore read with its separators removed: <c>x-api-key</c> is the header everybody writes, and it was
/// getting through only because the list happens to spell that name with an underscore.
/// </summary>
public class SecretLikeNamesTests
{
    /// <summary>The spellings of one name. None of them is a different name, and none of them is allowed.</summary>
    [Theory]
    [InlineData("x-api-key")]
    [InlineData("API-KEY")]
    [InlineData("api.key")]
    [InlineData("api key")]
    [InlineData("api_key")]
    [InlineData("apikey")]
    [InlineData("X-Api-Key")]
    public void Every_spelling_of_one_credential_name_reads_as_the_same_name(string name) =>
        Assert.True(SecretLikeNames.Matches(name), $"'{name}' is the name the rule exists for.");

    /// <summary>The same normalisation, asked of the rest of the vocabulary rather than of one entry.</summary>
    [Theory]
    [InlineData("private-key")]
    [InlineData("client.secret")]
    [InlineData("auth-token")]
    [InlineData("Set-Cookie")]
    [InlineData("pass-word")]
    public void A_separator_never_makes_a_credential_name_into_another_name(string name) =>
        Assert.True(SecretLikeNames.Matches(name), $"'{name}' is one of the listed fragments with punctuation in it.");

    /// <summary>
    /// What a binding is actually for. Dropping separators must not start refusing the names an installation
    /// legitimately selects an account with — the rule reads like a credential, not like a word with a dash in it.
    /// </summary>
    [Theory]
    [InlineData("workspace")]
    [InlineData("account-id")]
    [InlineData("mailbox")]
    [InlineData("list.name")]
    [InlineData("sender_domain")]
    public void A_name_that_reads_like_nothing_of_the_sort_is_left_alone(string name) =>
        Assert.False(SecretLikeNames.Matches(name), $"'{name}' names an identity, not a credential.");
}
