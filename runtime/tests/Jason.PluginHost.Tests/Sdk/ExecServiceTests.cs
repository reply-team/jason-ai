using System.Text.Json.Nodes;
using Jason.Contracts.Plugins;
using Jason.PluginHost.Sdk;
using Jason.PluginHost.Tests.Fixtures;
using Jint;

namespace Jason.PluginHost.Tests.Sdk;

public sealed class ExecServiceTests : IDisposable
{
    private const string Cli = "fake-cli";

    private readonly TempPackage _package = new();

    public void Dispose() => _package.Dispose();

    private static InvocationGrants Granted { get; } =
        new(new ExecGrants([new ExecutableGrant(Cli, FakeProviderCli.ExecutablePath), new ExecutableGrant("dotnet", Executables.Dotnet)]), null, null);

    private SdkHarness Harness(Action<InvocationBuilder>? configure = null) =>
        new(_package.Root, builder =>
        {
            builder.Grants = Granted;
            configure?.Invoke(builder);
        });

    private static JsonObject Exec(SdkHarness harness, string request) =>
        (JsonObject)JsJson.ToJson(harness.Engine, harness.Evaluate("host.exec(" + request + ")"))!;

    private static string Lines(JsonObject result) => result["stdout"]!.GetValue<string>().ReplaceLineEndings("\n");

    [Fact]
    public void A_granted_program_runs_with_the_arguments_it_was_given()
    {
        using var harness = Harness();

        var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"echo-args\", \"a\", \"b c\"] }}");

