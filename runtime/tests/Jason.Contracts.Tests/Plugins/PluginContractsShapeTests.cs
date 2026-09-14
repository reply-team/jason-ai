using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Api;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;

namespace Jason.Contracts.Tests.Plugins;

public class PluginContractsShapeTests
{
    private static PluginInvocation SampleInvocation() => new(
        PluginProtocol.CurrentVersion,
        "pin_01J",
        "att_01J",
        new InvocationPlugin("fake-provider", "1.0.0", PluginKind.Provider, "/packages/fake-provider", "sha256:ab", new PluginEntry("main.js", "invoke")),
        "echo.run",
        PluginProtocol.OperationContractVersion,
        new JsonObject { ["value"] = "hello" },
        new InvocationContext(new JsonObject { ["account"] = "a1" }, "att_01J", 2, "wi_01J", "cmp_01J", "0.1.0"),
        new InvocationGrants(
            new ExecGrants([new ExecutableGrant("reply", "/usr/local/bin/reply")]),
            new HttpGrants(["api.example.test"]),
            new EnvGrants(["EXAMPLE_TOKEN"])),
        new InvocationLimits(60_000, 67_108_864, 10_000_000, 64, new ExecLimits(4_194_304, 64), new HttpLimits(4_194_304, 1_048_576, 64, 30_000), new LogLimits(16_384, 4_194_304)));

    [Fact]
    public void The_registry_operations_are_named_and_routed_like_every_other()
    {
        Assert.Equal("plugin.list", Operations.PluginList);
        Assert.Equal("plugin.reload", Operations.PluginReload);
        Assert.Equal("/v1/plugin.list", Operations.Route(Operations.PluginList));
        Assert.Equal("/v1/plugin.reload", Operations.Route(Operations.PluginReload));
    }

