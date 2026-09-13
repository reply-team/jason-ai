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
}
