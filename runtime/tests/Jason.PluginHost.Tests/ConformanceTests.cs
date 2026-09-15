using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;

namespace Jason.PluginHost.Tests;

/// <summary>
/// The story's acceptance list, driven through the whole mode against the canonical package: what a plugin
/// author copies is what these tests run.
/// </summary>
[Collection(ModeCollection.Name)]
public sealed class ConformanceTests : IDisposable
{
    private const string TokenVariable = "FAKE_TOKEN";
    private const string TokenValue = "fake-token-value-long-enough";

    private readonly TempPackage _written = new();

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(TokenVariable, null);
        _written.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static InvocationGrants Grants { get; } = new(
        new ExecGrants([new ExecutableGrant("dotnet", Executables.Dotnet)]),
        null,
        new EnvGrants([TokenVariable]));

    private static Task<(int Exit, string Stdout, IReadOnlyList<string> Stderr)> RunAsync(
        string operation,
        JsonObject? input = null,
        Action<InvocationBuilder>? configure = null) =>
        ModeRun.RunAsync(
            InvocationFactory.Create(ModeRun.FakeProviderRoot, operation, input, builder =>
            {
                builder.Grants = Grants;
                configure?.Invoke(builder);
            }),
            Ct);

    private static async Task<PluginOutcome> OutcomeAsync(
        string operation,
        JsonObject? input = null,
        Action<InvocationBuilder>? configure = null)
    {
        var (exit, stdout, _) = await RunAsync(operation, input, configure);

        Assert.Equal(HostExitCodes.Completed, exit);
        return ModeRun.Outcome(stdout);
    }

    [Fact]
    public async Task An_operation_the_plugin_implements_answers_with_its_result()
    {
        var outcome = await OutcomeAsync("echo.run", new JsonObject { ["value"] = 7 });

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(7, outcome.Result!["echo"]!["value"]!.GetValue<int>());
        Assert.Null(outcome.Error);
    }

    [Fact]
    public async Task The_plugin_is_told_who_is_calling_and_for_how_long()
    {
        var outcome = await OutcomeAsync(
            "context.echo",
            configure: builder =>
            {
                builder.CorrelationId = "att_conformance";
                builder.Context = new InvocationContext(null, "att_1", 2, "wi_1", "cmp_1", "0.1.0");
            });

        var context = outcome.Result!;
        Assert.Equal(2, context["attempt_number"]!.GetValue<int>());
        Assert.Equal("att_conformance", context["correlation_id"]!.GetValue<string>());
        Assert.Equal("fake-provider", context["plugin"]!["id"]!.GetValue<string>());
        Assert.Equal(10_000, context["timeout_ms"]!.GetValue<int>());
    }

    [Fact]
    public async Task A_program_the_plugin_was_granted_runs_and_answers()
    {
        var outcome = await OutcomeAsync(
            "exec.run",
            new JsonObject
            {
                ["executable"] = "dotnet",
                ["args"] = new JsonArray(FakeProviderCli.Dll, "echo-args", "hi"),
            });

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(0, outcome.Result!["exit_code"]!.GetValue<int>());
        Assert.Equal("hi\n", outcome.Result!["stdout"]!.GetValue<string>().ReplaceLineEndings("\n"));
        Assert.Equal(1, outcome.Diagnostics.ExecCalls);
    }

    [Fact]
    public async Task A_program_that_floods_its_output_is_cut_at_the_cap()
    {
        var outcome = await OutcomeAsync(
            "exec.run",
            new JsonObject
            {
                ["executable"] = "dotnet",
                ["args"] = new JsonArray(FakeProviderCli.Dll, "spew", "300000"),
            },
            builder => builder.Limits = builder.Limits with { Exec = new ExecLimits(65_536, 64) });

        Assert.True(outcome.Result!["truncated"]!["stdout"]!.GetValue<bool>());
        Assert.Equal(65_536, outcome.Result!["stdout"]!.GetValue<string>().Length);
    }

    [Fact]
    public async Task Only_a_granted_variable_is_readable_and_only_by_name()
    {
        Environment.SetEnvironmentVariable(TokenVariable, TokenValue);

        var granted = await OutcomeAsync("env.read", new JsonObject { ["name"] = TokenVariable });
        var declared = await OutcomeAsync("env.read", new JsonObject { ["name"] = "FAKE_OTHER" });
        var everythingElse = await OutcomeAsync("env.read", new JsonObject { ["name"] = "PATH" });

        Assert.Equal(TokenValue, granted.Result!["value"]!.GetValue<string>());
        Assert.Null(declared.Result!["value"]);
        Assert.Null(everythingElse.Result!["value"]);
    }

