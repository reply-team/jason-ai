namespace Jason.Runtime.Tests.Plugins.Invocation;

/// <summary>
/// The tests that set the globalization mode of this whole process, kept away from everything else in this assembly while
/// they run: a .NET program any other test started meanwhile would inherit the mode, so a runtime would refuse to start
/// and a plugin host would compose nothing.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GlobalizationModeCollection
{
    public const string Name = "globalization mode";
}

/// <summary>
/// The globalization mode this runtime runs in reaches its plugin host, a real child process, and the program that host
/// starts: the mode decides how a letter is lowercased and composed, so a host given another would key a value as its
/// runtime does not.
/// </summary>
[Collection(GlobalizationModeCollection.Name)]
public class PluginInvokerGlobalizationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Invariant mode is the one value set here that changes anything, and it is also what keeps the made-up ICU version
    /// from being loaded by any .NET program started meanwhile, which would end it.
    /// </summary>
    [Fact]
    public async Task The_globalization_mode_of_the_runtime_reaches_its_plugin_host_and_what_it_starts()
    {
        (string Name, string Value)[] mode =
        [
            ("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "true"),
            ("DOTNET_SYSTEM_GLOBALIZATION_USENLS", "true"),
            ("DOTNET_SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY", "false"),
            ("DOTNET_SYSTEM_GLOBALIZATION_APPLOCALICU", "68.2.0.9"),
        ];
        var was = mode.Select(variable => (variable.Name, Environment.GetEnvironmentVariable(variable.Name))).ToList();
        try
        {
            foreach (var (name, value) in mode)
            {
                Environment.SetEnvironmentVariable(name, value);
            }

            await using var api = await PluginInvokerTests.StartAsync();

            foreach (var (name, value) in mode)
            {
                var printed = await PluginInvokerTests.InvokeAsync(api, PluginInvokerTests.Request("exec.run", PluginInvokerTests.Exec(name)), Ct);
                var seen = PluginInvokerTests.Stdout(printed);
                Assert.True(seen == value, $"{name} reached the program the plugin host started as '{seen}'.");
            }
        }
        finally
        {
            foreach (var (name, value) in was)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
