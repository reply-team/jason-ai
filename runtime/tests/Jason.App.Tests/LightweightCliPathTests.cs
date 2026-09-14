using Jason.App;
using Jason.Contracts.Discovery;

namespace Jason.App.Tests;

public class LightweightCliPathTests
{
    [Fact]
    public async Task Running_a_cli_command_does_not_load_server_side_assemblies()
    {
        var root = Path.Combine(Path.GetTempPath(), "jason-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable(JasonPaths.DataDirectoryVariable);
        var originalOut = Console.Out;
        Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, root);
        Console.SetOut(new StringWriter());
        try
        {
            var exit = await ModeRouter.RunAsync(["runtime", "status"], TestContext.Current.CancellationToken);
            Assert.Equal(3, exit);
        }
        finally
        {
            Console.SetOut(originalOut);
            Environment.SetEnvironmentVariable(JasonPaths.DataDirectoryVariable, previous);
            Directory.Delete(root, recursive: true);
        }

        var loaded = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetName().Name ?? string.Empty).ToList();
        foreach (var forbidden in new[] { "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite", "Microsoft.AspNetCore", "Serilog", "Jint" })
        {
            Assert.DoesNotContain(loaded, name => name.StartsWith(forbidden, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The plugin host is the other short-lived mode of the same executable, and it carries none of the server
    /// with it: no database, no web host, no logging pipeline, and not even the YAML reader that the runtime
    /// uses to validate a manifest — the child is told what to run, it does not go and find out.
    /// </summary>
    /// <remarks>
    /// Measured as what this call added rather than as what the process holds: the end-to-end plugin test in
    /// this assembly builds the runtime's own invoker in process, which legitimately loads the YAML reader, and
    /// which of the two runs first is not this test's business. What is asserted is therefore the causal claim —
    /// entering plugin-host mode loads none of these — which is the claim worth making.
    /// </remarks>
    [Fact]
    public async Task Entering_plugin_host_mode_loads_no_server_side_assemblies()
    {
        var before = Loaded();
        var originalError = Console.Error;
        Console.SetError(new StringWriter());
        try
        {
            // No arguments is a usage error, which is the cheapest way into the mode and out of it again.
            var exit = await ModeRouter.RunAsync(["plugin-host"], TestContext.Current.CancellationToken);
            Assert.Equal(2, exit);
        }
        finally
        {
            Console.SetError(originalError);
        }

        var added = Loaded().Except(before, StringComparer.Ordinal).ToList();
        foreach (var forbidden in new[] { "Microsoft.EntityFrameworkCore", "Microsoft.Data.Sqlite", "Microsoft.AspNetCore", "Serilog", "YamlDotNet" })
        {
            Assert.DoesNotContain(added, name => name.StartsWith(forbidden, StringComparison.Ordinal));
        }
    }

    private static List<string> Loaded() =>
        [.. AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetName().Name ?? string.Empty)];
}
