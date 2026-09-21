using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Execution;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.Contracts.Tests.OperationContracts;

namespace Jason.Contracts.Tests;

// The canonical operation contracts live in Jason.Contracts.Operations, whose name shadows the operation-name
// class from inside this namespace, so that one name is bound here explicitly.
using Operations = Jason.Contracts.Api.Operations;

public class ContractsShapeTests
{
    /// <summary>
    /// The phrase "canonical operation" belongs to <c>docs/contracts/</c>, where it means a unit of SDR work
    /// with a published JSON contract and executed fixtures. The names in <c>Operations</c> are Runtime API
    /// operations, which have neither, and that file used to call them canonical — which is how a reader
    /// concludes <c>system.info</c> has a contract document and goes looking for one.
    /// </summary>
    /// <remarks>
    /// Asserted the positive way round, which took one red run to learn: a guard that refuses the phrase
    /// cannot tell a claim from its retraction, and it failed on the very sentence written to retract the
    /// claim. So what is held is that the retraction is still there — and, because a disclaimer nobody reads
    /// is cheap, that the example it teaches instead is a real operation that really routes that way.
    /// </remarks>
    [Fact]
    public void The_api_operation_vocabulary_says_it_is_not_the_canonical_one()
    {
        var file = Path.Combine(ContractFiles.Root, "runtime", "src", "Jason.Contracts", "Api", "Operations.cs");
        Assert.True(File.Exists(file), $"The file this guard reads is not there: '{file}'.");
        var text = File.ReadAllText(file);

        Assert.Contains("These are not the <em>canonical operations</em>", text, StringComparison.Ordinal);

        // And the three levels it prints as the example are the three levels that exist.
        Assert.Contains("<c>campaign.list</c> ↔ <c>POST /v1/campaign.list</c> ↔ <c>jason campaign list</c>", text, StringComparison.Ordinal);
        Assert.Equal("campaign.list", Operations.CampaignList);
        Assert.Equal("/v1/campaign.list", Operations.Route(Operations.CampaignList));
    }

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
        var dto = new CampaignDto("cmp_A", "LatAm", CampaignStatus.Draft, new JsonObject { ["icp"] = "founders" }, [], "local-claude", 900, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null);
        var json = JsonSerializer.Serialize(dto, JasonJson.Options);
        Assert.Equal(
            "{\"id\":\"cmp_A\",\"name\":\"LatAm\",\"status\":\"draft\",\"context\":{\"icp\":\"founders\"},\"external_ids\":[],\"execution_profile\":\"local-claude\",\"review_seconds\":900,"
                + "\"created_at\":\"1970-01-01T00:00:00.000Z\",\"updated_at\":\"1970-01-01T00:00:00.000Z\",\"archived_at\":null}",
            json);
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

    /// <summary>
    /// The escalation verbs, named once. The vocabulary is one thing at three levels — the canonical name, the
    /// route and the command — so a rename that reached only two of them would be a skill printing a command
    /// nobody can type.
    /// </summary>
    [Fact]
    public void The_decision_operations_are_named_once()
    {
        Assert.Equal("decision.raise", Operations.DecisionRaise);
        Assert.Equal("decision.answer", Operations.DecisionAnswer);
        Assert.Equal("decision.get", Operations.DecisionGet);
        Assert.Equal("decision.list", Operations.DecisionList);
        Assert.Equal("/v1/decision.answer", Operations.Route(Operations.DecisionAnswer));
    }

    /// <summary>A decision's own enums travel as snake_case strings like every other enum on the wire.</summary>
    [Fact]
    public void The_decision_enums_are_snake_case_strings_in_both_directions()
    {
        Assert.Equal("\"pending\"", JsonSerializer.Serialize(DecisionStatus.Pending, JasonJson.Options));
        Assert.Equal("\"cancelled\"", JsonSerializer.Serialize(DecisionStatus.Cancelled, JasonJson.Options));
        Assert.Equal("\"work_item\"", JsonSerializer.Serialize(DecisionReferenceKind.WorkItem, JasonJson.Options));
        Assert.Equal("\"journal_entry\"", JsonSerializer.Serialize(DecisionReferenceKind.JournalEntry, JasonJson.Options));
        Assert.Equal(DecisionReferenceKind.Attempt, JsonSerializer.Deserialize<DecisionReferenceKind>("\"attempt\"", JasonJson.Options));
    }

