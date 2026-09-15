using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// The outcome is read back rather than trusted. The child is a separate program running code the user
/// installed: exiting zero says the protocol completed, not that what it wrote is an answer to this invocation.
/// Every rule below is one way a plugin could otherwise put something into the runtime's state.
/// </summary>
public class OutcomeValidatorTests
{
    private const string Expected = "pin_01K000000000000000000000";

    [Fact]
    public void A_succeeded_outcome_is_read_whole()
    {
        var accepted = OutcomeValidator.TryValidate(Succeeded(), Expected, out var outcome, out var problem);

        Assert.True(accepted, problem);
        Assert.Equal(OutcomeStatus.Succeeded, outcome!.Status);
        Assert.Equal("world", outcome.Result!["hello"]!.GetValue<string>());
        Assert.Equal("r_123", outcome.ExternalIds!["contact"]!.GetValue<string>());
        Assert.Equal(412, outcome.Diagnostics.DurationMs);
        Assert.Null(outcome.Error);
        Assert.Empty(problem);
    }

    [Fact]
    public void A_failed_outcome_keeps_the_class_the_runtime_reads()
    {
        var accepted = OutcomeValidator.TryValidate(Failed(), Expected, out var outcome, out var problem);

        Assert.True(accepted, problem);
        Assert.Equal(OutcomeStatus.Failed, outcome!.Status);
        Assert.Equal(FailureClass.Transient, outcome.Error!.Class);
        Assert.Equal("rate_limited", outcome.Error.Code);
        Assert.Equal("slow down", outcome.Error.Message);
    }

    [Fact]
    public void Two_documents_are_not_one_answer()
    {
        var accepted = OutcomeValidator.TryValidate(Succeeded() + Succeeded(), Expected, out var outcome, out var problem);

        Assert.False(accepted);
        Assert.Null(outcome);
        Assert.Contains("more than one JSON document", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_that_is_not_json_is_not_an_outcome()
    {
        Assert.False(OutcomeValidator.TryValidate("not json at all", Expected, out _, out var problem));
        Assert.Contains("not valid JSON", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_json_value_that_is_not_an_object_is_not_an_outcome()
    {
        Assert.False(OutcomeValidator.TryValidate("[1,2]", Expected, out _, out var problem));
        Assert.Contains("not a JSON object", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outcome_of_another_protocol_version_is_refused()
    {
        var document = Document(protocol: 2);

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("protocol_version must be 1", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outcome_of_another_invocation_is_refused()
    {
        var document = Document(invocationId: "pin_other");

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("pin_other", problem, StringComparison.Ordinal);
        Assert.Contains("invocation_id", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_status_outside_the_protocol_is_refused()
    {
        var document = Document(status: "done");

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("status", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_outcome_without_an_error_says_nothing()
    {
        var document = Document(status: "failed");

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("must carry an error", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_succeeded_outcome_carrying_an_error_is_refused()
    {
        var document = Document(error: Error());

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("carries no error", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_class_outside_the_four_is_refused()
    {
        var document = Document(status: "failed", error: Error(failureClass: "maybe"));

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("class", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_code_that_is_not_snake_case_is_refused()
    {
        var document = Document(status: "failed", error: Error(code: "Rate-Limited"));

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("error.code", problem, StringComparison.Ordinal);
        Assert.Contains("Rate-Limited", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_message_longer_than_the_protocol_allows_is_refused()
    {
        var document = Document(status: "failed", error: Error(message: new string('m', PluginProtocol.MaxErrorMessageLength + 1)));

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("error.message is at most 2000 characters", problem, StringComparison.Ordinal);
        Assert.Contains("2001", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_result_larger_than_the_protocol_allows_is_refused()
    {
        var document = Document(result: JsonValue.Create(new string('x', PluginProtocol.MaxResultBytes)));

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("result is at most 1048576 bytes", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void More_external_ids_than_the_protocol_allows_is_refused()
    {
        var ids = new JsonObject();
        for (var index = 0; index <= PluginProtocol.MaxExternalIds; index++)
        {
            ids["id" + index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = "value";
        }

        var document = Document(externalIds: ids);

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("external_ids holds at most 64 entries", problem, StringComparison.Ordinal);
        Assert.Contains("65", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_external_id_longer_than_the_protocol_allows_is_refused()
    {
        var ids = new JsonObject { ["contact"] = new string('r', PluginProtocol.MaxExternalIdLength + 1) };
        var document = Document(externalIds: ids);

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("external_ids.contact is at most 256 characters", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_outcome_without_diagnostics_is_refused()
    {
        var document = Document(withDiagnostics: false);

        Assert.False(OutcomeValidator.TryValidate(document, Expected, out _, out var problem));
        Assert.Contains("diagnostics must be present", problem, StringComparison.Ordinal);
    }

    private static string Succeeded() => Document();

    private static string Failed() => Document(status: "failed", error: Error(), result: null, externalIds: null);

    private static JsonObject Error(string failureClass = "transient", string code = "rate_limited", string message = "slow down") =>
        new()
        {
            ["class"] = failureClass,
            ["code"] = code,
            ["message"] = message,
            ["details"] = null,
            ["external_ids"] = null,
        };

    private static string Document(
        string? invocationId = Expected,
        int protocol = 1,
        string status = "succeeded",
        JsonNode? result = null,
        JsonObject? externalIds = null,
        JsonObject? error = null,
        bool withDiagnostics = true)
    {
        var document = new JsonObject
        {
            ["protocol_version"] = protocol,
            ["invocation_id"] = invocationId,
            ["status"] = status,
            ["result"] = result ?? (status == "succeeded" ? new JsonObject { ["hello"] = "world" } : null),
            ["external_ids"] = externalIds ?? (status == "succeeded" && error is null ? new JsonObject { ["contact"] = "r_123" } : null),
            ["error"] = error,
        };

        if (withDiagnostics)
        {
            document["diagnostics"] = new JsonObject
            {
                ["duration_ms"] = 412,
                ["exec_calls"] = 1,
                ["http_calls"] = 0,
                ["log_lines"] = 3,
            };
        }

        return document.ToJsonString();
    }
}
