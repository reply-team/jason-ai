using System.Text.Json;
using System.Text.Json.Nodes;
using Jason.Contracts.Json;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Tests.Fixtures;

namespace Jason.PluginHost.Tests;

[Collection(ModeCollection.Name)]
public sealed class PluginHostModeTests : IDisposable
{
    private readonly IInvocationRunner _original = PluginHostMode.Runner;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jason-plugin-host-tests", Guid.NewGuid().ToString("N"));

    public PluginHostModeTests() =>
        InvocationFactory.WritePackage(_root, "export function invoke() { return { result: {} }; }");

    public void Dispose()
    {
        PluginHostMode.Runner = _original;
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] Argv(string pluginId = "fake-provider", string operation = "echo.run", int protocol = 1) =>
        ["--protocol", protocol.ToString(System.Globalization.CultureInfo.InvariantCulture), "--plugin", pluginId, "--operation", operation, "--correlation", "test"];

    private static async Task<(int Exit, string Out, string Error)> RunAsync(string[] args, PluginInvocation? invocation = null)
    {
        using var stdin = new StringReader(invocation is null ? string.Empty : JsonSerializer.Serialize(invocation, JasonJson.Options));
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var exit = await PluginHostMode.RunAsync(args, stdin, stdout, stderr, Ct);
        return (exit, stdout.ToString(), stderr.ToString());
    }

    private sealed class StubRunner(Func<PluginInvocation, PluginOutcome> run) : IInvocationRunner
    {
        public int Calls { get; private set; }

        public PluginOutcome Run(PluginInvocation invocation, HostDiagnostics diagnostics, CancellationToken deadline)
        {
            Calls++;
            return run(invocation);
        }
    }

    private static StubRunner Succeeding() => new(invocation => new PluginOutcome(
        PluginProtocol.CurrentVersion,
        invocation.InvocationId,
        OutcomeStatus.Succeeded,
        JsonNode.Parse("""{"ok":true}"""),
        null,
        null,
        new OutcomeDiagnostics(1, 0, 0, 0)));

    [Fact]
    public async Task Without_arguments_it_is_a_usage_error_and_stdout_stays_empty()
    {
        var (exit, stdout, stderr) = await RunAsync([]);

        Assert.Equal(HostExitCodes.Usage, exit);
        Assert.Contains("usage", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stdout);
    }

    [Fact]
    public async Task A_protocol_this_build_does_not_speak_is_a_usage_error()
    {
        var (exit, stdout, stderr) = await RunAsync(Argv(protocol: 2));

        Assert.Equal(HostExitCodes.Usage, exit);
        Assert.Contains("protocol", stderr, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(string.Empty, stdout);
    }

    [Fact]
    public async Task An_empty_stdin_is_a_rejected_invocation()
    {
        var (exit, stdout, stderr) = await RunAsync(Argv());

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"envelope_unreadable\"", stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout);
    }

    [Fact]
    public async Task Something_that_is_not_an_envelope_is_a_rejected_invocation()
    {
        using var stdin = new StringReader("not json at all");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exit = await PluginHostMode.RunAsync(Argv(), stdin, stdout, stderr, Ct);

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"envelope_invalid\"", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_envelope_that_disagrees_with_the_command_line_is_refused()
    {
        PluginHostMode.Runner = Succeeding();

        var (exit, _, stderr) = await RunAsync(Argv(pluginId: "other"), InvocationFactory.Create(_root, "echo.run"));

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"argv_mismatch\"", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_envelope_from_another_protocol_is_refused()
    {
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.ProtocolVersion = 99);

        var (exit, _, stderr) = await RunAsync(Argv(), invocation);

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"protocol_unsupported\"", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_package_that_changed_since_the_runtime_validated_it_is_never_run()
    {
        var runner = Succeeding();
        PluginHostMode.Runner = runner;
        var invocation = InvocationFactory.Create(_root, "echo.run");
        File.WriteAllText(Path.Combine(_root, "main.js"), "export function invoke() { return { result: { tampered: true } }; }");

        var (exit, stdout, stderr) = await RunAsync(Argv(), invocation);

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"digest_mismatch\"", stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout);
        Assert.Equal(0, runner.Calls);
    }

    [Fact]
    public async Task A_hand_written_envelope_without_a_digest_runs_and_the_host_says_it_was_unverified()
    {
        var runner = Succeeding();
        PluginHostMode.Runner = runner;
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Digest = null);

        var (exit, stdout, stderr) = await RunAsync(Argv(), invocation);

        Assert.Equal(HostExitCodes.Completed, exit);
        Assert.Contains("digest_unverified", stderr, StringComparison.Ordinal);
        Assert.Equal(1, runner.Calls);
        Assert.NotEqual(string.Empty, stdout);
    }