    /// <summary>
    /// What the last update check learned rides on <c>system.info</c> as a section of its own, null until a
    /// check has succeeded — so a runtime that has not looked yet is told apart from one that looked and found
    /// nothing newer, and never dressed up as "up to date".
    /// </summary>
    [Fact]
    public void System_info_says_what_the_last_update_check_learned_and_null_until_it_has_looked()
    {
        var info = SystemInfo();

        Assert.Contains(
            "\"update\":{\"available\":true,\"version\":\"0.2.0\",\"checked_at\":\"2026-09-19T08:00:00.000Z\",\"release_notes_url\":\"https://example.test/notes\"}",
            JsonSerializer.Serialize(info, JasonJson.Options),
            StringComparison.Ordinal);
        Assert.Contains("\"update\":null", JsonSerializer.Serialize(info with { Update = null }, JasonJson.Options), StringComparison.Ordinal);
    }

    /// <summary>
    /// What the runtime will teach its roles from rides on <c>system.info</c> as a section of its own: the
    /// directory it owns, the cap it will enforce, and what is deployed there right now. An installer that
    /// guessed the cap would validate against the wrong number, and the cap is a live setting.
    /// </summary>
    /// <remarks>
    /// <c>bytes</c> is nullable and null means "could not be measured", which is not the same as an empty
    /// skill: a role directory renamed away mid-walk — which an installer causes — would otherwise be
    /// reported as a skill of zero bytes.
    /// </remarks>
    [Fact]
    public void System_info_says_what_it_will_teach_its_roles_from_and_the_cap_it_will_enforce()
    {
        var info = SystemInfo() with
        {
            Skills = new SkillsInfo("/data/skills/roles", 1_048_576, [new DeployedRoleSkill("researcher", 4_096, null)]),
        };

        Assert.EndsWith(
            "\"skills\":{\"role_skills_directory\":\"/data/skills/roles\",\"max_skill_bytes\":1048576,"
                + "\"roles\":[{\"role\":\"researcher\",\"bytes\":4096,\"problem\":null}],\"problem\":null}}",
            JsonSerializer.Serialize(info, JasonJson.Options),
            StringComparison.Ordinal);

        // A runtime older than this section reports none at all, which every other section here also does and
        // which the CLI renders as "unknown" rather than as an empty deployment.
        Assert.EndsWith("\"skills\":null}", JsonSerializer.Serialize(info with { Skills = null }, JasonJson.Options), StringComparison.Ordinal);
    }

    /// <summary>A role the walk could not measure says so, rather than reporting a skill of no bytes.</summary>
    [Fact]
    public void A_role_that_could_not_be_measured_carries_a_problem_and_no_byte_count()
    {
        var skills = new SkillsInfo("/data/skills/roles", 4096, [new DeployedRoleSkill("researcher", null, "it moved while it was being read")]);

        Assert.Equal(
            "{\"role_skills_directory\":\"/data/skills/roles\",\"max_skill_bytes\":4096,"
                + "\"roles\":[{\"role\":\"researcher\",\"bytes\":null,\"problem\":\"it moved while it was being read\"}],\"problem\":null}",
            JsonSerializer.Serialize(skills, JasonJson.Options));
    }

