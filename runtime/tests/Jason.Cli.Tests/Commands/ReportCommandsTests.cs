using Jason.Cli;
using Jason.Contracts.Api;

namespace Jason.Cli.Tests.Commands;

/// <summary>
/// Telling Jason what you did elsewhere, from the side it is typed on: what goes on the wire, and what a person
/// reading the answer is told about how much of it Jason actually knows.
/// </summary>
public class ReportCommandsTests
{
    private const string OneReport = """
        {
          "id": "rpt_01K52JR0000000000000000001",
          "received_at": "2026-09-17T12:00:00+00:00",
          "reporter": {"type": "role", "id": "rol_01K52JR0000000000000000002"},
          "reason": "Reporting it the moment I noticed.",
          "dedup": {"outcome": "admitted", "matched": null},
          "assertion": {
            "effect": "email_sent",
            "tool": "some-other-cli 1.2.3",
            "account": "team@example.test",
            "summary": "A follow-up was sent by hand while the runtime was not involved.",
            "occurred_at": "2026-09-17T11:04:00Z",
            "evidence": {"subject": "Following up"},
            "unknown_fields": ["observed_at"],
            "uncertainty": "The send was queued; nobody watched it leave."
          },
          "assertion_hash": "sha256:abc",
          "correlation": {
            "campaign_id": "cmp_01K52JR0000000000000000003",
            "contact_id": "cnt_01K52JR0000000000000000004",
            "work_item_id": null,
            "operation": "carrier_pigeon.dispatch",
            "operation_known": false,
            "contact_in_campaign": false,
            "verified": false
          }
        }
        """;

    [Fact]
    public async Task Submit_sends_every_field_it_was_given()
    {
        using var cli = new CliRun(OneReport);

        var exit = await cli.RunAsync(
            "--actor", "role:rol_7",
            "report", "submit",
            "--effect", "email_sent",
            "--tool", "some-other-cli",
            "--summary", "Sent by hand.",
            "--provider", "reply",
            "--account", "team@example.test",
            "--occurred-at", "2026-09-17T11:04:00Z",
            "--campaign", "cmp_7",
            "--contact", "cnt_7",
            "--operation", "campaign.enroll",
            "--external-id", "message=m-1",
            "--external-id", "thread=t-1",
            "--evidence", "{\"subject\":\"Following up\"}",
            "--unknown", "observed_at",
            "--uncertainty", "Nobody watched it leave.",
            "--idempotency-key", "my-send-1",
            "--reason", "Reporting it promptly.");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(
            Operations.ReportSubmit,
            "{\"effect\":\"email_sent\",\"tool\":\"some-other-cli\",\"summary\":\"Sent by hand.\",\"provider\":\"reply\","
            + "\"account\":\"team@example.test\",\"occurred_at\":\"2026-09-17T11:04:00Z\",\"campaign_id\":\"cmp_7\","
            + "\"contact_id\":\"cnt_7\",\"operation\":\"campaign.enroll\","
            + "\"external_ids\":[{\"kind\":\"message\",\"value\":\"m-1\"},{\"kind\":\"thread\",\"value\":\"t-1\"}],"
            + "\"evidence\":{\"subject\":\"Following up\"},\"unknown_fields\":[\"observed_at\"],"
            + "\"uncertainty\":\"Nobody watched it leave.\",\"idempotency_key\":\"my-send-1\","
            + "\"reason\":\"Reporting it promptly.\",\"actor\":{\"type\":\"role\",\"id\":\"rol_7\"}}");
    }

    /// <summary>
    /// The CLI composes a body and nothing more. Whether a report may name nobody is the runtime's rule, and
    /// duplicating it here would mean two places to change it and one of them getting it wrong.
    /// </summary>
    [Fact]
    public async Task Submit_without_an_actor_is_the_runtimes_refusal_rather_than_the_clis()
    {
        using var cli = new CliRun(
            "{\"error\":{\"code\":\"actor_required\",\"message\":\"actor.id is required.\",\"retryable\":false}}",
            status: System.Net.HttpStatusCode.BadRequest);

        var exit = await cli.RunAsync("report", "submit", "--effect", "email_sent", "--tool", "t", "--summary", "s");

        Assert.Equal(ExitCodes.ApiError, exit);

        // The API's own answer, verbatim on stdout: stderr is diagnostics, and the envelope is the answer.
        Assert.Contains("actor_required", cli.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_external_id_that_is_not_a_pair_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "report", "submit", "--effect", "email_sent", "--tool", "t", "--summary", "s", "--external-id", "just-a-value");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("kind=value", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Evidence_that_is_not_a_json_object_is_a_usage_error()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync(
            "report", "submit", "--effect", "email_sent", "--tool", "t", "--summary", "s", "--evidence", "\"just a string\"");

        Assert.Equal(ExitCodes.Usage, exit);
        Assert.Contains("--evidence", cli.Error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task List_passes_the_filters_it_was_given()
    {
        using var cli = new CliRun();

        var exit = await cli.RunAsync("report", "list", "--campaign", "cmp_7", "--operation", "campaign.enroll", "--limit", "5");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ReportList, "{\"campaign_id\":\"cmp_7\",\"operation\":\"campaign.enroll\",\"limit\":5}");
    }

    /// <summary>The CLI is a client, not an editor: what the runtime answered is what is printed.</summary>
    [Fact]
    public async Task Get_prints_the_api_response_exactly_as_it_came()
    {
        using var cli = new CliRun(OneReport);

        var exit = await cli.RunAsync("report", "get", "rpt_7");

        Assert.Equal(ExitCodes.Success, exit);
        cli.AssertPosted(Operations.ReportGet, "{\"report_id\":\"rpt_7\"}");
        Assert.Equal(OneReport, cli.Text.TrimEnd());
    }

    /// <summary>
    /// The one thing a person must not misread. An effect somebody performed elsewhere sits in the same views as
    /// work Jason did, and only the words on the page keep them apart.
    /// </summary>
    [Fact]
    public async Task Human_output_says_an_effect_was_reported_from_outside_and_is_not_verified()
    {
        using var cli = new CliRun(OneReport);

        var exit = await cli.RunAsync("report", "get", "rpt_7", "--human");

        Assert.Equal(ExitCodes.Success, exit);
        Assert.Contains("Reported from outside Jason", cli.Text, StringComparison.Ordinal);
        Assert.Contains("not verified", cli.Text, StringComparison.Ordinal);
        Assert.Contains("email_sent", cli.Text, StringComparison.Ordinal);
        Assert.Contains("A follow-up was sent by hand", cli.Text, StringComparison.Ordinal);

        // What the reporter could not say, and what nobody here recognises, are both said rather than left out.
        Assert.Contains("observed_at", cli.Text, StringComparison.Ordinal);
        Assert.Contains("carrier_pigeon.dispatch (unknown here)", cli.Text, StringComparison.Ordinal);
        Assert.Contains("not a member of that campaign", cli.Text, StringComparison.Ordinal);
    }
}
