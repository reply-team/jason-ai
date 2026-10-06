using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Contracts.Tests;

/// <summary>
/// The key a caller gives a create travels in the request and comes back on the entity it made, and a request or an
/// answer without one is exactly what it was before keys existed: the field is absent, not null, so nothing that reads
/// today's answers sees a change.
/// </summary>
public class IdempotencyKeyContractTests
{
    [Fact]
    public void A_create_request_without_a_key_binds_none_and_one_with_a_key_binds_it()
    {
        var plain = JsonSerializer.Deserialize<CampaignCreateRequest>("""{"name":"Q3"}""", JasonJson.Options)!;
        var keyed = JsonSerializer.Deserialize<CampaignCreateRequest>("""{"name":"Q3","idempotency_key":"q3-create"}""", JasonJson.Options)!;
        Assert.Null(plain.IdempotencyKey);
        Assert.Equal("q3-create", keyed.IdempotencyKey);

        var item = JsonSerializer.Deserialize<WorkItemCreateRequest>(
            """{"campaign_id":"cmp_1","kind":"ai_role","role":"researcher","idempotency_key":"research-1"}""",
            JasonJson.Options)!;
        Assert.Equal("research-1", item.IdempotencyKey);
        Assert.Null(JsonSerializer.Deserialize<WorkItemCreateRequest>("""{"campaign_id":"cmp_1"}""", JasonJson.Options)!.IdempotencyKey);
    }

    [Fact]
    public void A_key_is_sent_under_its_snake_case_name()
    {
        var json = JsonSerializer.SerializeToNode(new CampaignCreateRequest("Q3", null, null, null, "q3-create"), JasonJson.Options)!.AsObject();
        Assert.Equal("q3-create", (string?)json["idempotency_key"]);
    }

    [Fact]
    public void An_entity_with_no_key_is_answered_without_the_field_and_one_with_a_key_carries_it()
    {
        var campaign = new CampaignDto("cmp_1", "Q3", CampaignStatus.Draft, [], [], null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        Assert.False(Serialized(campaign).ContainsKey("idempotency_key"));
        Assert.Equal("q3-create", (string?)Serialized(campaign with { IdempotencyKey = "q3-create" })["idempotency_key"]);

        var item = new WorkItemDto(
            "wi_1", "cmp_1", null, WorkItemKind.AiRole, "researcher", null, null, WorkItemStatus.Created, true, 0, null, null, null,
            null, null, null, new ActorRef(ActorType.Human, "ada"), [], null, null, 0, null, null, null, null,
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        Assert.False(Serialized(item).ContainsKey("idempotency_key"));
        Assert.Equal("research-1", (string?)Serialized(item with { IdempotencyKey = "research-1" })["idempotency_key"]);
    }

    private static JsonObject Serialized<T>(T value) => JsonSerializer.SerializeToNode(value, JasonJson.Options)!.AsObject();
}
