using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// The <c>preference</c> verbs: one per operation. A value is given on the line or from a file, never both, and is sent as
/// it was written; whether it is right for its key is the runtime's to say, against the key's document.
/// </summary>
public class PreferenceCommandsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Set_sends_the_key_the_value_the_reason_and_the_person()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "preference", "set", "tone_of_voice", "--value", """{"summary":"Plain and warm.","formality":"informal"}""",
            "--reason", "the person's answer", "--actor", "human:Marta");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.PreferenceSet,
            """
            {"key":"tone_of_voice","value":{"summary":"Plain and warm.","formality":"informal"},
             "reason":"the person's answer","actor":{"type":"human","id":"Marta"}}
            """);
    }

    [Fact]
    public async Task Set_takes_the_value_from_a_file()
    {
        using var file = new TempFile("""{"senders":[{"name":"Marta Lind","gender_forms":"feminine"}]}""");
        using var cli = new CliRun();

        var exit = await cli.RunAsync("preference", "set", "sender.identity", "--value-file", file.Path, "--actor", "human:Marta");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.PreferenceSet,
            """{"key":"sender.identity","value":{"senders":[{"name":"Marta Lind","gender_forms":"feminine"}]},"actor":{"type":"human","id":"Marta"}}""");
    }

    [Fact]
    public async Task Set_takes_the_value_from_standard_input()
    {
        using var cli = new CliRun(standardInput: """{"summary":"Plain."}""");

        var exit = await cli.RunAsync("preference", "set", "tone_of_voice", "--value-file", "-", "--actor", "human:Marta");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.PreferenceSet, """{"key":"tone_of_voice","value":{"summary":"Plain."},"actor":{"type":"human","id":"Marta"}}""");
    }

    /// <summary>
    /// A value is replaced whole: one given twice is a caller who has lost track of which they meant, and choosing one for
    /// them would set the other aside without a word. None at all is not an empty value either: unset says that.
    /// </summary>
    [Theory]
    [InlineData("both")]
    [InlineData("neither")]
    [InlineData("a list")]
    public async Task Set_refuses_two_values_none_or_one_that_is_not_an_object(string shape)
    {
        using var file = new TempFile("""{"summary":"From the file."}""");
        using var cli = new CliRun();
        string[] args = shape switch
        {
            "both" => ["preference", "set", "tone_of_voice", "--value", """{"summary":"From the line."}""", "--value-file", file.Path],
            "neither" => ["preference", "set", "tone_of_voice"],
            _ => ["preference", "set", "tone_of_voice", "--value", """["Plain."]"""],
        };

        var exit = await cli.RunAsync(args);

        Assert.True(exit == ExitCodes.Usage, $"'{shape}' exited {exit}: {cli.Error}");
        Assert.Null(cli.Sent);
        Assert.Contains("--value", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unset_sends_the_key_the_reason_and_the_person()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("preference", "unset", "sender.identity", "--reason", "taken back", "--actor", "human:Marta");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.PreferenceUnset, """{"key":"sender.identity","reason":"taken back","actor":{"type":"human","id":"Marta"}}""");
    }

    [Fact]
    public async Task List_passes_the_page()
    {
        using var cli = new CliRun();

        await cli.RunAsync("preference", "list", "--limit", "1", "--cursor", "abc");

        cli.AssertPosted(Operations.PreferenceList, """{"limit":1,"cursor":"abc"}""");
    }

    /// <summary>A refusal the runtime answers is the runtime's to word: the verb prints it as it came, and exits 1.</summary>
    [Fact]
    public async Task A_refusal_is_printed_as_it_came_and_exits_1()
    {
        const string Refusal = """{"error":{"code":"preference_not_human","message":"An actor of type 'role' cannot set or unset a preference; a preference is a person's word.","retryable":false}}""";
        using var cli = new CliRun(Refusal, status: System.Net.HttpStatusCode.BadRequest);

        var exit = await cli.RunAsync("preference", "set", "tone_of_voice", "--value", """{"summary":"Loud."}""", "--actor", "role:copywriter");

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("preference_not_human", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_renders_one_preference_with_its_value()
    {
        var set = new PreferenceDto(
            "tone_of_voice", new JsonObject { ["summary"] = "Plain and warm." }, "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", 29,
            Moment, Moment, new ActorRef(ActorType.Human, "Marta"));
        using var cli = new CliRun(JsonSerializer.Serialize(set, JasonJson.Options));

        var exit = await cli.RunAsync("preference", "set", "tone_of_voice", "--value", """{"summary":"Plain and warm."}""", "--actor", "human:Marta", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Matches("(?m)^Key: +tone_of_voice", cli.Text);
        Assert.Matches("(?m)^Set: +2026-10-22 12:00:00 UTC", cli.Text);
        Assert.Matches("(?m)^By: +human:Marta", cli.Text);
        Assert.Matches("(?m)^Size: +29 bytes", cli.Text);
        Assert.Contains("\"summary\": \"Plain and warm.\"", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>What unset answers, and a key never set: the rendering says so in words rather than showing nothing.</summary>
    [Fact]
    public async Task Human_mode_says_a_key_is_not_set()
    {
        using var cli = new CliRun(JsonSerializer.Serialize(new PreferenceDto("sender.identity", null, null, null, null, null, null), JasonJson.Options));

        var exit = await cli.RunAsync("preference", "unset", "sender.identity", "--actor", "human:Marta", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Matches("(?m)^Key: +sender.identity", cli.Text);
        Assert.Matches("(?m)^Set: +no", cli.Text);
        Assert.DoesNotContain("{", cli.Text, StringComparison.Ordinal);
    }

    /// <summary>The listing carries every key and every value, so its rendering shows them all from that one answer.</summary>
    [Fact]
    public async Task Human_mode_renders_every_key_set_or_not_from_the_listing_alone()
    {
        var page = new Page<PreferenceDto>(
        [
            new PreferenceDto("sender.identity", null, null, null, null, null, null),
            new PreferenceDto("tone_of_voice", new JsonObject { ["summary"] = "Plain and warm." }, "sha256:ab", 29, Moment, Moment, new ActorRef(ActorType.Human, "Marta")),
        ],
            "dG9uZQ");
        using var cli = new CliRun(JsonSerializer.Serialize(page, JasonJson.Options));

        var exit = await cli.RunAsync("preference", "list", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Equal(1, cli.Requests);
        Assert.Matches("(?m)^sender\\.identity: not set\r?$", cli.Text);
        Assert.Matches("(?m)^tone_of_voice: set 2026-10-22 12:00:00 UTC by human:Marta\r?$", cli.Text);
        Assert.Contains("\"summary\": \"Plain and warm.\"", cli.Text, StringComparison.Ordinal);
        Assert.Contains("next cursor: dG9uZQ", cli.Text, StringComparison.Ordinal);
    }
}