    [Fact]
    public void The_invocation_envelope_is_snake_case_all_the_way_down()
    {
        var json = JsonSerializer.Serialize(SampleInvocation(), JasonJson.Options);

        Assert.Contains("\"protocol_version\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"invocation_id\":\"pin_01J\"", json, StringComparison.Ordinal);
        Assert.Contains("\"operation_contract_version\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"kind\":\"provider\"", json, StringComparison.Ordinal);
        Assert.Contains("\"attempt_number\":2", json, StringComparison.Ordinal);
        Assert.Contains("\"runtime_version\":\"0.1.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"executables\":[{\"name\":\"reply\",\"path\":\"/usr/local/bin/reply\"}]", json, StringComparison.Ordinal);
        Assert.Contains("\"output_bytes\":4194304", json, StringComparison.Ordinal);
        Assert.Contains("\"memory_bytes\":67108864", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_invocation_envelope_round_trips()
    {
        var json = JsonSerializer.Serialize(SampleInvocation(), JasonJson.Options);

        var back = JsonSerializer.Deserialize<PluginInvocation>(json, JasonJson.Options)!;

        Assert.Equal("fake-provider", back.Plugin.Id);
        Assert.Equal(PluginKind.Provider, back.Plugin.Kind);
        Assert.Equal("main.js", back.Plugin.Entry.Module);
        Assert.Equal("hello", back.Input["value"]!.GetValue<string>());
        Assert.Equal("a1", back.Context.Binding!["account"]!.GetValue<string>());
        Assert.Equal(2, back.Context.AttemptNumber);
        Assert.Equal("/usr/local/bin/reply", back.Grants.Exec!.Executables[0].Path);
        Assert.Equal(30_000, back.Limits.Http.TimeoutMs);
    }

    [Fact]
    public void A_failed_outcome_carries_its_class_and_omits_what_it_does_not_have()
    {
        var outcome = new PluginOutcome(
            PluginProtocol.CurrentVersion,
            "pin_01J",
            OutcomeStatus.Failed,
            Result: null,
            ExternalIds: null,
            new OutcomeError(FailureClass.Ambiguous, "plugin_timeout", "it never answered", Details: null, ExternalIds: null),
            new OutcomeDiagnostics(412, 1, 0, 3));

        var json = JsonSerializer.Serialize(outcome, JasonJson.Options);

        Assert.Contains("\"status\":\"failed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"class\":\"ambiguous\"", json, StringComparison.Ordinal);
        Assert.Contains("\"error\":{\"class\":\"ambiguous\",\"code\":\"plugin_timeout\",\"message\":\"it never answered\"}", json, StringComparison.Ordinal);
        Assert.Contains("\"diagnostics\":{\"duration_ms\":412,\"exec_calls\":1,\"http_calls\":0,\"log_lines\":3}", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"details\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_succeeded_outcome_carries_the_result_and_the_identifiers_the_provider_returned()
    {
        var outcome = new PluginOutcome(
            PluginProtocol.CurrentVersion,
            "pin_01J",
            OutcomeStatus.Succeeded,
            JsonNode.Parse("""{"id":"r_123"}"""),
            new JsonObject { ["contact"] = "r_123", ["sequenceId"] = "s_9" },
            Error: null,
            new OutcomeDiagnostics(12, 0, 1, 0));

        var json = JsonSerializer.Serialize(outcome, JasonJson.Options);
        var back = JsonSerializer.Deserialize<PluginOutcome>(json, JasonJson.Options)!;

        Assert.Equal(OutcomeStatus.Succeeded, back.Status);
        Assert.Equal("r_123", back.Result!["id"]!.GetValue<string>());
        Assert.Equal("r_123", back.ExternalIds!["contact"]!.GetValue<string>());

        // The keys are the provider's names for its own identifiers: they travel exactly as the plugin wrote them,
        // untouched by the snake_case policy that governs the runtime's own members.
        Assert.Contains("\"external_ids\":{\"contact\":\"r_123\",\"sequenceId\":\"s_9\"}", json, StringComparison.Ordinal);
        Assert.Equal("s_9", back.ExternalIds["sequenceId"]!.GetValue<string>());
        Assert.Null(back.Error);
    }

    [Fact]
    public void Plugin_enums_are_never_accepted_as_numbers()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<PluginOutcome>(
            """{"protocol_version":1,"invocation_id":"pin_1","status":0,"diagnostics":{"duration_ms":1,"exec_calls":0,"http_calls":0,"log_lines":0}}""",
            JasonJson.Options));
        Assert.Equal(FailureClass.Validation, JsonSerializer.Deserialize<OutcomeError>(
            """{"class":"validation","code":"c","message":"m"}""",
            JasonJson.Options)!.Class);
    }

    [Fact]
    public void The_registry_response_names_a_status_and_a_source_in_words()
    {
        var dto = new PluginRegistryDto(
            new SnapshotDto("snp_01J", DateTimeOffset.UnixEpoch, SnapshotSource.Startup, 1),
            [
                new PluginDto("fake-provider", "1.0.0", PluginKind.Provider, "Fake provider", null, null, "/packages/fake-provider", "sha256:ab",
                    new PluginContractsDto([1], [1]), ["echo.run"], new PluginEntry("main.js", "invoke"),
                    new PluginCapabilitiesDto(
                        new ExecCapabilityDto([new ExecutableDto("reply", null, null, "0.4.0")], []),
                        new ListCapabilityDto(["api.example.test"], []),
                        null),
                    new PluginLimitsDto(60_000, 64),
                    PluginStatus.Unavailable,
                    [new PluginProblemDto("executable_missing", "plugin.yaml#capabilities.exec.executables[0]", "reply was not found")])
            ],
            new ReloadReportDto(DateTimeOffset.UnixEpoch, SnapshotSource.Startup, true,
                [new CandidateDto("fake-provider", "fake-provider", CandidateStatus.Unavailable, [])]),
            Activated: true);

        var json = JsonSerializer.Serialize(dto, JasonJson.Options);

        Assert.Contains("\"source\":\"startup\"", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"unavailable\"", json, StringComparison.Ordinal);
        Assert.Contains("\"plugin_count\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"loaded_at\":\"1970-01-01T00:00:00.000Z\"", json, StringComparison.Ordinal);
        Assert.Contains("\"min_version\":\"0.4.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"last_reload\":{", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reload_request_carries_an_actor_and_a_reason_and_the_list_request_is_empty()
    {
        Assert.Equal("{}", JsonSerializer.Serialize(new PluginListRequest(), JasonJson.Options));
        var reload = JsonSerializer.Deserialize<PluginReloadRequest>("""{"actor":{"type":"human"},"reason":"after an edit"}""", JasonJson.Options)!;
        Assert.Equal(ActorType.Human, reload.Actor!.Type);
        Assert.Equal("after an edit", reload.Reason);
    }

    [Fact]
    public void System_info_carries_a_plugins_section()
    {
        var info = new SystemInfoResponse(
            "0.1.0", ApiVersion.Current, "rt_01J", 1234, DateTimeOffset.UnixEpoch, "/data",
            new DatabaseInfo([]),
            new DispatcherInfo(DispatcherState.Running, 10, 4, 0, null, 0),
            new PluginsInfo(1, "snp_01J", DateTimeOffset.UnixEpoch, true));

        var json = JsonSerializer.Serialize(info, JasonJson.Options);

        Assert.Contains("\"plugins\":{\"active_count\":1,\"snapshot_id\":\"snp_01J\"", json, StringComparison.Ordinal);
        Assert.Contains("\"last_reload_activated\":true", json, StringComparison.Ordinal);
    }

    [Fact]
    public void The_protocol_pins_the_versions_and_the_limits_the_whole_wave_quotes()
    {
        Assert.Equal(1, PluginProtocol.CurrentVersion);
        Assert.Equal(1, PluginProtocol.ManifestVersion);
        Assert.Equal(1, PluginProtocol.OperationContractVersion);
        Assert.Equal("pin", PluginProtocol.InvocationIdPrefix);
        Assert.Equal("snp", PluginProtocol.SnapshotIdPrefix);
        Assert.Equal(1_048_576, PluginProtocol.MaxInputBytes);
        Assert.Equal(65_536, PluginProtocol.MaxBindingBytes);
        Assert.Equal(1_048_576, PluginProtocol.MaxResultBytes);
        Assert.Equal(65_536, PluginProtocol.MaxDetailsBytes);
        Assert.Equal(64, PluginProtocol.MaxErrorCodeLength);
        Assert.Equal(2000, PluginProtocol.MaxErrorMessageLength);
        Assert.Equal(64, PluginProtocol.MaxExternalIds);
        Assert.Equal(256, PluginProtocol.MaxExternalIdLength);
        Assert.Equal(128, PluginProtocol.MaxCorrelationIdLength);
        Assert.Equal<string[]>(["--version", "-v", "-V", "version"], PluginProtocol.AllowedVersionCommands);
    }
}
