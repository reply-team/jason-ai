using Jason.App;
using Jason.Contracts.Discovery;
using Jason.Contracts.Update;
using Jason.Runtime.Execution.Hosts;
using Jason.Runtime.Tests;

namespace Jason.App.Tests;

public class ModeRouterTests
{
    [Theory]
    [InlineData(new[] { "--version" }, Mode.Version)]
    // Critical: one executable, the mode from argv.
    [InlineData(new[] { "runtime", "run" }, Mode.RuntimeService, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.B2)])]
    [InlineData(new[] { "runtime", "run", "--whatever" }, Mode.RuntimeService)]
    [InlineData(new[] { "runtime", "run", "--detached" }, Mode.RuntimeService)]
    // Critical: one executable, the mode from argv.
    [InlineData(new[] { "plugin-host" }, Mode.PluginHost, Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.B2)])]
    [InlineData(new[] { "plugin-host", "fake-provider" }, Mode.PluginHost)]
    // The mode follows the first argument whatever comes after it; no test names the protocol the host answers.
    [InlineData(new[] { "toast-host", "--protocol", "2" }, Mode.ToastHost)]
    [InlineData(new[] { "toast-host" }, Mode.ToastHost)]
    // The agent host's hook is a mode of its own, chosen by its first word alone.
    [InlineData(new[] { "host-hook", "antigravity-cli", "e30" }, Mode.HostHook)]
    [InlineData(new[] { "host-hook" }, Mode.HostHook)]
    [InlineData(new[] { "runtime", "host-hook" }, Mode.Cli)]
    [InlineData(new[] { "runtime", "toast-host" }, Mode.Cli)]
    [InlineData(new[] { "runtime", "status" }, Mode.Cli)]
    [InlineData(new[] { "--help" }, Mode.Cli)]
    [InlineData(new string[0], Mode.Cli)]
    [InlineData(new[] { "campaign", "create" }, Mode.Cli)]
    public void Selects_the_mode_from_the_leading_arguments(string[] args, Mode expected) =>
        Assert.Equal(expected, ModeRouter.Select(args));

    [Fact]
    public async Task Version_prints_the_build_version_and_exits_0()
    {
        var original = Console.Out;
        var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            var exit = await ModeRouter.RunAsync(["--version"], TestContext.Current.CancellationToken);
            Assert.Equal(0, exit);
            Assert.StartsWith(TreeVersion.Prefix(), output.ToString().Trim(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    /// <summary>
    /// Which data directory a run owns. The flag wins over the variable because the thing that starts a runtime
    /// at logon has no environment to put a variable in: a Windows logon task carries none, so naming the
    /// directory on the command line is the only way to say it, and a registration that says it must not be
    /// overruled by whatever the session it happens to run in has set.
    /// </summary>
    [Fact]
    public void The_data_directory_flag_wins_over_the_variable()
    {
        var original = Environment.GetEnvironmentVariable(JasonPaths.DataDirectoryVariable);
        var elsewhere = Path.Combine(Path.GetTempPath(), "jason-from-the-environment");
        var named = Path.Combine(Path.GetTempPath(), "jason-from-the-flag");
        try
        {
            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, elsewhere);

            Assert.Equal(
                Path.GetFullPath(named),
                ModeRouter.PathsFor(RuntimeRunArguments.Parse(["runtime", "run", "--data-dir", named])).Root);

            // And with no flag it is still the variable's, which is how every runtime started by this CLI runs.
            Assert.Equal(
                Path.GetFullPath(elsewhere),
                ModeRouter.PathsFor(RuntimeRunArguments.Parse(["runtime", "run"])).Root);
        }
        finally
        {
            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, original);
        }
    }

    /// <summary>
    /// The update's mark is read once and taken out of the runtime's own environment, so no child the runtime starts
    /// inherits it: a plugin host, an agent or a toast host that started a runtime of its own would otherwise start it as
    /// the update's.
    /// </summary>
    [Fact]
    public void The_runtime_mode_takes_the_update_mark_out_of_its_own_environment()
    {
        var original = Environment.GetEnvironmentVariable(UpdateLaunch.Variable);
        try
        {
            Environment.SetEnvironmentVariable(UpdateLaunch.Variable, UpdateLaunch.At(UpdateStep.Started));

            Assert.Equal("started", ModeRouter.TakeUpdateLaunch());
            Assert.Null(Environment.GetEnvironmentVariable(UpdateLaunch.Variable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(UpdateLaunch.Variable, original);
        }
    }

    /// <summary>A flag with no path after it stops the runtime before it starts, rather than defaulting.</summary>
    /// <remarks>
    /// Belt and braces, because of what this test does when the thing it guards is not there: it calls the
    /// runtime mode for real. With the usage refusal removed it does not fail — it starts an in-process runtime
    /// on <b>the default data directory</b> and waits there. Measured: <c>~/.jason</c> created in a home
    /// directory, with a database, a descriptor, a log, and an update check on its way to the internet once the
    /// first delay was up. So the variable points at this test's own tree, and the run is given thirty seconds before
    /// it is cancelled: the usage refusal ends the run at once, so the bound is only a guard against a hang, sized to the
    /// slowest runner's start; a regression fails in a temporary directory and reaches nothing, since the update check's
    /// first delay is five minutes.
    /// </remarks>
    [Fact]
    public async Task A_data_dir_with_no_path_after_it_starts_no_runtime()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable(JasonPaths.DataDirectoryVariable);
        var original = Console.Error;
        var error = new StringWriter();

        Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, root);
        Console.SetError(error);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var exit = await ModeRouter.RunAsync(["runtime", "run", "--data-dir"], bounded.Token);
            Assert.Equal(2, exit);
            Assert.Contains("usage", error.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--data-dir", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(original);
            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, previous);
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Plugin_host_without_arguments_is_a_usage_error()
    {
        var original = Console.Error;
        var error = new StringWriter();
        Console.SetError(error);
        try
        {
            var exit = await ModeRouter.RunAsync(["plugin-host"], TestContext.Current.CancellationToken);
            Assert.Equal(2, exit);
            Assert.Contains("usage", error.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    /// <summary>
    /// The hook mode answers one tool call with one line, and nothing of what it was asked or of the environment is in
    /// it: the command it was asked about and a variable that names a data directory are both poisoned with a marker, and
    /// the line must not hold it.
    /// </summary>
    [Fact]
    public async Task The_hook_mode_answers_one_line_that_holds_nothing_of_the_call_or_the_environment()
    {
        const string Marker = "marker-5d21c8-made-up";
        var originalInput = ModeRouter.HostHookInput;
        var originalOut = Console.Out;
        var output = new StringWriter();
        var previous = Environment.GetEnvironmentVariable(JasonPaths.DataDirectoryVariable);
        var rules = AntigravityHookRules.Encode(new AntigravityHookRules("jason", [], [], [], Path.Combine(Path.GetTempPath(), "jason-app-tests", "result.json")));
        var call = new System.Text.Json.Nodes.JsonObject
        {
            ["toolCall"] = new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = "run_command",
                ["args"] = new System.Text.Json.Nodes.JsonObject { ["CommandLine"] = "jason workitem get " + Marker },
            },
        }.ToJsonString();

        Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, Path.Combine(Path.GetTempPath(), Marker));
        ModeRouter.HostHookInput = () => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(call));
        Console.SetOut(output);
        try
        {
            var exit = await ModeRouter.RunAsync(["host-hook", "antigravity-cli", rules], TestContext.Current.CancellationToken);

            Assert.Equal(0, exit);
            var line = output.ToString();
            Assert.DoesNotContain(Marker, line, StringComparison.Ordinal);
            using var document = System.Text.Json.JsonDocument.Parse(line);
            Assert.Equal("auto_approve", document.RootElement.GetProperty("decision").GetString());
            Assert.Single(line.Trim().Split('\n'));
        }
        finally
        {
            ModeRouter.HostHookInput = originalInput;
            Console.SetOut(originalOut);
            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, previous);
        }
    }

    /// <summary>The hook mode reads its call as UTF-8 bytes, so a work directory with letters outside ASCII is matched as the host wrote it.</summary>
    [Fact]
    public async Task The_hook_mode_matches_a_path_with_letters_outside_ascii()
    {
        var originalInput = ModeRouter.HostHookInput;
        var originalOut = Console.Out;
        var output = new StringWriter();
        var path = Path.Combine(Path.GetTempPath(), "jason-app-tests", "Прівіт-ü", "result.json");
        var rules = AntigravityHookRules.Encode(new AntigravityHookRules("jason", [], [], [], path));
        var call = new System.Text.Json.Nodes.JsonObject
        {
            ["toolCall"] = new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = "write_to_file",
                ["args"] = new System.Text.Json.Nodes.JsonObject { ["TargetFile"] = path },
            },
        }.ToJsonString(new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        ModeRouter.HostHookInput = () => new MemoryStream(System.Text.Encoding.UTF8.GetBytes(call));
        Console.SetOut(output);
        try
        {
            var exit = await ModeRouter.RunAsync(["host-hook", "antigravity-cli", rules], TestContext.Current.CancellationToken);

            Assert.Equal(0, exit);
            using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal("auto_approve", document.RootElement.GetProperty("decision").GetString());
        }
        finally
        {
            ModeRouter.HostHookInput = originalInput;
            Console.SetOut(originalOut);
        }
    }
}
