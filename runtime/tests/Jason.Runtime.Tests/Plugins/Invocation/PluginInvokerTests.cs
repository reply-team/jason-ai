using System.Diagnostics;
using System.Text.Json.Nodes;
using Jason.Contracts;
using Jason.Contracts.Api;
using Jason.Contracts.Discovery;
using Jason.Contracts.Plugins;
using Jason.Runtime.Execution;
using Jason.Runtime.Plugins.Invocation;
using Jason.Runtime.Plugins.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// The invoker driving the shipped executable in plugin-host mode: a real child process, the real fixture
/// package, and the real protocol between them. What matters here is that the runtime never runs a line of the
/// plugin's code, that the child is told exactly what the user granted, and that every way an invocation can
/// end comes back named.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public class PluginInvokerTests
{
    /// <summary>The variable the fixture package declares; its value is the test's own, and never a real secret.</summary>
    private const string TokenVariable = "FAKE_TOKEN";

    private const string UngrantedVariable = "FAKE_OTHER";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_invocation_runs_the_shipped_executable_and_brings_back_the_plugin_s_answer()
    {
        await using var api = await StartAsync();
        var listed = await api.PostOkAsync<PluginRegistryDto>(Operations.PluginList, null, Ct);

        var result = await InvokeAsync(api, Request("echo.run", new JsonObject { ["hello"] = "world" }), Ct);

        var succeeded = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal("world", succeeded.Result!["echo"]!["hello"]!.GetValue<string>());

        // The answer is evidence only if its origin can be named.
        Assert.Equal(TestPlugins.FakeProviderId, result.Provenance.PluginId);
        Assert.Equal("1.0.0", result.Provenance.Version);
        Assert.Equal(Assert.Single(listed.Plugins).Digest, result.Provenance.Digest);
        Assert.Equal(listed.Snapshot.Id, result.Provenance.SnapshotId);
        Assert.Equal(PluginProtocol.CurrentVersion, result.Provenance.ProtocolVersion);
        Assert.Equal(PluginProtocol.OperationContractVersion, result.Provenance.OperationContractVersion);
        Assert.StartsWith("pin_", result.Provenance.InvocationId, StringComparison.Ordinal);

        Assert.True(result.Launch!.Pid > 0);
        Assert.Equal(0, result.Launch.ExitCode);
        Assert.Equal(
            ["--protocol", "1", "--plugin", "fake-provider", "--operation", "echo.run", "--correlation", result.Provenance.CorrelationId],
            result.Launch.Command.TakeLast(8));
    }

    [Fact]
    public async Task The_plugin_is_told_who_is_asking_and_what_the_caller_bound_to_the_call()
    {
        await using var api = await StartAsync();

        var result = await InvokeAsync(api, new PluginInvocationRequest(
            TestPlugins.FakeProviderId,
            "context.echo",
            new JsonObject(),
            new JsonObject { ["profile"] = "default" },
            "att_01K0CONTEXT",
            AttemptId: "att_01K0CONTEXT",
            AttemptNumber: 2,
            WorkItemId: "wi_01K0",
            CampaignId: "cmp_01K0"),
            Ct);

        var context = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).Result!;
        Assert.Equal(2, context["attempt_number"]!.GetValue<int>());
        Assert.Equal("att_01K0CONTEXT", context["correlation_id"]!.GetValue<string>());
        Assert.Equal("wi_01K0", context["work_item_id"]!.GetValue<string>());
        Assert.Equal("default", context["binding"]!["profile"]!.GetValue<string>());
        Assert.Equal(JasonVersion.Current, context["runtime_version"]!.GetValue<string>());
        Assert.Equal(result.Provenance.InvocationId, context["invocation_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_program_the_plugin_starts_sees_its_granted_variable_and_nothing_of_the_runtime()
    {
        var token = "token-" + Guid.NewGuid().ToString("N");
        var unlisted = "FAKE_UNLISTED_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

        // Both are set in this process, which is what makes the child's answer mean something: the environment
        // it runs under was built from nothing, not inherited and filtered afterwards.
        Environment.SetEnvironmentVariable(TokenVariable, token);
        Environment.SetEnvironmentVariable(unlisted, "inherited-if-this-leaks");
        Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, "inherited-if-this-leaks");
        try
        {
            await using var api = await StartAsync();

            var dataDirectory = await InvokeAsync(api, Request("exec.run", Exec(JasonPaths.DataDirectoryVariable)), Ct);
            var unrelated = await InvokeAsync(api, Request("exec.run", Exec(unlisted)), Ct);
            var granted = await InvokeAsync(api, Request("exec.run", Exec(TokenVariable)), Ct);

            Assert.Equal("<unset>", Stdout(dataDirectory));
            Assert.Equal("<unset>", Stdout(unrelated));
            Assert.Equal(token, Stdout(granted));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenVariable, null);
            Environment.SetEnvironmentVariable(unlisted, null);
            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, null);
        }
    }

    [Fact]
    public async Task A_variable_the_manifest_declares_but_nobody_granted_reads_as_nothing()
    {
        Environment.SetEnvironmentVariable(UngrantedVariable, "present-but-not-granted");
        try
        {
            await using var api = await StartAsync();

            var result = await InvokeAsync(api, Request("env.read", new JsonObject { ["name"] = UngrantedVariable }), Ct);

            var value = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).Result!["value"];
            Assert.Null(value);
        }
        finally
        {
            Environment.SetEnvironmentVariable(UngrantedVariable, null);
        }
    }

    [Fact]
    public async Task What_the_plugin_writes_to_stderr_is_kept_redacted_and_alone_in_the_working_directory()
    {
        var token = "token-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(TokenVariable, token);
        try
        {
            await using var api = await StartAsync();

            var result = await InvokeAsync(api, Request("log.emit", new JsonObject { ["message"] = "the token is " + token }), Ct);

            Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
            var workDir = result.Launch!.WorkDir;
            Assert.Equal("stderr.log", Path.GetFileName(Assert.Single(Directory.GetFiles(workDir))));
            var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);
            Assert.Contains(Redactor.Mask, stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(token, stderr, StringComparison.Ordinal);

            // Never the invocation and never the outcome: the input may hold contact data and the binding.
            Assert.Empty(Directory.GetDirectories(workDir));
        }
        finally
        {
            Environment.SetEnvironmentVariable(TokenVariable, null);
        }
    }

    [Fact]
    public async Task The_runtime_process_never_loads_the_engine_that_runs_the_plugin()
    {
        await using var api = await StartAsync();

        Assert.IsType<InvocationOutcome.Succeeded>((await InvokeAsync(api, Request("echo.run", new JsonObject()), Ct)).Outcome);

        Assert.DoesNotContain(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly => string.Equals(assembly.GetName().Name, "Jint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_declared_failure_comes_back_with_the_class_the_dispatcher_will_read()
    {
        await using var api = await StartAsync();

        var transient = await InvokeAsync(api, Request("fail.transient", new JsonObject { ["code"] = "rate_limited" }), Ct);
        var ambiguous = await InvokeAsync(api, Request("fail.ambiguous", new JsonObject { ["code"] = "maybe_sent" }), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(transient.Outcome);
        Assert.Equal(FailureClass.Transient, failed.Error.Class);
        Assert.Equal("rate_limited", failed.Error.Code);
        Assert.True(OutcomeClassification.IsRetriable(failed.Error.Class));
        Assert.Equal(0, transient.Launch!.ExitCode);

        var unsure = Assert.IsType<InvocationOutcome.Failed>(ambiguous.Outcome);
        Assert.Equal(FailureClass.Ambiguous, unsure.Error.Class);
        Assert.False(OutcomeClassification.IsRetriable(unsure.Error.Class));
    }

    [Fact]
    public async Task A_plugin_the_caller_pinned_runs_even_after_the_registry_has_moved_on()
    {
        await using var api = await StartAsync();
        using var scope = api.Runtime.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<PluginRegistry>();
        var decided = registry.Snapshot;
        var pinned = new PinnedPlugin(decided.Find(TestPlugins.FakeProviderId)!, decided.Id);

        // A reload lands between the decision and the attempt, and this one leaves nothing installed at all.
        registry.Replace(
            PluginSnapshot.Empty(DateTime.UtcNow, SnapshotSource.Reload),
            new ReloadReport(DateTime.UtcNow, SnapshotSource.Reload, Activated: true, [], []));

        var result = await InvokeAsync(api, Request("echo.run", new JsonObject { ["hello"] = "world" }) with { Pinned = pinned }, Ct);

        var succeeded = Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome);
        Assert.Equal("world", succeeded.Result!["echo"]!["hello"]!.GetValue<string>());

        // The provenance names the decision that was acted on, not whatever the registry holds now.
        Assert.Equal(decided.Id, result.Provenance.SnapshotId);
        Assert.Equal(pinned.Plugin.Digest, result.Provenance.Digest);
        Assert.Equal(pinned.Plugin.Manifest.Version, result.Provenance.Version);

        // Without the pin the same call is refused, which is what makes the pin the thing under test.
        var unpinned = await InvokeAsync(api, Request("echo.run", new JsonObject()), Ct);
        Assert.Equal(ProtocolCodes.PluginNotLoaded, Assert.IsType<InvocationOutcome.ProtocolFailure>(unpinned.Outcome).Code);
    }

    [Fact]
    public async Task A_pinned_plugin_is_held_to_every_rule_an_unpinned_one_is()
    {
        await using var api = await StartAsync(paths => TestPlugins.Write(
            paths,
            "narrow",
            TestPlugins.Manifest("narrow", operations: "[other.thing]"),
            "export function invoke() { return { result: {} }; }"));
        using var scope = api.Runtime.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<PluginRegistry>();
        var snapshot = registry.Snapshot;
        var pinned = new PinnedPlugin(snapshot.Find("narrow")!, snapshot.Id);

        var result = await InvokeAsync(api, Request("echo.run", new JsonObject(), plugin: "narrow") with { Pinned = pinned }, Ct);

        // Pinning says which package runs, never that it may do something its manifest does not offer.
        Assert.Equal(ProtocolCodes.PluginOperationUnsupported, Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome).Code);
        Assert.Null(result.Launch);
    }

    [Fact]
    public async Task A_plugin_that_is_not_in_the_snapshot_is_refused_before_anything_starts()
    {
        await using var api = await StartAsync();

        var result = await InvokeAsync(api, Request("echo.run", new JsonObject(), plugin: "no-such-plugin"), Ct);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginNotLoaded, failure.Code);
        Assert.Null(result.Launch);
        Assert.Equal("no-such-plugin", result.Provenance.PluginId);
        Assert.Null(result.Provenance.Digest);
        Assert.Equal(FailureClass.Permanent, OutcomeClassification.ClassOf(failure.Code));
    }

    [Fact]
    public async Task A_plugin_whose_program_this_machine_does_not_have_is_refused_with_its_problem_named()
    {
        await using var api = await StartAsync(paths => TestPlugins.Write(
            paths,
            "needs-a-tool",
            TestPlugins.Manifest("needs-a-tool", extra: "capabilities:\n  exec:\n    executables:\n      - name: not-installed-anywhere\n"),
            "export function invoke() { return { result: {} }; }"));

        var result = await InvokeAsync(api, Request("echo.run", new JsonObject(), plugin: "needs-a-tool"), Ct);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginUnavailable, failure.Code);
        Assert.Contains("executable_missing", failure.Message, StringComparison.Ordinal);
        Assert.Null(result.Launch);
    }

    [Fact]
    public async Task A_capability_the_manifest_asks_for_but_nobody_granted_is_refused_as_not_granted()
    {
        // Declared, resolvable, and granted nothing: the plugin must hear "not granted", not "this program is
        // not on your list" — the list exists only once a grant does.
        await using var api = await StartAsync(paths => TestPlugins.Write(
            paths,
            "ungranted",
            TestPlugins.Manifest("ungranted", operations: "[exec.run]", extra: $"capabilities:\n  exec:\n    executables:\n      - name: {FakeProviderCli.ExecutableName}\n"),
            "export function invoke(operation, input) { return { result: host.exec({ executable: input.executable, args: input.args }) }; }"));

        var result = await InvokeAsync(api, Request("exec.run", Exec(TokenVariable), plugin: "ungranted"), Ct);

        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("capability_not_granted", failed.Error.Code);
        Assert.Equal(FailureClass.Permanent, failed.Error.Class);
        Assert.Equal("exec", failed.Error.Details!["capability"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_plugin_of_a_kind_nothing_invokes_is_refused()
    {
        await using var api = await StartAsync(paths => TestPlugins.Write(
            paths,
            "notifier",
            """
            manifest_version: 1
            id: notifier
            version: 0.2.0
            kind: notification
            contracts:
              protocol: [1]
            """,
            "export function invoke() { return { result: {} }; }"));

        var result = await InvokeAsync(api, Request("echo.run", new JsonObject(), plugin: "notifier"), Ct);

        Assert.Equal(ProtocolCodes.PluginKindNotInvocable, Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome).Code);
        Assert.Null(result.Launch);
    }

    [Fact]
    public async Task An_operation_the_plugin_does_not_implement_is_refused()
    {
        await using var api = await StartAsync(paths => TestPlugins.Write(
            paths,
            "narrow",
            TestPlugins.Manifest("narrow", operations: "[other.thing]"),
            "export function invoke() { return { result: {} }; }"));

        var result = await InvokeAsync(api, Request("echo.run", new JsonObject(), plugin: "narrow"), Ct);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginOperationUnsupported, failure.Code);
        Assert.Contains("echo.run", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_package_changed_after_the_reload_is_refused_by_the_child_itself()
    {
        await using var api = await StartAsync();
        var main = Path.Combine(api.Paths.PluginPackageDirectory(TestPlugins.FakeProviderId), "main.js");
        await File.AppendAllTextAsync(main, "\n// edited after the runtime validated this package\n", Ct);

        var result = await InvokeAsync(api, Request("echo.run", new JsonObject()), Ct);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginInvocationRejected, failure.Code);
        Assert.Contains("digest_mismatch", failure.StderrTail!, StringComparison.Ordinal);
        Assert.Equal(3, result.Launch!.ExitCode);
        Assert.Equal(FailureClass.Permanent, OutcomeClassification.ClassOf(failure.Code));
    }

    /// <summary>
    /// A pin says which package runs, and between the decision and the child there is a filesystem. The child
    /// recomputes what it was told to run before running any of it, so a package that has gone missing since is
    /// refused there rather than half-run — and the runtime reports it as a rejection, not as an answer.
    /// </summary>
    [Fact]
    public async Task A_pinned_package_that_is_gone_by_the_time_the_child_starts_is_refused_by_the_child()
    {
        await using var api = await StartAsync();
        PinnedPlugin pinned;
        using (var scope = api.Runtime.Services.CreateScope())
        {
            var snapshot = scope.ServiceProvider.GetRequiredService<PluginRegistry>().Snapshot;
            pinned = new PinnedPlugin(snapshot.Find(TestPlugins.FakeProviderId)!, snapshot.Id);
        }

        Directory.Delete(api.Paths.PluginPackageDirectory(TestPlugins.FakeProviderId), recursive: true);

        var result = await InvokeAsync(api, Request("echo.run", new JsonObject()) with { Pinned = pinned }, Ct);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginInvocationRejected, failure.Code);
        Assert.Contains("package_root_missing", failure.StderrTail!, StringComparison.Ordinal);
        Assert.Equal(3, result.Launch!.ExitCode);
        Assert.Equal(FailureClass.Permanent, OutcomeClassification.ClassOf(failure.Code));
    }

    [Fact]
    public async Task An_input_larger_than_an_envelope_may_carry_starts_nothing()
    {
        await using var api = await StartAsync();

        var result = await InvokeAsync(api, Request("echo.run", new JsonObject { ["big"] = new string('x', PluginProtocol.MaxInputBytes) }), Ct);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginInputTooLarge, failure.Code);
        Assert.Null(result.Launch);
        Assert.Equal(FailureClass.Permanent, OutcomeClassification.ClassOf(failure.Code));
    }

    [Fact]
    public async Task A_binding_larger_than_an_envelope_may_carry_starts_nothing()
    {
        await using var api = await StartAsync();

        var result = await InvokeAsync(api, new PluginInvocationRequest(
            TestPlugins.FakeProviderId,
            "echo.run",
            new JsonObject(),
            new JsonObject { ["big"] = new string('b', PluginProtocol.MaxBindingBytes) },
            "att_01K0BINDING"),
            Ct);

        Assert.Equal(ProtocolCodes.PluginBindingTooLarge, Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome).Code);
        Assert.Null(result.Launch);
    }

    [Fact]
    public async Task A_correlation_id_that_would_travel_on_argv_is_the_caller_s_bug()
    {
        await using var api = await StartAsync();
        using var scope = api.Runtime.Services.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<PluginInvoker>();

        await Assert.ThrowsAsync<ArgumentException>(() => invoker.InvokeAsync(
            new PluginInvocationRequest(TestPlugins.FakeProviderId, "echo.run", new JsonObject(), null, "not a correlation id"),
            Ct));
    }

    [Fact]
    public async Task A_plugin_that_never_ends_is_ended_by_its_own_budget()
    {
        await using var api = await StartAsync(settings: PatientEngine);

        var result = await InvokeAsync(api, Request("hang.forever", new JsonObject(), timeout: TimeSpan.FromSeconds(1)), Ct);

        // The child enforces the budget itself and reports it as a business answer: the protocol completed.
        var failed = Assert.IsType<InvocationOutcome.Failed>(result.Outcome);
        Assert.Equal("plugin_timeout", failed.Error.Code);
        Assert.Equal(0, result.Launch!.ExitCode);
    }

    [Fact]
    public async Task A_cancelled_invocation_ends_the_child_and_says_so()
    {
        await using var api = await StartAsync(settings: PatientEngine);
        using var kill = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var result = await InvokeAsync(api, Request("hang.forever", new JsonObject(), timeout: TimeSpan.FromSeconds(30)), kill.Token);

        var failure = Assert.IsType<InvocationOutcome.ProtocolFailure>(result.Outcome);
        Assert.Equal(ProtocolCodes.PluginKilled, failure.Code);
        AssertGone(result.Launch!.Pid!.Value);
        Assert.Equal(FailureClass.Ambiguous, OutcomeClassification.ClassOf(failure.Code));
    }

    /// <summary>
    /// A busy loop spends statements, not time, so the engine's statement budget would end it long before its
    /// timeout. Raising that budget is what makes the timeout the thing under test.
    /// </summary>
    private const string PatientEngine = """{"Dispatcher":{"Enabled":false},"Plugins":{"Limits":{"MaxStatements":1000000000}}}""";

    internal static Task<RuntimeApiFixture> StartAsync(Action<JasonPaths>? extra = null, string? settings = null) =>
        RuntimeApiFixture.StartAsync(
            Ct,
            prepare: paths =>
            {
                File.WriteAllText(paths.UserSettingsFile, settings ?? RuntimeApiFixture.DispatcherOff);
                TestPlugins.InstallFakeProvider(paths);

                // The fixture declares the stand-in vendor CLI by its own name, and host.exec has to start a
                // real program, so the search path is the real one with that program's directory in front.
                TestPlugins.Grant(paths, TestPlugins.FakeProviderId, exec: ["*"], env: [TokenVariable]);
                extra?.Invoke(paths);
            },
            configureServices: services =>
            {
                services.AddSingleton<IPluginHostLocator>(new JasonDllLocator());
                services.AddSingleton(TestPlugins.SearchPath);
            });

    internal static async Task<PluginInvocationResult> InvokeAsync(
        RuntimeApiFixture api,
        PluginInvocationRequest request,
        CancellationToken kill)
    {
        using var scope = api.Runtime.Services.CreateScope();
        var invoker = scope.ServiceProvider.GetRequiredService<PluginInvoker>();
        return await invoker.InvokeAsync(request, kill);
    }

    internal static PluginInvocationRequest Request(
        string operation,
        JsonObject input,
        string? plugin = null,
        TimeSpan? timeout = null) =>
        new(plugin ?? TestPlugins.FakeProviderId, operation, input, null, "att_01K0" + operation.Replace('.', '_'), Timeout: timeout);

    private static JsonObject Exec(string variable) => new()
    {
        ["executable"] = FakeProviderCli.ExecutableName,
        ["args"] = new JsonArray("print-env", variable),
    };

    private static string Stdout(PluginInvocationResult result) =>
        Assert.IsType<InvocationOutcome.Succeeded>(result.Outcome).Result!["stdout"]!.GetValue<string>().Trim();

    private static void AssertGone(int pid)
    {
        try
        {
            using var child = Process.GetProcessById(pid);
            Assert.True(child.HasExited, "the child outlived the kill");
        }
        catch (ArgumentException)
        {
            // Gone entirely, which is the same answer.
        }
    }
}
