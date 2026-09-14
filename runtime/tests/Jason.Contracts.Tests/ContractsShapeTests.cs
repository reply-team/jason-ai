using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
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

    [Fact]
    public void The_work_item_and_role_operations_are_named_once()
    {
        Assert.Equal("workitem.create", Operations.WorkItemCreate);
        Assert.Equal("workitem.get", Operations.WorkItemGet);
        Assert.Equal("workitem.list", Operations.WorkItemList);
        Assert.Equal("workitem.update", Operations.WorkItemUpdate);
        Assert.Equal("workitem.cancel", Operations.WorkItemCancel);
        Assert.Equal("workitem.heartbeat", Operations.WorkItemHeartbeat);
        Assert.Equal("workitem.set_result", Operations.WorkItemSetResult);
        Assert.Equal("workitem.complete", Operations.WorkItemComplete);
        Assert.Equal("role.list", Operations.RoleList);
        Assert.Equal("role.add", Operations.RoleAdd);
        Assert.Equal("/v1/workitem.set_result", Operations.Route(Operations.WorkItemSetResult));
    }

    [Fact]
    public void The_work_enums_are_snake_case_strings_in_both_directions()
    {
        Assert.Equal("\"ai_role\"", JsonSerializer.Serialize(WorkItemKind.AiRole, JasonJson.Options));
        Assert.Equal("\"provider_op\"", JsonSerializer.Serialize(WorkItemKind.ProviderOp, JasonJson.Options));
        Assert.Equal(WorkItemKind.ProviderOp, JsonSerializer.Deserialize<WorkItemKind>("\"provider_op\"", JasonJson.Options));
        Assert.Equal("\"attempt\"", JsonSerializer.Serialize(ActorType.Attempt, JasonJson.Options));
        Assert.Equal("\"interrupted\"", JsonSerializer.Serialize(AttemptStatus.Interrupted, JasonJson.Options));
        Assert.Equal("\"draining\"", JsonSerializer.Serialize(DispatcherState.Draining, JasonJson.Options));
        Assert.Equal("\"succeeded\"", JsonSerializer.Serialize(CompletionStatus.Succeeded, JasonJson.Options));
        Assert.Equal(WorkItemStatus.Expired, JsonSerializer.Deserialize<WorkItemStatus>("\"expired\"", JasonJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<WorkItemStatus>("3", JasonJson.Options));
    }

    [Fact]
    public void A_work_item_patch_tells_an_absent_field_from_an_explicit_null()
    {
        var request = JsonSerializer.Deserialize<WorkItemUpdateRequest>("""{"work_item_id":"wi_x","priority":5,"due_at":null}""", JasonJson.Options)!;

        Assert.Equal("wi_x", request.WorkItemId);
        Assert.True(request.Priority.IsSet);
        Assert.Equal(5, request.Priority.Value);
        Assert.True(request.DueAt.IsSet);
        Assert.Null(request.DueAt.Value);
        Assert.False(request.NotBefore.IsSet);
        Assert.False(request.MaxAttempts.IsSet);
        Assert.False(request.ResultFormat.IsSet);
    }

    [Fact]
    public void Attempts_and_context_snapshots_are_omitted_rather_than_written_as_null()
    {
        var attempt = NewAttemptDto();
        var withoutSnapshot = JsonSerializer.Serialize(attempt, JasonJson.Options);
        Assert.DoesNotContain("\"context_snapshot\"", withoutSnapshot, StringComparison.Ordinal);
        Assert.Contains("\"command\":\"ai_role\"", withoutSnapshot, StringComparison.Ordinal);
        Assert.Contains(
            "\"context_snapshot\":{\"icp\":\"founders\"}",
            JsonSerializer.Serialize(attempt with { ContextSnapshot = new JsonObject { ["icp"] = "founders" } }, JasonJson.Options),
            StringComparison.Ordinal);

        var listed = JsonSerializer.Serialize(NewWorkItemDto(null), JasonJson.Options);
        Assert.DoesNotContain("\"attempts\"", listed, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"ai_role\"", listed, StringComparison.Ordinal);
        Assert.Contains("\"created_by\":{\"type\":\"human\",\"id\":null}", listed, StringComparison.Ordinal);
        Assert.Contains("\"attempts\":[{\"id\":\"att_A\"", JsonSerializer.Serialize(NewWorkItemDto([attempt]), JasonJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public void An_attempt_error_omits_the_trace_and_the_details_it_does_not_carry()
    {
        Assert.Equal(
            "{\"code\":\"lease_expired\",\"message\":\"m\",\"retriable\":true}",
            JsonSerializer.Serialize(new AttemptErrorDto("lease_expired", "m", true), JasonJson.Options));
        Assert.Contains(
            "\"trace\":\"exit 3\"",
            JsonSerializer.Serialize(new AttemptErrorDto("executor_exited", "m", true, "exit 3"), JasonJson.Options),
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_launch_envelope_round_trips_in_the_api_dialect()
    {
        var envelope = new LaunchEnvelope(
            LaunchEnvelope.CurrentVersion,
            "att_A",
            2,
            "wi_A",
            "cmp_A",
            null,
            WorkItemKind.AiRole,
            "researcher",
            null,
            new JsonObject { ["icp"] = "founders" },
            null,
            3600,
            120,
            DateTimeOffset.UnixEpoch,
            "/work/wi_A/att_A",
            new RuntimeLocation("/run/runtime.json", "v1"));

        var json = JsonSerializer.Serialize(envelope, JasonJson.Options);

        Assert.Contains("\"envelope_version\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"attempt_id\":\"att_A\"", json, StringComparison.Ordinal);
        Assert.Contains("\"attempt_number\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"work_dir\":\"/work/wi_A/att_A\"", json, StringComparison.Ordinal);
        Assert.Contains("\"runtime\":{\"descriptor_file\":\"/run/runtime.json\",\"api_version\":\"v1\"}", json, StringComparison.Ordinal);

        var back = JsonSerializer.Deserialize<LaunchEnvelope>(json, JasonJson.Options)!;
        Assert.Equal(LaunchEnvelope.CurrentVersion, back.EnvelopeVersion);
        Assert.Equal("att_A", back.AttemptId);
        Assert.Equal("/work/wi_A/att_A", back.WorkDir);
        Assert.Equal(WorkItemKind.AiRole, back.Kind);
        Assert.Equal("founders", (string?)back.Context["icp"]);
        Assert.Equal(envelope.Runtime, back.Runtime);
        Assert.Equal(DateTimeOffset.UnixEpoch, back.LockUntil);
    }

    private static AttemptDto NewAttemptDto() => new(
        "att_A",
        1,
        AttemptStatus.Running,
        WorkItemKind.AiRole,
        null,
        null,
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        null,
        null,
        DateTimeOffset.UnixEpoch,
        null);

    private static WorkItemDto NewWorkItemDto(IReadOnlyList<AttemptDto>? attempts) => new(
        "wi_A",
        "cmp_A",
        null,
        WorkItemKind.AiRole,
        "researcher",
        null,
        null,
        WorkItemStatus.Created,
        true,
        0,
        null,
        null,
        null,
        null,
        null,
        null,
        new ActorRef(ActorType.Human),
        new JsonObject(),
        null,
        null,
        0,
        null,
        null,
        attempts,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        null);
}
