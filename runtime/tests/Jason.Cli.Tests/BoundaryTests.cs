using Jason.Cli;

namespace Jason.Cli.Tests;

public class BoundaryTests
{
    [Theory]
    [InlineData("Jason.Runtime")]
    [InlineData("Jason.PluginHost")]
    [InlineData("Microsoft.EntityFrameworkCore")]
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
