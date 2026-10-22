using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Cli;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// <c>--idempotency-key</c> on the two creating verbs. The CLI sends a key exactly when one is given, exactly as it was
/// typed — the runtime is what reads it — and makes none up: a key the caller did not choose is one its retry could
/// not repeat.
/// </summary>
public class IdempotencyKeyOptionTests
{
    private static readonly DateTimeOffset Moment = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Campaign_create_sends_no_key_unless_one_is_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "create", "--name", "Q3");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.False(JsonNode.Parse(cli.Sent!)!.AsObject().ContainsKey("idempotency_key"));
    }

    [Fact]
    public async Task Campaign_create_sends_the_key_as_typed()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("campaign", "create", "--name", "Q3", "--idempotency-key", " launch-q3 ");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.CampaignCreate, """{"name":"Q3","idempotency_key":" launch-q3 "}""");
    }

    [Fact]
    [Critical(Family.W8, "the CLI never derives a key")]
    public async Task Workitem_create_sends_no_key_unless_one_is_given_and_the_key_as_typed_when_it_is()
    {
        using (var plain = new CliRun())
        {
            Assert.Equal(ExitCodes.Success, await plain.RunAsync("workitem", "create", "cmp_A", "--kind", "ai_role", "--role", "researcher"));
            Assert.False(JsonNode.Parse(plain.Sent!)!.AsObject().ContainsKey("idempotency_key"));
        }

        using var keyed = new CliRun();
        var exit = await keyed.RunAsync("workitem", "create", "cmp_A", "--kind", "ai_role", "--role", "researcher", "--idempotency-key", " research-1 ");

        Assert.Equal(ExitCodes.Success, exit);
        keyed.AssertPosted(
            Operations.WorkItemCreate,
            """{"campaign_id":"cmp_A","kind":"ai_role","role":"researcher","idempotency_key":" research-1 "}""");
    }

    [Theory]
    [InlineData("campaign", "create", "--name", "Q4")]
    [InlineData("workitem", "create", "cmp_A", "--kind", "ai_role", "--role", "researcher")]
    public async Task A_key_already_used_by_a_different_request_is_a_refusal(params string[] verb)
    {
        const string Refused = """{"error":{"code":"idempotency_key_reused","message":"This idempotency key already names 'cmp_A', which a different request created; give a different request a key of its own.","retryable":false}}""";
        using var cli = new CliRun(Refused, status: HttpStatusCode.Conflict);

        var exit = await cli.RunAsync([.. verb, "--idempotency-key", "launch-q3"]);

        Assert.Equal(ExitCodes.ApiError, exit);
        Assert.Contains("idempotency_key_reused", cli.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("campaign")]
    [InlineData("workitem")]
    public async Task The_help_of_each_creating_verb_offers_the_option(string noun)
    {
        using var dir = new TempPaths();
        var output = new StringWriter();

        var exit = await CliApp.RunAsync([noun, "create", "--help"], new CliEnvironment(output, new StringWriter(), dir.Paths), TestContext.Current.CancellationToken);

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("--idempotency-key", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_shows_a_campaign_s_key_when_it_has_one_and_nothing_when_it_has_none()
    {
        var keyed = new CampaignDto("cmp_A", "Q3", CampaignStatus.Draft, "prj_A", "default", [], [], null, null, Moment, Moment, null, "launch-q3");
        using (var cli = new CliRun(JsonSerializer.Serialize(keyed, JasonJson.Options)))
        {
            Assert.Equal(ExitCodes.Success, await cli.RunAsync("campaign", "get", "cmp_A", "--human"));
            Assert.Contains("launch-q3", cli.Text, StringComparison.Ordinal);
            Assert.Contains("Key:", cli.Text, StringComparison.Ordinal);
        }

        using var plain = new CliRun(JsonSerializer.Serialize(keyed with { IdempotencyKey = null }, JasonJson.Options));
        Assert.Equal(ExitCodes.Success, await plain.RunAsync("campaign", "get", "cmp_A", "--human"));
        Assert.DoesNotContain("Key:", plain.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Human_mode_shows_a_work_item_s_key_when_it_has_one_and_nothing_when_it_has_none()
    {
        var keyed = new WorkItemDto(
            "wi_A", "cmp_A", null, WorkItemKind.AiRole, "researcher", null, null, WorkItemStatus.Created, true, 0, null, null, null,
            null, null, null, new ActorRef(ActorType.Human, null), [], null, null, 0, null, null, null, null, Moment, Moment, null,
            IdempotencyKey: "research-1");
        using (var cli = new CliRun(JsonSerializer.Serialize(keyed, JasonJson.Options)))
        {
            Assert.Equal(ExitCodes.Success, await cli.RunAsync("workitem", "get", "wi_A", "--human"));
            Assert.Contains("research-1", cli.Text, StringComparison.Ordinal);
            Assert.Contains("Key:", cli.Text, StringComparison.Ordinal);
        }

        using var plain = new CliRun(JsonSerializer.Serialize(keyed with { IdempotencyKey = null }, JasonJson.Options));
        Assert.Equal(ExitCodes.Success, await plain.RunAsync("workitem", "get", "wi_A", "--human"));
        Assert.DoesNotContain("Key:", plain.Text, StringComparison.Ordinal);
    }
}
