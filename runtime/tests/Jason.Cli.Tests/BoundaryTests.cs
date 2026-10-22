using Jason.Cli;
using Jason.Runtime.Tests;

namespace Jason.Cli.Tests;

public class BoundaryTests
{
    [Theory]
    // Critical: no Jason.Runtime, no EF Core in the CLI.
    [InlineData("Jason.Runtime", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.B2)])]
    [InlineData("Jason.PluginHost")]
    // Critical: no Jason.Runtime, no EF Core in the CLI.
    [InlineData("Microsoft.EntityFrameworkCore", Traits = ["Category", "Critical", "CriticalFamily", nameof(Family.B2)])]
    [InlineData("Microsoft.Data.Sqlite")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("YamlDotNet")]
    [InlineData("Jint")]
    public void The_cli_never_references_server_side_assemblies(string forbiddenPrefix)
    {
        var references = typeof(CliApp).Assembly.GetReferencedAssemblies().Select(a => a.Name!);
        Assert.DoesNotContain(references, name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
    }
}
