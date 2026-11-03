using System.Net;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;

namespace Jason.Runtime.Tests.Profiles;

/// <summary>
/// The command word a launched agent calls home with is one word of letters, digits, dots, dashes and underscores that
/// starts with a letter or a digit. It is put inside a permission rule on every host, so a character any host reads as
/// something else (a list separator, a command separator, a substitution) would make the rule a different rule. Held for
/// every host at the one place a profile is written.
/// </summary>
public class CliCommandShapeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("jason,shell,x")]
    [InlineData("jason;rm")]
    [InlineData("jason|cat")]
    [InlineData("jason&x")]
    [InlineData("jason{a}")]
    [InlineData("jason`x`")]
    [InlineData("jason$HOME")]
    [InlineData("jason<x")]
    [InlineData("jason=x")]
    [InlineData("jason!x")]
    [InlineData("jason~x")]
    [InlineData(".jason")]
    [InlineData("-jason")]
    [InlineData("_jason")]
    [InlineData("jasön")]
    [InlineData("/usr/local/bin/jason")]
    [InlineData("jason x")]
    [InlineData("jason*")]
    [InlineData("jason(x)")]
    [InlineData("..")]
    public async Task A_command_word_that_is_not_one_word_of_plain_characters_is_refused_at_create_and_at_update(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostErrorAsync(
            Operations.ProfileCreate,
            new { name = "refused-at-create", host = "claude_code", program = "claude", cli_command = word },
            HttpStatusCode.BadRequest,
            Ct);
        await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "refused-at-update", host = "claude_code", program = "claude", cli_command = "jason" },
            Ct);
        var updated = await api.PostErrorAsync(
            Operations.ProfileUpdate,
            new { name = "refused-at-update", cli_command = word },
            HttpStatusCode.BadRequest,
            Ct);

        Assert.Equal("cli_command", Assert.Single(created.Details!).Field);
        Assert.Equal("cli_command", Assert.Single(updated.Details!).Field);

        var stored = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileGet, new { name = "refused-at-update" }, Ct);
        Assert.Equal(1, stored.CurrentRevision);
        Assert.Equal("jason", stored.Revision.CliCommand);
    }

    [Theory]
    [InlineData("jason")]
    [InlineData("jason.exe")]
    [InlineData("my_jason-2")]
    [InlineData("9lives")]
    public async Task A_command_word_of_plain_characters_is_accepted_at_create_and_at_update(string word)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var created = await api.PostOkAsync<ExecutionProfileDto>(
            Operations.ProfileCreate,
            new { name = "plain", host = "claude_code", program = "claude", cli_command = "jason" },
            Ct);
        var updated = await api.PostOkAsync<ExecutionProfileDto>(Operations.ProfileUpdate, new { name = "plain", cli_command = word }, Ct);

        Assert.Equal("jason", created.Revision.CliCommand);
        Assert.Equal(word, updated.Revision.CliCommand);
    }
}
