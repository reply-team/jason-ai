using System.Globalization;
using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;

namespace Jason.PluginHost.Tests;

public sealed class InvocationRunnerTests
{
    private static PluginOutcome Run(
        string mainJs,
        JsonObject? input = null,
        Action<InvocationBuilder>? configure = null,
        IReadOnlyDictionary<string, string>? modules = null,
        string operation = "echo.run")
    {
        using var package = new TempPackage(mainJs, modules);
        var invocation = InvocationFactory.Create(package.Root, operation, input, configure);
        using var stderr = new StringWriter();
        var diagnostics = SdkHarness.DiagnosticsFor(stderr, invocation);
        using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(invocation.Limits.TimeoutMs + 1000));

        return new InvocationRunner().Run(invocation, diagnostics, deadline.Token);
    }

    private static OutcomeError Error(PluginOutcome outcome)
    {
        Assert.Equal(OutcomeStatus.Failed, outcome.Status);
        return Assert.IsType<OutcomeError>(outcome.Error);
    }

    [Fact]
    public void A_plugin_that_answers_with_a_result_succeeds()
    {
        var outcome = Run(
            "export function invoke(operation, input) { return { result: { got: input.x, operation } }; }",
            new JsonObject { ["x"] = "hello" });

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Null(outcome.Error);
        Assert.Equal("hello", outcome.Result!["got"]!.GetValue<string>());
        Assert.Equal("echo.run", outcome.Result!["operation"]!.GetValue<string>());
        Assert.Equal(PluginProtocol.CurrentVersion, outcome.ProtocolVersion);
    }

    [Fact]
    public void A_promise_is_waited_for()
    {
        var outcome = Run("export async function invoke() { return { result: { awaited: true } }; }");

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.True(outcome.Result!["awaited"]!.GetValue<bool>());
    }

    [Fact]
    public void A_default_export_is_an_entry_function_too()
    {
        var outcome = Run(
            "export default function () { return { result: 1 }; }",
            configure: builder => builder.Entry = new PluginEntry("main.js", "default"));

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(1, outcome.Result!.GetValue<int>());
    }

    [Fact]
    public void An_entry_function_the_module_does_not_export_ends_the_invocation()
    {
        var outcome = Run("export function somethingElse() { return { result: 1 }; }");

        var error = Error(outcome);
        Assert.Equal(OutcomeCodes.EntryFunctionMissing, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
    }

    [Theory]
    [InlineData("return 42;")]
    [InlineData("return;")]
    [InlineData("return [1];")]
    [InlineData("return { value: 1 };")]
    public void An_answer_that_is_not_the_return_contract_is_a_bad_return(string body)
    {
        var outcome = Run("export function invoke() { " + body + " }");

        var error = Error(outcome);
        Assert.Equal(OutcomeCodes.BadReturn, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
    }

    [Fact]
    public void External_identifiers_travel_with_a_result()
    {
        var outcome = Run("export function invoke() { return { result: 1, external_ids: { contact: \"r_123\" } }; }");

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal("r_123", outcome.ExternalIds!["contact"]!.GetValue<string>());
    }

    [Fact]
    public void External_identifiers_that_are_not_strings_are_a_bad_return()
    {
        var outcome = Run("export function invoke() { return { result: 1, external_ids: { contact: 1 } }; }");

        Assert.Equal(OutcomeCodes.BadReturn, Error(outcome).Code);
    }

    [Fact]
    public void An_error_the_plugin_did_not_mean_to_throw_is_a_plugin_exception()
    {
        var outcome = Run("export function invoke() { throw new Error(\"boom\"); }");

        var error = Error(outcome);
        Assert.Equal(OutcomeCodes.PluginException, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("boom", error.Message, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(error.Details!["stack"]!.GetValue<string>()));
    }

    [Fact]
    public void A_failure_the_plugin_declares_carries_its_class_and_everything_with_it()
    {
        var outcome = Run("""
            export function invoke() {
              throw host.fail({
                class: "transient",
                code: "rate_limited",
                message: "slow down",
                details: { retry_in: 5 },
                external_ids: { req: "r1" },
              });
            }
            """);

        var error = Error(outcome);
        Assert.Equal(FailureClass.Transient, error.Class);
        Assert.Equal("rate_limited", error.Code);
        Assert.Equal("slow down", error.Message);
        Assert.Equal(5, error.Details!["retry_in"]!.GetValue<int>());
        Assert.Equal("r1", error.ExternalIds!["req"]!.GetValue<string>());
    }

    [Fact]
    public void A_rejected_promise_carrying_a_declared_failure_is_read_the_same_way()
    {
        var outcome = Run("""
            export async function invoke() {
              throw host.fail({ class: "ambiguous", code: "maybe_sent", message: "the provider did not answer" });
            }
            """);

        var error = Error(outcome);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        Assert.Equal("maybe_sent", error.Code);
    }

    [Fact]
    public void A_failure_outside_the_four_classes_is_refused_where_the_plugin_stands()
    {
        var outcome = Run("""
            export function invoke() {
              try {
                host.fail({ class: "bogus", code: "c", message: "m" });
              } catch (e) {
                return { result: { caught: e.name } };
              }

              return { result: { caught: "nothing" } };
            }
            """);

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal("TypeError", outcome.Result!["caught"]!.GetValue<string>());
    }

    [Fact]
    public void A_plugin_that_never_finishes_runs_out_of_time()
    {
        var outcome = Run(
            "export function invoke() { for (;;) { } }",
            configure: builder => builder.Limits = builder.Limits with { TimeoutMs = 300, MaxStatements = 1_000_000_000 });

        var error = Error(outcome);
        Assert.Equal(OutcomeCodes.PluginTimeout, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.True(outcome.Diagnostics.DurationMs >= 300, "the invocation ran for at least its budget");
    }

    [Fact]
    public void Runaway_recursion_ends_the_invocation()
    {
        var outcome = Run(
            "export function invoke() { function f(n) { return f(n + 1); } return f(0); }",
            configure: builder => builder.Limits = builder.Limits with { MaxRecursion = 16 });

        Assert.Equal(OutcomeCodes.PluginRecursionLimit, Error(outcome).Code);
    }

    [Fact]
    public void A_result_larger_than_the_protocol_allows_is_refused()
    {
        var outcome = Run("export function invoke() { return { result: \"x\".repeat(2000000) }; }");

        var error = Error(outcome);
        Assert.Equal(OutcomeCodes.ResultTooLarge, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
    }

    [Fact]
    public void A_package_may_be_more_than_one_module()
    {
        var outcome = Run(
            "import { helper } from \"./modules/helper.js\"; export function invoke() { return { result: helper(\"v\") }; }",
            modules: new Dictionary<string, string> { ["modules/helper.js"] = "export function helper(v) { return \"helped:\" + v; }" });

        Assert.Equal("helped:v", outcome.Result!.GetValue<string>());
    }

    [Fact]
    public void A_module_outside_the_package_is_never_loaded()
    {
        var outcome = Run("import fs from \"fs\"; export function invoke() { return { result: 1 }; }");

        var error = Error(outcome);
        Assert.Equal(OutcomeCodes.ModuleNotAllowed, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
    }

    [Fact]
    public void A_module_that_does_not_parse_says_so()
    {
        var outcome = Run("export function invoke( { return { result: 1 }; }");

        var error = Error(outcome);
        Assert.Equal(OutcomeCodes.PluginSyntaxError, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
    }

    [Fact]
    public void The_context_says_what_the_plugin_is_allowed_to_know()
    {
        var outcome = Run(
            "export function invoke(operation, input, context) { return { result: context }; }",
            configure: builder => builder.Context = new InvocationContext(
                new JsonObject { ["account"] = "a1" },
                "att_1",
                3,
                "wi_1",
                "cmp_1",
                "0.1.0"));

        var context = outcome.Result!;
        Assert.Equal(3, context["attempt_number"]!.GetValue<int>());
        Assert.Equal("att_1", context["attempt_id"]!.GetValue<string>());
        Assert.Equal("a1", context["binding"]!["account"]!.GetValue<string>());
        Assert.Equal("fake-provider", context["plugin"]!["id"]!.GetValue<string>());
        Assert.Equal(10_000, context["timeout_ms"]!.GetValue<int>());
        var deadline = DateTimeOffset.Parse(context["deadline"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        Assert.InRange(deadline, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMilliseconds(11_000));
    }

    [Fact]
    public void A_plugin_has_no_console_and_no_way_out_of_the_engine()
    {
        var outcome = Run("""
            export function invoke() {
              return { result: { console: typeof console, system: typeof System, require: typeof require, process: typeof process } };
            }
            """);

        Assert.Equal("undefined", outcome.Result!["console"]!.GetValue<string>());
        Assert.Equal("undefined", outcome.Result!["system"]!.GetValue<string>());
        Assert.Equal("undefined", outcome.Result!["require"]!.GetValue<string>());
        Assert.Equal("undefined", outcome.Result!["process"]!.GetValue<string>());
    }

    [Fact]
    public void Every_outcome_counts_what_the_invocation_cost()
    {
        var outcome = Run("export function invoke() { host.log(\"info\", \"one\"); host.log(\"info\", \"two\"); return { result: 1 }; }");

        Assert.Equal(2, outcome.Diagnostics.LogLines);
        Assert.Equal(0, outcome.Diagnostics.ExecCalls);
        Assert.Equal(0, outcome.Diagnostics.HttpCalls);
    }
}