    [Fact]
    public async Task What_the_plugin_logs_is_on_stderr_with_its_secrets_masked()
    {
        Environment.SetEnvironmentVariable(TokenVariable, TokenValue);

        var (exit, stdout, stderr) = await RunAsync(
            "log.emit",
            new JsonObject { ["level"] = "warn", ["message"] = "sending", ["data"] = new JsonObject { ["token"] = TokenValue } });

        Assert.Equal(HostExitCodes.Completed, exit);
        Assert.Equal(1, ModeRun.Outcome(stdout).Diagnostics.LogLines);
        var line = Assert.Single(ModeRun.Records(stderr), record => record["source"]!.GetValue<string>() == HostDiagnostics.PluginSource);
        Assert.Equal("warn", line["level"]!.GetValue<string>());
        Assert.Equal("sending", line["message"]!.GetValue<string>());
        Assert.Equal(Redactor.Mask, line["data"]!["token"]!.GetValue<string>());
        Assert.DoesNotContain(TokenValue, string.Join('\n', stderr), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("fail.transient", FailureClass.Transient)]
    [InlineData("fail.permanent", FailureClass.Permanent)]
    [InlineData("fail.validation", FailureClass.Validation)]
    [InlineData("fail.ambiguous", FailureClass.Ambiguous)]
    public async Task A_declared_failure_carries_its_class_out_of_the_process(string operation, FailureClass expected)
    {
        var outcome = await OutcomeAsync(
            operation,
            new JsonObject
            {
                ["code"] = "rate_limited",
                ["message"] = "slow down",
                ["details"] = new JsonObject { ["retry_in"] = 5 },
                ["external_ids"] = new JsonObject { ["contact"] = "r_9" },
            });

        Assert.Equal(OutcomeStatus.Failed, outcome.Status);
        Assert.Equal(expected, outcome.Error!.Class);
        Assert.Equal("rate_limited", outcome.Error!.Code);
        Assert.Equal("slow down", outcome.Error!.Message);
        Assert.Equal(5, outcome.Error!.Details!["retry_in"]!.GetValue<int>());
        Assert.Equal("r_9", outcome.Error!.ExternalIds!["contact"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_plugin_that_only_computes_and_never_ends_failed_permanently()
    {
        var outcome = await OutcomeAsync(
            "hang.forever",
            configure: builder => builder.Limits = builder.Limits with { TimeoutMs = 500, MaxStatements = 1_000_000_000 });

        Assert.Equal(OutcomeCodes.PluginTimeout, outcome.Error!.Code);
        Assert.Equal(FailureClass.Permanent, outcome.Error!.Class);
    }

    [Fact]
    public async Task A_plugin_that_had_already_started_a_program_times_out_ambiguously()
    {
        // The canonical package cannot both call out and hang, so this one is written for the question: the
        // provider may have acted, and only the host knows that it might have.
        InvocationFactory.WritePackage(
            _written.Root,
            """
            export function invoke(operation, input) {
              host.exec({ executable: "dotnet", args: [input.dll, "echo-args", "started"] });
              for (;;) { }
            }
            """);

        var invocation = InvocationFactory.Create(
            _written.Root,
            "hang.after.exec",
            new JsonObject { ["dll"] = FakeProviderCli.Dll },
            builder =>
            {
                builder.Grants = Grants;
                builder.Limits = builder.Limits with { TimeoutMs = 3_000, MaxStatements = 1_000_000_000 };
            });

        var (exit, stdout, _) = await ModeRun.RunAsync(invocation, Ct);

        Assert.Equal(HostExitCodes.Completed, exit);
        var outcome = ModeRun.Outcome(stdout);
        Assert.Equal(OutcomeCodes.PluginTimeout, outcome.Error!.Code);
        Assert.Equal(FailureClass.Ambiguous, outcome.Error!.Class);
        Assert.Equal(1, outcome.Diagnostics.ExecCalls);
    }

    [Fact]
    public async Task A_result_too_large_for_the_protocol_is_refused_rather_than_written()
    {
        var outcome = await OutcomeAsync("spew.bytes", new JsonObject { ["bytes"] = 2_000_000 });

        Assert.Equal(OutcomeCodes.ResultTooLarge, outcome.Error!.Code);
    }

    [Fact]
    public async Task An_error_the_plugin_did_not_declare_is_an_exception()
    {
        var outcome = await OutcomeAsync("throw.plain");

        Assert.Equal(OutcomeCodes.PluginException, outcome.Error!.Code);
        Assert.Contains("boom", outcome.Error!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_answer_that_is_not_the_return_contract_is_refused()
    {
        var outcome = await OutcomeAsync("return.bare");

        Assert.Equal(OutcomeCodes.BadReturn, outcome.Error!.Code);
    }

    [Fact]
    public async Task An_operation_the_plugin_does_not_know_is_the_plugin_s_own_answer()
    {
        var outcome = await OutcomeAsync("nothing.here");

        Assert.Equal(FailureClass.Validation, outcome.Error!.Class);
        Assert.Equal("unknown_operation", outcome.Error!.Code);
    }

    [Fact]
    public async Task A_package_may_be_more_than_one_file()
    {
        var outcome = await OutcomeAsync("module.import", new JsonObject { ["value"] = "v" });

        Assert.Equal("helped:v", outcome.Result!["helper"]!.GetValue<string>());
    }

    [Fact]
    public async Task However_much_a_plugin_logs_stdout_still_carries_one_outcome()
    {
        InvocationFactory.WritePackage(
            _written.Root,
            """
            export function invoke() {
              for (let i = 0; i < 1000; i++) {
                host.log("info", "line " + i, { i });
              }

              return { result: { logged: 1000 } };
            }
            """);

        var invocation = InvocationFactory.Create(
            _written.Root,
            "log.flood",
            configure: builder => builder.Limits = builder.Limits with { Log = new LogLimits(16_384, 4096) });

        var (exit, stdout, stderr) = await ModeRun.RunAsync(invocation, Ct);

        Assert.Equal(HostExitCodes.Completed, exit);
        var outcome = ModeRun.Outcome(stdout);
        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(1000, outcome.Diagnostics.LogLines);
        Assert.Contains(stderr, line => line.Contains(HostDiagnostics.TruncationNotice, StringComparison.Ordinal));
        Assert.True(stderr.Count < 1000, "the log stops once its budget is spent");
    }
}
