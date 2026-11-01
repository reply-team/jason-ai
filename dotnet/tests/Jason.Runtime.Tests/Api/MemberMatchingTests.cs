using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// A request's members are matched by their exact spelling, at every depth. A name that differs from a member only in
/// case is not that member: at the top of a body it is skipped like any member the operation does not take, inside a
/// free-form object it is a key of its own beside the one it resembles, and inside <c>context.input</c> it is what the
/// operation's schema says of a name it does not declare.
/// </summary>
public class MemberMatchingTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.R2, "members are matched exactly")]
    public async Task A_member_spelled_in_another_case_is_not_the_member_it_resembles()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, body) = await api.PostRawAsync(Operations.CampaignCreate, """{"Name":"Q3"}""", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var error = Error(body);
        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("name", "required"), (detail.Field, detail.Code));
    }

    [Fact]
    public async Task A_member_spelled_in_another_case_beside_the_member_is_skipped()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var made = await PostOkAsync(api, Operations.CampaignCreate, """{"name":"Q3","Name":"Q4"}""");

        Assert.Equal("Q3", (string?)made["name"]);
    }

    [Fact]
    public async Task A_patch_member_spelled_in_another_case_changes_nothing()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var made = await PostOkAsync(api, Operations.CampaignCreate, """{"name":"Q3"}""");

        var updated = await PostOkAsync(api, Operations.CampaignUpdate, $$"""{"campaign_id":"{{made["id"]}}","Name":"Q4"}""");

        Assert.Equal("Q3", (string?)updated["name"]);
    }

    /// <summary>
    /// Two keys of a campaign's context that differ only in case are two keys. Read as one, they made the create's
    /// digest fail on the second, in an exception that named it.
    /// </summary>
    [Fact]
    [Critical(Family.R2, "members are matched exactly")]
    public async Task Context_keys_that_differ_only_in_case_are_two_keys_at_create()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var made = await PostOkAsync(
            api, Operations.CampaignCreate, """{"name":"Q3","idempotency_key":"q3","context":{"region":"latam","Region":"emea"}}""");

        Assert.Equal("latam", (string?)made["context"]!["region"]);
        Assert.Equal("emea", (string?)made["context"]!["Region"]);
    }

    /// <summary>The same in a patch: the update compares the keys it sets with the ones the item holds.</summary>
    [Fact]
    public async Task Keys_a_patch_sets_that_differ_only_in_case_are_two_keys()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);
        var item = await PostOkAsync(
            api, Operations.WorkItemCreate, $$$"""{"campaign_id":"{{{campaign}}}","kind":"ai_role","role":"researcher","context":{"note":"first"}}""");

        var updated = await PostOkAsync(
            api, Operations.WorkItemUpdate, $$$"""{"work_item_id":"{{{item["id"]}}}","set":{"note":"second","Note":"third"}}""");

        Assert.Equal("second", (string?)updated["context"]!["note"]);
        Assert.Equal("third", (string?)updated["context"]!["Note"]);
    }

    /// <summary>
    /// Read as one, two such keys failed the create and the update in an exception the catch-all wrote to the log, key
    /// and all. Both are answered now, and the file the real sink wrote holds no failure and neither key.
    /// </summary>
    [Fact]
    public async Task Keys_that_differ_only_in_case_put_nothing_in_the_log()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);
        var (created, createBody) = await api.PostRawAsync(
            Operations.CampaignCreate, """{"name":"Q3","context":{"sentinel_region":"latam","SENTINEL_REGION":"emea"}}""", Ct);
        var item = await PostOkAsync(
            api, Operations.WorkItemCreate, $$$"""{"campaign_id":"{{{campaign}}}","kind":"ai_role","role":"researcher","context":{"note":"first"}}""");
        var (updated, updateBody) = await api.PostRawAsync(
            Operations.WorkItemUpdate, $$$"""{"work_item_id":"{{{item["id"]}}}","set":{"sentinel_note":"second","SENTINEL_NOTE":"third"}}""", Ct);

        // Stopping flushes and closes the rolling file sink, so what is on disk now is everything there is.
        await api.Runtime.StopAsync();
        var lines = Directory.GetFiles(api.Paths.LogsDirectory, "*", SearchOption.AllDirectories).SelectMany(File.ReadAllLines).ToList();

        Assert.Contains(lines, line => line.Contains(Operations.Route(Operations.WorkItemUpdate), StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => (string?)JsonNode.Parse(line)!["@l"] is "Error" or "Fatal");
        Assert.DoesNotContain(lines, line => line.Contains("sentinel", StringComparison.OrdinalIgnoreCase));
        Assert.True(created == HttpStatusCode.OK, createBody);
        Assert.True(updated == HttpStatusCode.OK, updateBody);
    }

    /// <summary>
    /// <c>input</c> is the reserved key, and <c>Input</c> is a context key like any other: the arguments it holds are not
    /// the operation's, so an operation that declares required arguments is refused for carrying none.
    /// </summary>
    [Fact]
    [Critical(Family.R3, "the reserved key")]
    public async Task The_reserved_key_spelled_in_another_case_is_not_the_reserved_key()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);

        var (status, body) = await api.PostRawAsync(
            Operations.WorkItemCreate, ProviderOp(campaign, """{"Input":{"list":{"external_id":"lst_7"},"channel":"email"}}"""), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var error = Error(body);
        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("context.input", "required"), (detail.Field, detail.Code));
    }

    /// <summary>An argument spelled in another case is a name the schema does not declare, which it refuses as it always has.</summary>
    [Fact]
    public async Task An_argument_spelled_in_another_case_is_refused_as_a_name_the_schema_does_not_declare()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);

        var (status, body) = await api.PostRawAsync(
            Operations.WorkItemCreate, ProviderOp(campaign, """{"input":{"list":{"external_id":"lst_7"},"Channel":"email"}}"""), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var error = Error(body);
        Assert.Equal("validation_failed", error.Code);
        Assert.Contains(error.Details!, detail => (detail.Field, detail.Code) == ("context.input/Channel", "invalid"));
    }

    /// <summary>Beside the argument it resembles, the same: the argument is read, and the other name refused.</summary>
    [Fact]
    public async Task An_argument_spelled_in_another_case_beside_the_argument_is_refused_by_itself()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);

        var (status, body) = await api.PostRawAsync(
            Operations.WorkItemCreate,
            ProviderOp(campaign, """{"input":{"list":{"external_id":"lst_7"},"channel":"email","Channel":"sms"}}"""),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var error = Error(body);
        Assert.Equal("validation_failed", error.Code);
        var detail = Assert.Single(error.Details!);
        Assert.Equal(("context.input/Channel", "invalid"), (detail.Field, detail.Code));
    }

    /// <summary>
    /// A report reads its reporter from the body it was given, by the same exact spelling: <c>Type</c> and <c>Id</c>
    /// name nobody, and a report that names nobody is refused.
    /// </summary>
    [Fact]
    public async Task A_reporter_spelled_in_another_case_names_nobody()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var (status, body) = await api.PostRawAsync(
            Operations.ReportSubmit, """{"effect":"email_sent","tool":"mailer","summary":"s","actor":{"Type":"human","Id":"person-1"}}""", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("actor_required", Error(body).Code);
    }

    /// <summary>Beside the reporter's own members, names spelled in another case are skipped, and the reporter is who they say.</summary>
    [Fact]
    public async Task A_reporter_beside_names_spelled_in_another_case_is_the_one_it_names()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);

        var made = await PostOkAsync(
            api,
            Operations.ReportSubmit,
            """{"effect":"email_sent","tool":"mailer","summary":"s","actor":{"type":"human","id":"person-1","Type":"agent","ID":"other"}}""");

        Assert.Equal("human", (string?)made["reporter"]!["type"]);
        Assert.Equal("person-1", (string?)made["reporter"]!["id"]);
    }

    private static string ProviderOp(string campaign, string context) =>
        $$"""{"campaign_id":"{{campaign}}","kind":"provider_op","operation":"list_membership.add","context":{{context}}}""";

    /// <summary>The answer read as written, so that two keys differing only in case stay two.</summary>
    private static async Task<JsonObject> PostOkAsync(RuntimeApiFixture api, string operation, string body)
    {
        var (status, text) = await api.PostRawAsync(operation, body, Ct);
        Assert.True(status == HttpStatusCode.OK, $"{operation} answered {(int)status}: {text}");
        return JsonNode.Parse(text)!.AsObject();
    }

    private static ErrorBody Error(string body) => JsonSerializer.Deserialize<ErrorResponse>(body, JasonJson.Options)!.Error;

    private static async Task<string> CampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await PostOkAsync(api, Operations.CampaignCreate, """{"name":"Work"}""");
        await PostOkAsync(api, Operations.CampaignStart, $$"""{"campaign_id":"{{campaign["id"]}}"}""");
        return (string)campaign["id"]!;
    }
}
