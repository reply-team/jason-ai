namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// The runtime's configuration never reaches an agent it launches for a role. A person may set any setting of the runtime
/// as a <c>JASON_&lt;Section&gt;__&lt;Key&gt;</c> variable, a plugin's secret under <c>Plugins:Config</c> among them, and
/// an unhappy agent host prints its environment into the transcript the person keeps: so every such override is taken
/// out of the child's environment, whatever its case, while the runtime's own single-word names and everything else the
/// runtime's environment holds stay.
/// </summary>
[Collection(ConfinementEnvironmentCollection.Name)]
public class ExecutorConfigurationEnvironmentTests
{
    private const string WorkItemId = "wi_one";
    private const string AttemptId = "att_one";

    private const string Setting = "JASON_Dispatcher__MaxParallel";
    private const string PluginSecret = "JASON_Plugins__Config__acme-provider__api_key";
    private const string LowerCaseSetting = "jason_runtime__port";
    private const string Ordinary = "ORDINARY_VARIABLE_FOR_TESTS";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    [Critical(Family.B1, "no stray variable reaches a child")]
    public async Task An_agent_the_runtime_launches_is_handed_no_configuration_override_and_no_plugin_secret()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);
        string[] names = [Setting, PluginSecret, LowerCaseSetting, Ordinary];
        var original = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Setting, "3");
            Environment.SetEnvironmentVariable(PluginSecret, "made-up-secret-value");
            Environment.SetEnvironmentVariable(LowerCaseSetting, "49999");
            Environment.SetEnvironmentVariable(Ordinary, "kept");

            await AiRoleCommandTests.RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"));
        }
        finally
        {
            foreach (var (name, value) in original)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);

        // Not one override, the plugin's secret and the one spelled in lower case included.
        Assert.Contains("configuration-in-env=none", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("made-up-secret-value", stderr, StringComparison.Ordinal);

        // The four are still there, and so is what the runtime's environment holds that is not its configuration.
        Assert.Contains("data-dir=set", stderr, StringComparison.Ordinal);
        Assert.Contains($"attempt-in-env={AttemptId}", stderr, StringComparison.Ordinal);
        Assert.Contains($"work-item-in-env={WorkItemId}", stderr, StringComparison.Ordinal);
        Assert.Contains($"path-head={ProgramResolverDirectory()}", stderr, StringComparison.Ordinal);
        Assert.Contains("ordinary-in-env=kept", stderr, StringComparison.Ordinal);
    }

    private static string ProgramResolverDirectory() =>
        Jason.Runtime.Execution.Hosts.ProgramResolver.ExecutableDirectory
        ?? throw new InvalidOperationException("This build publishes no directory of its own.");
}
