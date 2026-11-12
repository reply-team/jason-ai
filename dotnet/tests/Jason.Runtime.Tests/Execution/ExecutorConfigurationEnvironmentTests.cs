using Jason.Contracts.Plugins;
using Jason.Contracts.Ids;
using Jason.Runtime.Plugins.Registry;
using Jason.Runtime.Tests.Plugins;

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

    /// <summary>
    /// A name the active snapshot grants a plugin carries that plugin's credential and is taken from a role's child; one
    /// the machine list holds stays, granted or not, so granting a machine variable never takes it from an agent.
    /// </summary>
    [Fact]
    [Critical(Family.B1, "no stray variable reaches a child")]
    public async Task A_name_granted_to_a_plugin_never_reaches_an_agent_unless_the_machine_list_holds_it()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);
        var home = OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME";
        Assert.Contains(home, BaseEnvironment.ForCurrentOs);
        Assert.False(string.IsNullOrEmpty(Environment.GetEnvironmentVariable(home)), $"This test needs {home} in its own environment.");

        var registry = new PluginRegistry(TimeProvider.System);
        var plugin = TestPlugins.Loaded("acme-provider") with { Grants = new ResolvedGrants([], [], [Granted, home], []) };
        registry.Replace(
            new PluginSnapshot(PublicId.New(PluginProtocol.SnapshotIdPrefix), DateTime.UnixEpoch, SnapshotSource.Startup, [plugin]),
            new ReloadReport(DateTime.UnixEpoch, SnapshotSource.Startup, Activated: true, [], []));

        var original = Environment.GetEnvironmentVariable(Granted);
        try
        {
            Environment.SetEnvironmentVariable(Granted, "made-up-granted-token");

            await AiRoleCommandTests.RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"), plugins: registry);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Granted, original);
        }

        var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);
        Assert.Contains("granted-in-env=unset", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("made-up-granted-token", stderr, StringComparison.Ordinal);
        Assert.Contains("home-in-env=set", stderr, StringComparison.Ordinal);
        Assert.Contains($"attempt-in-env={AttemptId}", stderr, StringComparison.Ordinal);
    }

    /// <summary>With no plugin granted anything, a variable of that name is the person's and reaches the agent.</summary>
    [Fact]
    public async Task With_no_plugins_a_variable_of_a_granted_kind_still_reaches_the_agent()
    {
        using var directory = new TempDataDir();
        var workDir = directory.Paths.AttemptWorkDirectory(WorkItemId, AttemptId);
        var original = Environment.GetEnvironmentVariable(Granted);
        try
        {
            Environment.SetEnvironmentVariable(Granted, "kept");

            await AiRoleCommandTests.RunAsync(directory.Paths, workDir, FakeAgentHost.EntryCommand("silent"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Granted, original);
        }

        var stderr = await File.ReadAllTextAsync(Path.Combine(workDir, "stderr.log"), Ct);
        Assert.Contains("granted-in-env=kept", stderr, StringComparison.Ordinal);
        Assert.Contains("configuration-in-env=none", stderr, StringComparison.Ordinal);
    }

    private const string Granted = "GRANTED_TOKEN_FOR_TESTS";

    private static string ProgramResolverDirectory() =>
        Jason.Runtime.Execution.Hosts.ProgramResolver.ExecutableDirectory
        ?? throw new InvalidOperationException("This build publishes no directory of its own.");
}
