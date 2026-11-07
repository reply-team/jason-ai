using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Harness;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;
using Jason.Runtime.Tests;

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

        return new InvocationRunner().Run(invocation, diagnostics, new UnfinishedKills(), deadline.Token);
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
        Assert.Equal(HostOutcomeCodes.EntryFunctionMissing, error.Code);
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
        Assert.Equal(HostOutcomeCodes.BadReturn, error.Code);
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

        Assert.Equal(HostOutcomeCodes.BadReturn, Error(outcome).Code);
    }

    [Fact]
    public void Whether_the_provider_found_or_created_what_an_identifier_names_travels_beside_it()
    {
        var outcome = Run("""
            export function invoke() {
              return { result: 1, external_ids: { contact: "r_123", campaign: "c_9" }, external_id_origins: { contact: "created" } };
            }
            """);

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal("created", outcome.ExternalIdOrigins!["contact"]!.GetValue<string>());
        Assert.False(outcome.ExternalIdOrigins.ContainsKey("campaign"));
    }

    [Fact]
    public void An_origin_beside_no_identifier_is_a_bad_return()
    {
        var outcome = Run("export function invoke() { return { result: 1, external_id_origins: { contact: \"created\" } }; }");

        Assert.Equal(HostOutcomeCodes.BadReturn, Error(outcome).Code);
    }

    /// <summary>An empty object says nothing, so the outcome carries no origins at all, exactly as when the key is left out.</summary>
    [Fact]
    public void An_empty_object_of_origins_says_nothing()
    {
        var outcome = Run("export function invoke() { return { result: 1, external_ids: { contact: \"r_123\" }, external_id_origins: {} }; }");

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Null(outcome.ExternalIdOrigins);
    }

    [Fact]
    public void A_plugin_that_says_nothing_about_an_origin_leaves_none()
    {
        var outcome = Run("export function invoke() { return { result: 1, external_ids: { contact: \"r_123\" } }; }");

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Null(outcome.ExternalIdOrigins);
    }

    /// <summary>
    /// An origin is <c>found</c> or <c>created</c>, about a kind the same answer identifies, in an object: a third word, a
    /// list, or a kind it returned no identifier for says nothing anybody can read, so the answer is a bad return.
    /// </summary>
    [Theory]
    [InlineData("{ contact: \"maybe\" }")]
    [InlineData("[\"created\"]")]
    [InlineData("{ campaign: \"found\" }")]
    [InlineData("\"created\"")]
    [InlineData("{ Contact: \"created\" }")]
    [InlineData("{ contact: null }")]
    public void An_origin_outside_the_rule_is_a_bad_return(string origins)
    {
        var outcome = Run("export function invoke() { return { result: 1, external_ids: { contact: \"r_123\" }, external_id_origins: " + origins + " }; }");

        var error = Error(outcome);
        Assert.Equal(HostOutcomeCodes.BadReturn, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Contains("external_id_origins", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_error_the_plugin_did_not_mean_to_throw_is_a_plugin_exception()
    {
        var outcome = Run("export function invoke() { throw new Error(\"boom\"); }");

        var error = Error(outcome);
        Assert.Equal(HostOutcomeCodes.PluginException, error.Code);
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
    public void A_blocked_failure_carries_its_block_out_of_the_plugin()
    {
        var outcome = Run("""
            export function invoke() {
              throw host.fail({
                class: "blocked",
                code: "provider_blocked",
                message: "the account is out of credits",
                block: { scope: "account", reason: "billing", link: "https://provider.example/billing" },
              });
            }
            """);

        var error = Error(outcome);
        Assert.Equal(FailureClass.Blocked, error.Class);
        Assert.Equal("provider_blocked", error.Code);
        Assert.Equal("billing", error.Block!["reason"]!.GetValue<string>());
        Assert.Equal("https://provider.example/billing", error.Block!["link"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{ class: \"blocked\", code: \"provider_blocked\", message: \"m\" }")]
    [InlineData("{ class: \"permanent\", code: \"c\", message: \"m\", block: { scope: \"account\", reason: \"other\" } }")]
    [InlineData("{ class: \"blocked\", code: \"provider_blocked\", message: \"m\", block: \"billing\" }")]
    [InlineData("{ class: \"blocked\", code: \"provider_blocked\", message: \"m\", block: { scope: \"account\", reason: \"other\", note: \"x\".repeat(9000) } }")]
    public void A_block_travels_with_the_class_blocked_and_never_without_it(string specification)
    {
        var outcome = Run($$"""
            export function invoke() {
              try {
                host.fail({{specification}});
              } catch (e) {
                return { result: { caught: e.name } };
              }

              return { result: { caught: "nothing" } };
            }
            """);

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal("TypeError", outcome.Result!["caught"]!.GetValue<string>());
    }

    /// <summary>
    /// The block is the plugin's own words as much as the details are, and is stored and shown the same way, so a
    /// granted secret quoted in it is masked on the way out; the mask is ordinary text, so the block it leaves
    /// still satisfies its contract.
    /// </summary>
    [Fact]
    public void A_secret_quoted_in_a_block_is_masked_and_the_block_still_satisfies_its_contract()
    {
        const string secret = "super-secret-block-token";
        var variable = "FAKE_BLOCK_TOKEN_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variable, secret);
        try
        {
            var outcome = Run(
                $$"""
                export function invoke() {
                  const token = host.env("{{variable}}");
                  throw host.fail({
                    class: "blocked",
                    code: "provider_blocked",
                    message: "the mailbox is at its limit",
                    block: { scope: "sender", sender_external_id: "mbx_" + token, reason: "limit", link: "https://provider.example/" + token },
                  });
                }
                """,
                configure: builder => builder.Grants = new InvocationGrants(null, null, new EnvGrants([variable])));

            var block = Error(outcome).Block!;
            Assert.DoesNotContain(secret, block.ToJsonString(), StringComparison.Ordinal);
            Assert.Equal("mbx_" + Redactor.Mask, block["sender_external_id"]!.GetValue<string>());
            Assert.Equal("https://provider.example/" + Redactor.Mask, block["link"]!.GetValue<string>());
            var problems = ProviderBlockContract.Published.Check(block);
            Assert.True(problems.Count == 0, "the masked block was refused: " + string.Join("; ", problems.Select(problem => problem.ToString())));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Theory]
    [InlineData("class: \"blocked\"")]
    [InlineData("class: \"permanent\", block: { scope: \"account\", reason: \"other\" }")]
    [InlineData("class: \"blocked\", block: \"billing\"")]
    [InlineData("class: \"blocked\", block: { scope: \"account\", reason: \"other\", note: \"x\".repeat(9000) }")]
    public void A_failure_built_by_hand_that_host_fail_would_refuse_is_a_plugin_exception(string fields)
    {
        // Nothing stops a plugin from building the object host.fail would have made; the host reads it again
        // rather than trusting the name it gives itself.
        var outcome = Run($$"""
            export function invoke() {
              const failure = new Error("m");
              Object.assign(failure, { name: "JasonFailure", code: "provider_blocked", {{fields}} });
              throw failure;
            }
            """);

        var error = Error(outcome);
        Assert.Equal(HostOutcomeCodes.PluginException, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Null(error.Block);
    }

    [Fact]
    [Critical(Family.R1, "five failure classes only")]
    public void A_failure_outside_the_five_classes_is_refused_where_the_plugin_stands()
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
    [Critical(Family.R4, "the invocation deadline")]
    public void A_plugin_that_never_finishes_runs_out_of_time()
    {
        var outcome = Run(
            "export function invoke() { for (;;) { } }",
            configure: builder => builder.Limits = builder.Limits with { TimeoutMs = 300, MaxStatements = 1_000_000_000 });

        var error = Error(outcome);
        Assert.Equal(HostOutcomeCodes.PluginTimeout, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.True(outcome.Diagnostics.DurationMs >= 300, "the invocation ran for at least its budget");
    }

    /// <summary>A door to the Runtime API that answers every call with <paramref name="status"/> and <paramref name="body"/>.</summary>
    private static LoopbackHttpServer DoorApi(int status = 200, string body = "{}") =>
        new(async context =>
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(body);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            context.Response.OutputStream.Close();
        });

    private static Action<InvocationBuilder> WithDoor(LoopbackHttpServer api, int timeoutMs = 5_000) =>
        builder =>
        {
            builder.Door = new InvocationDoor(
                api.BaseUrl.TrimEnd('/'), "door-capability-0123456789abcdef", "jrn_01JDOORLINE", ["campaign.list"], DoorLimits.Standard);
            builder.Limits = builder.Limits with { TimeoutMs = timeoutMs, MaxStatements = 1_000_000_000 };
        };

    [Fact]
    public void An_outcome_counts_the_calls_made_through_the_door()
    {
        using var api = DoorApi();

        var outcome = Run(
            "export function invoke() { host.call(\"campaign.list\"); host.call(\"campaign.list\"); return { result: null }; }",
            configure: WithDoor(api));

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(2, outcome.Diagnostics.ApiCalls);
        Assert.Equal(2, api.Requests);
    }

    /// <summary>A call through the door may have changed the runtime's state, so running out of time after one is ambiguous.</summary>
    [Fact]
    public void A_plugin_that_runs_out_of_time_after_a_door_call_may_have_acted()
    {
        using var api = DoorApi();

        var outcome = Run(
            "export function invoke() { host.call(\"campaign.list\"); for (;;) { } }",
            configure: WithDoor(api, timeoutMs: 1_000));

        var error = Error(outcome);
        Assert.Equal(HostOutcomeCodes.PluginTimeout, error.Code);
        Assert.Equal(FailureClass.Ambiguous, error.Class);
        Assert.Equal(1, outcome.Diagnostics.ApiCalls);
    }

    /// <summary>A directory granted to the plugin, holding a file the disk will not add to: a failure of the disk, on every platform, without a full one.</summary>
    private sealed class RefusingFile : IDisposable
    {
        private readonly TempPackage _place = new();
        private readonly string _directory;

        public RefusingFile()
        {
            Assert.SkipWhen(Environment.UserName == "root", "root writes a read-only file all the same.");
            _directory = Path.Combine(_place.Outside, "lines");
            Directory.CreateDirectory(_directory);
            Target = Path.Combine(_directory, "a.log");
            File.WriteAllBytes(Target, []);
            File.SetAttributes(Target, FileAttributes.ReadOnly);
        }

        public string Target { get; }

        /// <summary>The call that fails, as a statement of a script.</summary>
        public string Append => "host.appendLine(" + JsonSerializer.Serialize(Target) + ", \"a line\");";

        public InvocationGrants Grants(HttpGrants? http = null) =>
            new(null, http, null, new FilesGrants([new FileDirectoryGrant(_directory, FolderPaths.Real(_directory))]));

        public void Dispose()
        {
            File.SetAttributes(Target, FileAttributes.Normal);
            _place.Dispose();
        }
    }

    private static string CallTheReceiver(LoopbackHttpServer server) =>
        "host.http({ method: \"GET\", url: " + JsonSerializer.Serialize(server.Url("/ok")) + " });";

    [Fact]
    public void A_disk_failure_nothing_caught_after_an_http_call_may_have_acted()
    {
        using var file = new RefusingFile();
        using var server = new LoopbackHttpServer();

        var outcome = Run(
            "export function invoke() { " + CallTheReceiver(server) + file.Append + " return { result: null }; }",
            configure: builder => builder.Grants = file.Grants(new HttpGrants([server.HostKey])));

        var error = Error(outcome);
        Assert.Equal((HostOutcomeCodes.FileWriteFailed, FailureClass.Ambiguous), (error.Code, error.Class));
        Assert.Equal(1, outcome.Diagnostics.HttpCalls);
    }

    [Fact]
    public void A_disk_failure_nothing_caught_after_a_door_call_may_have_acted()
    {
        using var file = new RefusingFile();
        using var api = DoorApi();

        var outcome = Run(
            "export function invoke() { host.call(\"campaign.list\"); " + file.Append + " return { result: null }; }",
            configure: builder =>
            {
                WithDoor(api)(builder);
                builder.Grants = file.Grants();
            });

        Assert.Equal((HostOutcomeCodes.FileWriteFailed, FailureClass.Ambiguous), (Error(outcome).Code, Error(outcome).Class));
    }

    /// <summary>A rejected promise is the same ending by another road.</summary>
    [Fact]
    public void A_disk_failure_that_rejects_the_promise_after_an_http_call_may_have_acted()
    {
        using var file = new RefusingFile();
        using var server = new LoopbackHttpServer();

        var outcome = Run(
            "export async function invoke() { " + CallTheReceiver(server) + file.Append + " return { result: null }; }",
            configure: builder => builder.Grants = file.Grants(new HttpGrants([server.HostKey])));

        Assert.Equal((HostOutcomeCodes.FileWriteFailed, FailureClass.Ambiguous), (Error(outcome).Code, Error(outcome).Class));
    }

    /// <summary>The append is the one call here, and counted as one, but the disk refused it: nothing acted, so a repeat is safe.</summary>
    [Fact]
    public void A_disk_failure_nothing_caught_with_no_call_before_it_is_transient()
    {
        using var file = new RefusingFile();

        var outcome = Run(
            "export function invoke() { " + file.Append + " return { result: null }; }",
            configure: builder => builder.Grants = file.Grants());

        Assert.Equal((HostOutcomeCodes.FileWriteFailed, FailureClass.Transient), (Error(outcome).Code, Error(outcome).Class));
        Assert.Equal(1, outcome.Diagnostics.FileCalls);
    }

    [Fact]
    public void A_plugin_that_catches_a_disk_failure_reads_it_transient_whatever_was_called_before_it()
    {
        using var file = new RefusingFile();
        using var server = new LoopbackHttpServer();

        var outcome = Run(
            "export function invoke() { " + CallTheReceiver(server) + " try { " + file.Append + " } catch (e) { return { result: { read: e.class } }; } }",
            configure: builder => builder.Grants = file.Grants(new HttpGrants([server.HostKey])));

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal("transient", outcome.Result!["read"]!.GetValue<string>());
    }

    /// <summary>A plugin that caught it knows what it did before and answers for what follows: the word it writes is the one that stands.</summary>
    [Theory]
    [InlineData("transient", "file_write_failed", FailureClass.Transient)]
    [InlineData("permanent", "audit_unavailable", FailureClass.Permanent)]
    public void A_plugin_that_catches_a_disk_failure_ends_on_its_own_word(string word, string code, FailureClass failureClass)
    {
        using var file = new RefusingFile();
        using var server = new LoopbackHttpServer();

        var outcome = Run(
            "export function invoke() { " + CallTheReceiver(server) + " try { " + file.Append + " } catch (e) { throw host.fail({ class: \"" + word + "\", code: \"" + code + "\", message: e.message }); } }",
            configure: builder => builder.Grants = file.Grants(new HttpGrants([server.HostKey])));

        Assert.Equal((code, failureClass), (Error(outcome).Code, Error(outcome).Class));
    }

    /// <summary>A catch that only throws it on has said nothing of its own: it is the failure nothing caught.</summary>
    [Fact]
    public void A_disk_failure_thrown_on_as_it_came_is_one_nothing_caught()
    {
        using var file = new RefusingFile();
        using var server = new LoopbackHttpServer();

        var outcome = Run(
            "export function invoke() { " + CallTheReceiver(server) + " try { " + file.Append + " } catch (e) { throw e; } }",
            configure: builder => builder.Grants = file.Grants(new HttpGrants([server.HostKey])));

        Assert.Equal((HostOutcomeCodes.FileWriteFailed, FailureClass.Ambiguous), (Error(outcome).Code, Error(outcome).Class));
    }

    [Fact]
    public void An_API_error_the_plugin_does_not_catch_ends_the_invocation_as_that_failure()
    {
        using var api = DoorApi(404, """{"error":{"code":"not_found","message":"No campaign cmp_01J.","retryable":false}}""");

        var outcome = Run("export function invoke() { host.call(\"campaign.list\"); return { result: null }; }", configure: WithDoor(api));

        var error = Error(outcome);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal("not_found", error.Code);
        Assert.Equal("No campaign cmp_01J.", error.Message);
    }

    /// <summary>A handler that calls outside its door is misconfigured; catching the refusal does not hide it.</summary>
    [Fact]
    public void A_call_outside_the_door_ends_the_invocation_even_inside_a_catch()
    {
        using var api = DoorApi();

        var outcome = Run(
            "export function invoke() { try { host.call(\"contact.list\", {}); } catch (e) { } return { result: \"hidden\" }; }",
            configure: WithDoor(api));

        var error = Error(outcome);
        Assert.Equal(FailureClass.Permanent, error.Class);
        Assert.Equal(HostOutcomeCodes.CapabilityNotGranted, error.Code);
        Assert.Equal("api", error.Details!["capability"]!.GetValue<string>());
        Assert.Equal("contact.list", error.Details!["operation"]!.GetValue<string>());
        Assert.Equal(0, api.Requests);
    }

    [Fact]
    public void Runaway_recursion_ends_the_invocation()
    {
        var outcome = Run(
            "export function invoke() { function f(n) { return f(n + 1); } return f(0); }",
            configure: builder => builder.Limits = builder.Limits with { MaxRecursion = 16 });

        Assert.Equal(HostOutcomeCodes.PluginRecursionLimit, Error(outcome).Code);
    }

    [Fact]
    public void A_result_larger_than_the_protocol_allows_is_refused()
    {
        var outcome = Run("export function invoke() { return { result: \"x\".repeat(2000000) }; }");

        var error = Error(outcome);
        Assert.Equal(HostOutcomeCodes.ResultTooLarge, error.Code);
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
    [Critical(Family.R4, "the package is the sandbox's edge")]
    public void A_module_outside_the_package_is_never_loaded()
    {
        var outcome = Run("import fs from \"fs\"; export function invoke() { return { result: 1 }; }");

        var error = Error(outcome);
        Assert.Equal(HostOutcomeCodes.ModuleNotAllowed, error.Code);
        Assert.Equal(FailureClass.Permanent, error.Class);
    }

    [Fact]
    public void A_module_that_does_not_parse_says_so()
    {
        var outcome = Run("export function invoke( { return { result: 1 }; }");

        var error = Error(outcome);
        Assert.Equal(HostOutcomeCodes.PluginSyntaxError, error.Code);
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

        // No earlier attempt is unsettled: the keys are absent, which a plugin reads as it reads null.
        Assert.False(context.AsObject().ContainsKey("unsettled_since"));
        Assert.False(context.AsObject().ContainsKey("unsettled_reference"));
    }

    /// <summary>
    /// A later attempt after one that may have acted and returned the work item's send is told that send's reference,
    /// beside the moment, so a plugin can read the send back before it repeats anything.
    /// </summary>
    [Fact]
    public void The_context_names_the_send_the_latest_unsettled_attempt_returned()
    {
        var outcome = Run(
            "export function invoke(operation, input, context) { return { result: context }; }",
            configure: builder => builder.Context = new InvocationContext(
                null,
                "att_1",
                3,
                "wi_1",
                "cmp_1",
                "0.1.0",
                new DateTimeOffset(2026, 9, 14, 7, 30, 0, TimeSpan.Zero),
                "email-1007"));

        var context = outcome.Result!.AsObject();
        Assert.Equal("email-1007", context["unsettled_reference"]!.GetValue<string>());
        Assert.Equal(context.IndexOf("unsettled_since") + 1, context.IndexOf("unsettled_reference"));
    }

    /// <summary>
    /// A later attempt after one that may have acted is told when that one was claimed, in UTC to the millisecond, so a
    /// plugin whose provider keeps an idempotency key for a bounded time can tell whether it still does.
    /// </summary>
    [Fact]
    public void The_context_says_when_the_earliest_unsettled_attempt_was_claimed()
    {
        var outcome = Run(
            "export function invoke(operation, input, context) { return { result: context }; }",
            configure: builder => builder.Context = new InvocationContext(
                null,
                "att_1",
                3,
                "wi_1",
                "cmp_1",
                "0.1.0",
                new DateTimeOffset(2026, 9, 14, 9, 30, 0, TimeSpan.FromHours(2))));

        Assert.Equal("2026-09-14T07:30:00.000Z", outcome.Result!["unsettled_since"]!.GetValue<string>());
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
