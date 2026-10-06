using System.Net;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;

namespace Jason.Runtime.Tests.Api;

/// <summary>
/// The key as it travels through the real request door. A refusal — of a key that cannot be one, or of a different
/// request under a key already used — says what is wrong and names the entity, and holds no part of the key, which is
/// the caller's text; and a repeat is answered with the body the first request was.
/// </summary>
public class IdempotencyKeyEndpointTests
{
    private const string Sentinel = "SENTINEL_KEYDOOR_7f3c";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_repeated_campaign_create_is_answered_with_the_body_of_the_first()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var request = new { Name = "Q3", Context = new { region = "latam" }, IdempotencyKey = "launch-q3" };

        var (firstStatus, first) = await api.PostAsync(Operations.CampaignCreate, request, Ct);
        var (againStatus, again) = await api.PostAsync(Operations.CampaignCreate, request, Ct);

        Assert.Equal(HttpStatusCode.OK, firstStatus);
        Assert.Equal(HttpStatusCode.OK, againStatus);
        Assert.Equal(first, again);
        Assert.Contains("\"idempotency_key\":\"launch-q3\"", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repeated_work_item_create_is_answered_with_the_body_of_the_first()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);
        var request = new { CampaignId = campaign, Kind = "ai_role", Role = "researcher", IdempotencyKey = "research-1" };

        var (_, first) = await api.PostAsync(Operations.WorkItemCreate, request, Ct);
        var (againStatus, again) = await api.PostAsync(Operations.WorkItemCreate, request, Ct);

        Assert.Equal(HttpStatusCode.OK, againStatus);
        Assert.Equal(first, again);
    }

    [Fact]
    public async Task A_different_request_under_a_used_key_is_a_final_conflict_that_holds_no_part_of_the_key()
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var made = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Q3", IdempotencyKey = Sentinel }, Ct);

        var (status, body) = await api.PostAsync(Operations.CampaignCreate, new { Name = "Q4", IdempotencyKey = Sentinel }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, status);
        var error = JsonNode.Parse(body)!["error"]!;
        Assert.Equal("idempotency_key_reused", (string?)error["code"]);
        Assert.False((bool)error["retryable"]!);
        Assert.Contains(made.Id, (string?)error["message"], StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);

        var campaign = await CampaignAsync(api);
        var item = await api.PostOkAsync<WorkItemDto>(
            Operations.WorkItemCreate, new { CampaignId = campaign, Kind = "ai_role", Role = "researcher", IdempotencyKey = Sentinel }, Ct);
        var (itemStatus, itemBody) = await api.PostAsync(
            Operations.WorkItemCreate, new { CampaignId = campaign, Kind = "ai_role", Role = "researcher", Priority = 9, IdempotencyKey = Sentinel }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, itemStatus);
        Assert.Contains(item.Id, itemBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", itemBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// Too long, a control character, and half of a surrogate pair written as a JSON escape — which the reader refuses
    /// before the key is ever a string — each refused, and none of them quoted.
    /// </summary>
    [Theory]
    [InlineData(Operations.CampaignCreate)]
    [InlineData(Operations.WorkItemCreate)]
    public async Task A_key_that_cannot_be_one_is_refused_without_being_quoted(string operation)
    {
        await using var api = await RuntimeApiFixture.StartAsync(Ct);
        var campaign = await CampaignAsync(api);
        var fields = operation == Operations.CampaignCreate
            ? "\"name\":\"Q3\""
            : $"\"campaign_id\":\"{campaign}\",\"kind\":\"ai_role\",\"role\":\"researcher\"";

        foreach (var key in new[] { Sentinel + new string('k', 200), Sentinel + "\\u0007", Sentinel + "\\ud800" })
        {
            var (status, body) = await api.PostRawAsync(operation, $"{{{fields},\"idempotency_key\":\"{key}\"}}", Ct);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.DoesNotContain("SENTINEL", body, StringComparison.Ordinal);
            Assert.DoesNotContain("KEYDOOR", body, StringComparison.Ordinal);
        }
    }

    private static async Task<string> CampaignAsync(RuntimeApiFixture api)
    {
        var campaign = await api.PostOkAsync<CampaignDto>(Operations.CampaignCreate, new { Name = "Work" }, Ct);
        await api.PostOkAsync<CampaignDto>(Operations.CampaignStart, new { CampaignId = campaign.Id }, Ct);
        return campaign.Id;
    }
}
