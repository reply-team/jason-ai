using Jason.App;
using Jason.Contracts.Discovery;

namespace Jason.App.Tests;

public class ModeRouterTests
{
    [Theory]
    [InlineData(new[] { "--version" }, Mode.Version)]
    [InlineData(new[] { "runtime", "run" }, Mode.RuntimeService)]
    [InlineData(new[] { "runtime", "run", "--whatever" }, Mode.RuntimeService)]
    [InlineData(new[] { "runtime", "run", "--detached" }, Mode.RuntimeService)]
    [InlineData(new[] { "plugin-host" }, Mode.PluginHost)]
    [InlineData(new[] { "plugin-host", "fake-provider" }, Mode.PluginHost)]
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
            Assert.StartsWith("0.1.0", output.ToString().Trim(), StringComparison.Ordinal);
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

    /// <summary>A flag with no path after it stops the runtime before it starts, rather than defaulting.</summary>
    [Fact]
    public async Task A_data_dir_with_no_path_after_it_starts_no_runtime()
    {
        var original = Console.Error;
        var error = new StringWriter();
        Console.SetError(error);
        try
        {
            var exit = await ModeRouter.RunAsync(["runtime", "run", "--data-dir"], TestContext.Current.CancellationToken);
            Assert.Equal(2, exit);
            Assert.Contains("usage", error.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--data-dir", error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(original);
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
}