    private static SystemInfoResponse SystemInfo() =>
        new(
            "0.1.0", "v1", "rt_01J", 1234, DateTimeOffset.UnixEpoch, "/data",
            new DatabaseInfo([], [], null),
            new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0, 0),
            new PluginsInfo(1, "snp_01J", DateTimeOffset.UnixEpoch, true),
            new RoutesInfo("rts_01J", DateTimeOffset.UnixEpoch, null, 0, 0),
            new UpdateInfo(true, "0.2.0", new DateTimeOffset(2026, 9, 19, 8, 0, 0, TimeSpan.Zero), "https://example.test/notes"));

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

    /// <summary>
    /// The four levels a route can be decided at, in order of specificity. They live in the contracts because
    /// both a resolution and an attempt's provenance say which one answered.
    /// </summary>
    [Fact]
    public void The_route_scopes_are_snake_case_strings_in_both_directions()
    {
        Assert.Equal("\"campaign_operation\"", JsonSerializer.Serialize(RouteScope.CampaignOperation, JasonJson.Options));
        Assert.Equal("\"campaign_default\"", JsonSerializer.Serialize(RouteScope.CampaignDefault, JasonJson.Options));
        Assert.Equal("\"global_operation\"", JsonSerializer.Serialize(RouteScope.GlobalOperation, JasonJson.Options));
        Assert.Equal("\"global_default\"", JsonSerializer.Serialize(RouteScope.GlobalDefault, JasonJson.Options));
        Assert.Equal(RouteScope.GlobalDefault, JsonSerializer.Deserialize<RouteScope>("\"global_default\"", JasonJson.Options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RouteScope>("0", JasonJson.Options));

        // Most specific first, so that a comparison is the precedence rather than a second copy of it.
        Assert.Equal(
            [RouteScope.CampaignOperation, RouteScope.CampaignDefault, RouteScope.GlobalOperation, RouteScope.GlobalDefault],
            Enum.GetValues<RouteScope>());
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

    /// <summary>
    /// Only a failure somebody classified carries a class, so an agent attempt serialises exactly as it always
    /// has: the field says "a plugin told us what kind of failure this was", and silence is not a fifth class.
    /// </summary>
    [Fact]
    public void An_attempt_error_names_a_failure_class_only_when_one_was_established()
    {
        Assert.DoesNotContain(
            "\"class\"",
            JsonSerializer.Serialize(new AttemptErrorDto("lease_expired", "m", true), JasonJson.Options),
            StringComparison.Ordinal);
        Assert.Contains(
            "\"class\":\"ambiguous\"",
            JsonSerializer.Serialize(new AttemptErrorDto("provider_unavailable", "m", false, Class: FailureClass.Ambiguous), JasonJson.Options),
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
            new RuntimeLocation("/run/runtime.json", "v1", "jason"),
            new RoleMemoryLocation("cmp_A", "researcher"));

        var json = JsonSerializer.Serialize(envelope, JasonJson.Options);

        Assert.Contains("\"envelope_version\":3", json, StringComparison.Ordinal);
        Assert.Contains("\"attempt_id\":\"att_A\"", json, StringComparison.Ordinal);
        Assert.Contains("\"attempt_number\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"work_dir\":\"/work/wi_A/att_A\"", json, StringComparison.Ordinal);
        Assert.Contains(
            "\"runtime\":{\"descriptor_file\":\"/run/runtime.json\",\"api_version\":\"v1\",\"cli_command\":\"jason\"}",
            json,
            StringComparison.Ordinal);

        // The address of the role's memory, and never the memory: what the note says is read through the API at
        // the moment the role wants it, so a stale document can never arrive looking like current truth.
        Assert.Contains("\"role_memory\":{\"campaign_id\":\"cmp_A\",\"role\":\"researcher\"}", json, StringComparison.Ordinal);

        var back = JsonSerializer.Deserialize<LaunchEnvelope>(json, JasonJson.Options)!;
        Assert.Equal(LaunchEnvelope.CurrentVersion, back.EnvelopeVersion);
        Assert.Equal("att_A", back.AttemptId);
        Assert.Equal("/work/wi_A/att_A", back.WorkDir);
        Assert.Equal(WorkItemKind.AiRole, back.Kind);
        Assert.Equal("founders", (string?)back.Context["icp"]);
        Assert.Equal(envelope.Runtime, back.Runtime);
        Assert.Equal(envelope.RoleMemory, back.RoleMemory);
        Assert.Equal(DateTimeOffset.UnixEpoch, back.LockUntil);
    }

    [Fact]
    public void An_envelope_written_before_role_memory_existed_still_reads()
    {
        // Version 2, as this runtime wrote it the day before role notes existed. The claim that the change was
        // additive is only a claim until a document written without the new field is read by the code that
        // knows about it.
        const string version2 = """
            {"envelope_version":2,"attempt_id":"att_A","attempt_number":1,"work_item_id":"wi_A","campaign_id":"cmp_A",
             "contact_id":null,"kind":"ai_role","role":"researcher","execution_profile":null,"context":{},
             "result_format":null,"timeout_seconds":3600,"heartbeat_seconds":120,"lock_until":"1970-01-01T00:00:00.000Z",
             "work_dir":"/work/wi_A/att_A","runtime":{"descriptor_file":"/run/runtime.json","api_version":"v1","cli_command":"jason"}}
            """;

        var envelope = JsonSerializer.Deserialize<LaunchEnvelope>(version2, JasonJson.Options)!;

        Assert.Equal(2, envelope.EnvelopeVersion);
        Assert.Equal("jason", envelope.Runtime.CliCommand);
        Assert.Null(envelope.RoleMemory);
    }

    [Fact]
    public void An_envelope_written_before_the_command_word_existed_still_reads()
    {
        // The change that took the envelope to version 2 was additive, which is only a claim until a document
        // written without the new field is read by the code that knows about it.
        const string version1 = """
            {"envelope_version":1,"attempt_id":"att_A","attempt_number":1,"work_item_id":"wi_A","campaign_id":"cmp_A",
             "contact_id":null,"kind":"ai_role","role":"researcher","execution_profile":null,"context":{},
             "result_format":null,"timeout_seconds":3600,"heartbeat_seconds":120,"lock_until":"1970-01-01T00:00:00.000Z",
             "work_dir":"/work/wi_A/att_A","runtime":{"descriptor_file":"/run/runtime.json","api_version":"v1"}}
            """;

        var envelope = JsonSerializer.Deserialize<LaunchEnvelope>(version1, JasonJson.Options)!;

        Assert.Equal(1, envelope.EnvelopeVersion);
        Assert.Equal("/run/runtime.json", envelope.Runtime.DescriptorFile);
        Assert.Null(envelope.Runtime.CliCommand);
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
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        null);
}
