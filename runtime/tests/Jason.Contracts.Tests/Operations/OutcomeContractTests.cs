using System.Text.Json.Nodes;
using Jason.Contracts.Operations;
using Jason.Contracts.Plugins;
using Jason.Runtime.Tests;

namespace Jason.Contracts.Tests.OperationContracts;

/// <summary>
/// What the runtime makes of an answer the protocol already accepted. A well-formed outcome whose content is
/// nonsense for the operation is a defect in the plugin, and pinning an identifier of a kind nobody declared would
/// put a value into Jason's record that nothing later knows how to read.
/// </summary>
public class OutcomeContractTests
{
    private static readonly OperationContract Add = OperationCatalog.Find("list_membership.add")!;
    private static readonly OperationContract Get = OperationCatalog.Find("campaign.get")!;

    private static JsonNode Result(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void A_result_of_the_promised_shape_passes()
    {
        var result = Result("""
            {"items":[{"contact_id":"cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD","status":"added","external_ids":{"contact":"p_88421"}}]}
            """);

        Assert.Empty(OutcomeContract.CheckResult(Add, result));
    }

    [Fact]
    [Critical(Family.R1, "a result that breaks its schema is refused")]
    public void A_result_of_another_shape_is_reported_with_the_pointers_that_name_the_trouble()
    {
        var result = Result("""{"items":[{"contact_id":"cnt_01JB6K8TQ2W9V4MZ0C3Y7H5NRD","status":"joined"}]}""");

        var problems = OutcomeContract.CheckResult(Add, result);

        Assert.Equal("/items/0/status", Assert.Single(problems).Pointer);
        Assert.Equal("enum", problems[0].Reason);
    }

    [Fact]
    public void A_missing_result_is_reported_rather_than_read_as_an_empty_answer()
    {
        var problems = OutcomeContract.CheckResult(Get, null);

        Assert.NotEmpty(problems);
        Assert.Equal("", problems[0].Pointer);
    }

    [Fact]
    public void An_identifier_of_a_declared_kind_is_accepted()
    {
        Assert.Empty(OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = "p_88421" }));
    }

    [Fact]
    [Critical(Family.R1, "identifiers are declared kinds only")]
    public void An_identifier_of_a_kind_the_operation_never_declared_is_refused()
    {
        var problems = OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = "p_1", ["enrollment"] = "e_1" });

        var problem = Assert.Single(problems);
        Assert.Equal("/external_ids/enrollment", problem.Pointer);
        Assert.Equal("additional_properties", problem.Reason);
        Assert.Contains("list_membership.add", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A failed answer keeps its identifiers on the error, so a pointer that named <c>/external_ids</c> would
    /// send its author to a key that is null in the document they actually wrote — evidence pointing at the
    /// wrong place is worse than no evidence, because it is believed.
    /// </summary>
    [Fact]
    public void An_identifier_refused_on_a_failure_is_pointed_at_where_the_plugin_wrote_it()
    {
        var problems = OutcomeContract.CheckExternalIds(Add, new JsonObject { ["enrollment"] = "e_1" }, OutcomeContract.OnTheError);

        Assert.Equal("/error/external_ids/enrollment", Assert.Single(problems).Pointer);
    }

    [Fact]
    public void An_identifier_that_is_not_a_string_is_refused_because_it_travels_exactly_as_written()
    {
        var problems = OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = 88421 });

        Assert.Equal("type", Assert.Single(problems).Reason);
    }

    [Fact]
    public void An_identifier_that_is_empty_or_longer_than_the_protocol_carries_is_refused()
    {
        Assert.Equal("min_length", Assert.Single(OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = "" })).Reason);
        Assert.Equal(
            "max_length",
            Assert.Single(OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = new string('p', 257) })).Reason);
    }

    /// <summary>
    /// An identifier is a provider's word rendered into a person's terminal beside the rest of a table. A newline
    /// in it forges a row, an escape sequence rewrites what is already on the screen, and a NUL ends the string
    /// somewhere no reader expects — so a value carrying one is refused rather than kept and printed later. The
    /// offending values are built here, character by character, rather than parsed out of text.
    /// </summary>
    [Theory]
    // Critical: a provider value never prints control characters.
    [InlineData("p_884\n21", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.R1)])]
    [InlineData("p_884\u001b[2J21")]
    [InlineData("p_884\u000021")]
    [InlineData("p_884\u009b31m21")]
    public void An_identifier_carrying_a_control_character_is_refused_before_anything_prints_it(string identifier)
    {
        var problems = OutcomeContract.CheckExternalIds(Add, new JsonObject { ["contact"] = identifier });

        Assert.Equal("pattern", Assert.Single(problems).Reason);
    }

    [Fact]
    public void Returning_no_identifiers_at_all_is_an_ordinary_answer()
    {
        Assert.Empty(OutcomeContract.CheckExternalIds(Add, null));
        Assert.Empty(OutcomeContract.CheckExternalIds(Add, new JsonObject()));
    }

    private static OutcomeError Failure(FailureClass failureClass, string code) => new(failureClass, code, "the provider said no", null, null);

    /// <summary>
    /// A failure is read by its class, so the class has to be the one its operation gives the code: every code the add
    /// declares passes under its own class, which is the whole of what the check asks.
    /// </summary>
    [Fact]
    public void Every_code_an_operation_declares_passes_under_the_class_it_declares()
    {
        Assert.All(Add.FailureCodes, declared => Assert.Null(OutcomeContract.CheckFailure(Add, Failure(declared.Class, declared.Code))));
    }

    [Fact]
    public void A_code_the_operation_does_not_declare_is_refused_at_the_code()
    {
        var problem = OutcomeContract.CheckFailure(Add, Failure(FailureClass.Permanent, "quota_exceeded"));

        Assert.Equal(("/error/code", "enum"), (problem?.Pointer, problem?.Reason));
        Assert.Contains("quota_exceeded", problem!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_code_under_another_class_is_refused_at_the_class_naming_both()
    {
        var problem = OutcomeContract.CheckFailure(Add, Failure(FailureClass.Permanent, "rate_limited"));

        Assert.Equal(("/error/class", "enum"), (problem?.Pointer, problem?.Reason));
        Assert.Contains("`transient`", problem!.Message, StringComparison.Ordinal);
        Assert.Contains("`permanent`", problem.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The host's own endings belong to no operation, so they pass beside any of them — under the class the host gives
    /// each, and only there.
    /// </summary>
    [Theory]
    [InlineData(HostOutcomeCodes.PluginException, FailureClass.Permanent, null)]
    [InlineData(HostOutcomeCodes.PluginTimeout, FailureClass.Ambiguous, null)]
    [InlineData(HostOutcomeCodes.PluginTimeout, FailureClass.Permanent, null)]
    [InlineData(HostOutcomeCodes.PluginException, FailureClass.Transient, "/error/code")]
    [InlineData(HostOutcomeCodes.HttpLimit, FailureClass.Blocked, "/error/code")]
    public void A_host_ending_passes_only_under_the_class_the_host_gives_it(string code, FailureClass failureClass, string? pointer)
    {
        Assert.Equal(pointer, OutcomeContract.CheckFailure(Get, Failure(failureClass, code))?.Pointer);
    }

    public static TheoryData<string, string> EveryHostCodeBesideEveryOperation()
    {
        var data = new TheoryData<string, string>();
        foreach (var contract in OperationCatalog.All)
        {
            foreach (var code in HostOutcomeCodes.All.Order(StringComparer.Ordinal))
            {
                data.Add(contract.Id, code);
            }
        }

        return data;
    }

    /// <summary>Every code the host writes passes under <c>permanent</c>, the class it writes them all with, beside every operation.</summary>
    [Theory]
    [MemberData(nameof(EveryHostCodeBesideEveryOperation))]
    public void Every_host_code_passes_under_permanent_beside_every_operation(string operation, string code)
    {
        var problem = OutcomeContract.CheckFailure(OperationCatalog.Find(operation)!, Failure(FailureClass.Permanent, code));

        Assert.True(problem is null, $"'{code}' under permanent was refused beside '{operation}': {problem?.Pointer} {problem?.Message}");
    }
}
