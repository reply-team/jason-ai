using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;

namespace Jason.Contracts.Tests;

public class ContractsShapeTests
{
    [Fact]
    public void Every_operation_name_is_noun_dot_verb_and_routes_under_v1()
    {
        var names = typeof(Operations).GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();
        Assert.Contains("campaign.add_contacts", names);
        Assert.Contains("suppression.remove", names);
        Assert.Contains("system.shutdown", names);
        Assert.All(names, n => Assert.Matches("^[a-z]+\\.[a-z_]+$", n));
        Assert.Equal("/v1/campaign.update_context", Operations.Route(Operations.CampaignUpdateContext));
    }

    [Fact]
    public void Campaign_dto_serializes_with_inline_context_and_snake_case_status()
    {
        var dto = new CampaignDto("cmp_A", "LatAm", CampaignStatus.Draft, new JsonObject { ["icp"] = "founders" }, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        var json = JsonSerializer.Serialize(dto, JasonJson.Options);
        Assert.Equal("{\"id\":\"cmp_A\",\"name\":\"LatAm\",\"status\":\"draft\",\"context\":{\"icp\":\"founders\"},\"created_at\":\"1970-01-01T00:00:00.000Z\",\"updated_at\":\"1970-01-01T00:00:00.000Z\",\"archived_at\":null}", json);
    }

    [Fact]
    public void Error_details_are_omitted_when_absent_and_written_when_present()
    {
        Assert.Equal("{\"error\":{\"code\":\"x\",\"message\":\"m\",\"retryable\":false}}", JsonSerializer.Serialize(new ErrorResponse(new ErrorBody("x", "m", false)), JasonJson.Options));
        var with = JsonSerializer.Serialize(new ErrorResponse(new ErrorBody("validation_failed", "m", false, [new ErrorDetail("name", "required", "name is required")])), JasonJson.Options);
        Assert.Contains("\"details\":[{\"field\":\"name\",\"code\":\"required\",\"message\":\"name is required\"}]", with, StringComparison.Ordinal);
    }

    [Fact]
    public void Enums_are_never_accepted_as_numbers()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<CampaignListRequest>("{\"status\":1}", JasonJson.Options));
        Assert.Equal(CampaignStatus.Archived, JsonSerializer.Deserialize<CampaignListRequest>("{\"status\":\"archived\"}", JasonJson.Options)!.Status);
    }

    [Fact]
    public void Contact_update_request_distinguishes_absent_from_null()
    {
        var request = JsonSerializer.Deserialize<ContactUpdateRequest>("{\"contact_id\":\"cnt_A\",\"title\":null,\"channels\":[]}", JasonJson.Options)!;
        Assert.False(request.FirstName.IsSet);
        Assert.True(request.Title.IsSet);
        Assert.Null(request.Title.Value);
        Assert.True(request.Channels.IsSet);
        Assert.Empty(request.Channels.Value!);
    }

    [Fact]
    public void A_page_carries_its_items_and_an_opaque_next_cursor()
    {
        var page = new Page<SuppressionDto>([new SuppressionDto("sup_A", "email", "a@b.test", null, DateTimeOffset.UnixEpoch)], "Y3Vyc29y");
        var json = JsonSerializer.Serialize(page, JasonJson.Options);
        Assert.Contains("\"items\":[{\"id\":\"sup_A\"", json, StringComparison.Ordinal);
        Assert.EndsWith("\"next_cursor\":\"Y3Vyc29y\"}", json, StringComparison.Ordinal);
    }
}