    [Fact]
    public async Task A_missing_package_or_entry_module_is_refused()
    {
        var invocation = InvocationFactory.Create(_root, "echo.run", configure: builder => builder.Entry = new PluginEntry("absent.js", "invoke"));

        var (exit, _, stderr) = await RunAsync(Argv(), invocation);

        Assert.Equal(HostExitCodes.Rejected, exit);
        Assert.Contains("\"code\":\"entry_module_missing\"", stderr, StringComparison.Ordinal);

        var gone = Path.Combine(Path.GetTempPath(), "jason-plugin-host-tests", Guid.NewGuid().ToString("N"));
        var missing = InvocationFactory.Create(_root, "echo.run") with
        {
            Plugin = new InvocationPlugin("fake-provider", "1.0.0", PluginKind.Provider, gone, null, new PluginEntry("main.js", "invoke")),
        };

        var (exitMissing, _, stderrMissing) = await RunAsync(Argv(), missing);

        Assert.Equal(HostExitCodes.Rejected, exitMissing);
        Assert.Contains("\"code\":\"package_root_missing\"", stderrMissing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_runner_that_throws_is_the_host_failing_rather_than_the_plugin()
    {
        PluginHostMode.Runner = new StubRunner(_ => throw new InvalidOperationException("the host broke"));

        var (exit, stdout, stderr) = await RunAsync(Argv(), InvocationFactory.Create(_root, "echo.run"));

        Assert.Equal(HostExitCodes.HostFailure, exit);
        Assert.Contains("the host broke", stderr, StringComparison.Ordinal);
        Assert.Equal(string.Empty, stdout);
    }

    [Fact]
    public async Task A_completed_invocation_writes_exactly_one_outcome_and_exits_zero()
    {
        var invocation = InvocationFactory.Create(_root, "echo.run");
        PluginHostMode.Runner = Succeeding();

        var (exit, stdout, _) = await RunAsync(Argv(), invocation);

        Assert.Equal(HostExitCodes.Completed, exit);
        var outcome = JsonSerializer.Deserialize<PluginOutcome>(stdout, JasonJson.Options)!;
        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.Equal(invocation.InvocationId, outcome.InvocationId);
        Assert.Equal(PluginProtocol.CurrentVersion, outcome.ProtocolVersion);
        Assert.False(stdout.TrimEnd().Contains('\n'), "stdout carries exactly one JSON document");
    }

    [Fact]
    public async Task The_runner_is_handed_the_envelope_it_was_given()
    {
        PluginInvocation? seen = null;
        PluginHostMode.Runner = new StubRunner(invocation =>
        {
            seen = invocation;
            return new PluginOutcome(PluginProtocol.CurrentVersion, invocation.InvocationId, OutcomeStatus.Succeeded, null, null, null, new OutcomeDiagnostics(0, 0, 0, 0));
        });
        var invocation = InvocationFactory.Create(_root, "echo.run", new JsonObject { ["value"] = "hello" });

        await RunAsync(Argv(), invocation);

        Assert.NotNull(seen);
        Assert.Equal("hello", seen.Input["value"]!.GetValue<string>());
        Assert.Equal(_root, seen.Plugin.Root);
    }
}