        Assert.Equal(0, result["exit_code"]!.GetValue<int>());
        Assert.Equal("a\nb c\n", Lines(result));
        Assert.False(result["timed_out"]!.GetValue<bool>());
        Assert.False(result["truncated"]!["stdout"]!.GetValue<bool>());
    }

    [Fact]
    public void The_dll_of_a_program_is_just_another_argument()
    {
        using var harness = Harness();

        var result = Exec(harness, $"{{ executable: \"dotnet\", args: [{JsonValue.Create(FakeProviderCli.Dll)!.ToJsonString()}, \"echo-args\", \"hi\"] }}");

        Assert.Equal(0, result["exit_code"]!.GetValue<int>());
        Assert.Equal("hi\n", Lines(result));
    }

    [Fact]
    public void An_exit_code_comes_back_as_it_is()
    {
        using var harness = Harness();

        var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"exit\", \"7\"] }}");

        Assert.Equal(7, result["exit_code"]!.GetValue<int>());
        Assert.Contains("exiting", result["stderr"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public void Standard_input_reaches_the_program_and_is_then_closed()
    {
        using var harness = Harness();

        var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"stdin-length\"], stdin: \"x\".repeat(10000) }}");

        Assert.Equal("10000\n", Lines(result));
    }

    [Fact]
    public void Output_beyond_the_cap_is_cut_and_the_cut_is_flagged()
    {
        using var harness = Harness(builder => builder.Limits = builder.Limits with { Exec = new ExecLimits(65_536, 64) });

        var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"spew\", \"5000000\"] }}");

        Assert.True(result["truncated"]!["stdout"]!.GetValue<bool>());
        Assert.Equal(65_536, result["stdout"]!.GetValue<string>().Length);
    }

    [Fact]
    public void A_program_that_outstays_its_timeout_is_killed_and_the_call_says_so()
    {
        using var harness = Harness();

        var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"sleep\", \"30000\"], timeout_ms: 300 }}");

        Assert.True(result["timed_out"]!.GetValue<bool>());
        Assert.True(result["duration_ms"]!.GetValue<long>() < 10_000, "the kill did not wait for the program");
    }

    /// <summary>
    /// The program leaves a helper running that the kill cannot reach, and that helper still holds the output
    /// pipes open. Whatever was captured by the end of the kill grace is the answer: the call must not wait for
    /// a program it has already ended to stop being referred to by something else. The call is made off the test
    /// thread only so that failing this rule fails the test rather than stopping the run.
    /// </summary>
    [Fact]
    public async Task Something_the_killed_program_left_behind_does_not_hold_the_call_open()
    {
        using var harness = Harness();
        var token = TestContext.Current.CancellationToken;

        var call = Task.Run(
            () => Exec(harness, $"{{ executable: \"{Cli}\", args: [\"spawn-orphan\", \"30000\"], timeout_ms: 500 }}"),
            token);
        var result = await call.WaitAsync(TimeSpan.FromSeconds(20), token);

        Assert.True(result["timed_out"]!.GetValue<bool>());
    }

    /// <summary>
    /// The same helper, left behind by a program that exited cleanly and of its own accord. The pipes it holds
    /// are no more finished than after a kill, and the call has the same nothing to wait for: a plugin is owed
    /// an answer whichever way the program it ran ended.
    /// </summary>
    [Fact]
    public async Task Something_a_program_left_behind_does_not_hold_a_clean_exit_open()
    {
        using var harness = Harness();
        var token = TestContext.Current.CancellationToken;

        // The program starts the lingering one and exits at once, successfully and within milliseconds.
        var call = Task.Run(
            () => Exec(harness, $"{{ executable: \"{Cli}\", args: [\"spawn-orphan-inner\", \"30000\"] }}"),
            token);
        var result = await call.WaitAsync(TimeSpan.FromSeconds(20), token);

        Assert.Equal(0, result["exit_code"]!.GetValue<int>());
        Assert.False(result["timed_out"]!.GetValue<bool>());
    }

    [Fact]
    public void A_variable_the_plugin_adds_reaches_that_child_and_nothing_else()
    {
        using var harness = Harness();

        var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"print-env\", \"FAKE_EXEC_VALUE\"], env: {{ FAKE_EXEC_VALUE: \"bar\" }} }}");

        Assert.Equal("bar\n", Lines(result));
        Assert.Null(Environment.GetEnvironmentVariable("FAKE_EXEC_VALUE"));
    }

    [Fact]
    public void A_reserved_variable_is_not_a_plugin_s_to_set()
    {
        using var harness = Harness();

        Assert.Equal("TypeError", harness.Caught($"host.exec({{ executable: \"{Cli}\", args: [\"echo-args\"], env: {{ JASON_X: \"1\" }} }})"));
    }

    /// <summary>
    /// A granted program is a decision about that program, not about whatever the plugin would like to load into
    /// it. Each of these names makes an operating system, a runtime or an interpreter run code of the setter's
    /// choosing before the program's own first line, so none of them is a plugin's to set.
    /// </summary>
    [Theory]
    [InlineData("LD_PRELOAD")]
    [InlineData("LD_LIBRARY_PATH")]
    [InlineData("LD_AUDIT")]
    [InlineData("DYLD_INSERT_LIBRARIES")]
    [InlineData("DYLD_LIBRARY_PATH")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("DOTNET_ROOT")]
    [InlineData("CORECLR_PROFILER_PATH")]
    [InlineData("COMPlus_ETWEnabled")]
    [InlineData("NODE_OPTIONS")]
    [InlineData("NODE_PATH")]
    [InlineData("PYTHONPATH")]
    [InlineData("PYTHONSTARTUP")]
    [InlineData("RUBYOPT")]
    [InlineData("PERL5OPT")]
    [InlineData("JAVA_TOOL_OPTIONS")]
    [InlineData("_JAVA_OPTIONS")]
    [InlineData("CLASSPATH")]
    [InlineData("PATH")]
    [InlineData("PATHEXT")]
    [InlineData("COMSPEC")]
    [InlineData("SHELL")]
    [InlineData("ld_preload")]
    public void A_loader_or_interpreter_hook_is_not_a_plugin_s_to_set(string variable)
    {
        using var harness = Harness();

        Assert.Equal("TypeError", harness.Caught($"host.exec({{ executable: \"{Cli}\", args: [\"echo-args\"], env: {{ {variable}: \"anything\" }} }})"));
    }

    [Fact]
    public void A_refused_variable_never_reaches_the_program()
    {
        using var harness = Harness();

        harness.Caught($"host.exec({{ executable: \"{Cli}\", args: [\"print-env\", \"NODE_OPTIONS\"], env: {{ NODE_OPTIONS: \"--require=./evil.js\" }} }})");

        // Nothing ran, so nothing can have been loaded: the refusal is before the process, not inside it.
        var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"print-env\", \"NODE_OPTIONS\"] }}");
        Assert.Equal("<unset>\n", Lines(result));
    }

    /// <summary>
    /// A name ending in a line break. The anchor `$` matches before a trailing one, so such a name satisfied the
    /// name rule, and the denylist — which compares whole names — did not recognise it either: two rules missing
    /// the same string for the same reason. The name is built here rather than parsed from anything.
    /// </summary>
    [Theory]
    [InlineData("FAKE_EXEC_VALUE")]
    [InlineData("NODE_OPTIONS")]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    public void A_name_with_a_line_break_after_it_is_not_a_variable_name(string stem)
    {
        using var harness = Harness();

        Assert.Equal(
            "TypeError",
            harness.Caught($"host.exec({{ executable: \"{Cli}\", args: [\"echo-args\"], env: {{ \"{stem}\\n\": \"anything\" }} }})"));
    }

    /// <summary>
    /// The refusal has a name of its own, and a <c>TypeError</c> has nowhere to put it but the message — so the
    /// message carries it, and an author who reads one can search the guide for the rule that produced it.
    /// </summary>
    [Fact]
    public void A_refused_variable_names_the_code_an_author_can_look_up()
    {
        using var harness = Harness();

        var message = harness.Message($"host.exec({{ executable: \"{Cli}\", args: [\"echo-args\"], env: {{ NODE_OPTIONS: \"x\" }} }})");

        Assert.Contains(ExecService.EnvNotAllowedCode, message, StringComparison.Ordinal);
        Assert.Contains("NODE_OPTIONS", message, StringComparison.Ordinal);
    }

    /// <summary>The denylist compares names the way an operating system does, so a different case is the same name.</summary>
    [Theory]
    [InlineData("Ld_Preload")]
    [InlineData("Node_Options")]
    [InlineData("dotnet_startup_hooks")]
    public void A_denylisted_variable_in_another_case_is_the_same_variable(string variable)
    {
        using var harness = Harness();

        Assert.Equal("TypeError", harness.Caught($"host.exec({{ executable: \"{Cli}\", args: [\"echo-args\"], env: {{ {variable}: \"anything\" }} }})"));
    }

    [Fact]
    public void An_ordinary_variable_of_a_runtime_that_has_hooks_is_still_a_plugin_s_to_set()
    {
        using var harness = Harness();

        var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"print-env\", \"NODE_ENV\"], env: {{ NODE_ENV: \"production\" }} }}");

        Assert.Equal("production\n", Lines(result));
    }

    [Theory]
    [InlineData("sh")]
    [InlineData("cmd")]
    [InlineData("C:\\\\Windows\\\\System32\\\\cmd.exe")]
    [InlineData("/bin/sh")]
    [InlineData("./fake-cli")]
    public void Anything_but_a_granted_name_is_refused(string executable)
    {
        using var harness = Harness();

        var refused = Assert.Throws<HostRuleException>(() => harness.Evaluate($"host.exec({{ executable: \"{executable}\" }})"));

        Assert.Equal(OutcomeCodes.ExecutableNotAllowed, refused.Code);
    }

    [Fact]
    public void Without_the_capability_nothing_starts()
    {
        using var harness = new SdkHarness(_package.Root);

        var refused = Assert.Throws<HostRuleException>(() => harness.Evaluate($"host.exec({{ executable: \"{Cli}\" }})"));

        Assert.Equal(OutcomeCodes.CapabilityNotGranted, refused.Code);
        Assert.Equal("exec", refused.Details!["capability"]!.GetValue<string>());
        Assert.Equal(Cli, refused.Details!["requested"]!.GetValue<string>());
    }

    [Fact]
    public void An_invocation_may_start_only_so_many_programs()
    {
        using var harness = Harness(builder => builder.Limits = builder.Limits with { Exec = new ExecLimits(4_194_304, 2) });

        Exec(harness, $"{{ executable: \"{Cli}\", args: [\"echo-args\", \"one\"] }}");
        Exec(harness, $"{{ executable: \"{Cli}\", args: [\"echo-args\", \"two\"] }}");
        var refused = Assert.Throws<HostRuleException>(() => harness.Evaluate($"host.exec({{ executable: \"{Cli}\", args: [\"echo-args\"] }})"));

        Assert.Equal(OutcomeCodes.ExecLimit, refused.Code);
        Assert.Equal(2, harness.Budget.ExecCalls);
    }

    [Fact]
    public void A_started_program_is_what_makes_a_later_timeout_ambiguous()
    {
        using var harness = Harness();

        Exec(harness, $"{{ executable: \"{Cli}\", args: [\"echo-args\", \"one\"] }}");

        Assert.Equal(1, harness.Budget.ExternalCallsStarted);
    }

    [Fact]
    public void Every_program_the_plugin_starts_is_on_the_record()
    {
        using var harness = Harness();

        Exec(harness, $"{{ executable: \"{Cli}\", args: [\"exit\", \"3\"] }}");

        var line = Assert.Single(harness.Lines);
        Assert.Equal("exec", line["message"]!.GetValue<string>());
        Assert.Equal(Cli, line["data"]!["executable"]!.GetValue<string>());
        Assert.Equal(3, line["data"]!["exit_code"]!.GetValue<int>());
        Assert.Equal("exit", line["data"]!["args"]![0]!.GetValue<string>());
    }

    [Fact]
    public void An_option_the_call_does_not_have_is_a_type_error()
    {
        using var harness = Harness();

        Assert.Equal("TypeError", harness.Caught($"host.exec({{ executable: \"{Cli}\", cwd: \"/tmp\" }})"));
        Assert.Equal("TypeError", harness.Caught("host.exec({ })"));
        Assert.Equal("TypeError", harness.Caught($"host.exec({{ executable: \"{Cli}\", args: \"echo-args\" }})"));
        Assert.Equal("TypeError", harness.Caught($"host.exec({{ executable: \"{Cli}\", timeout_ms: 0 }})"));
    }

    [Fact]
    public void A_key_the_plugin_left_undefined_is_simply_absent()
    {
        using var harness = Harness();

        var result = Exec(harness, $"{{ executable: \"{Cli}\", args: [\"echo-args\", \"a\"], stdin: undefined, timeout_ms: undefined, env: undefined }}");

        Assert.Equal(0, result["exit_code"]!.GetValue<int>());
        Assert.Equal("a\n", Lines(result));
    }
}
