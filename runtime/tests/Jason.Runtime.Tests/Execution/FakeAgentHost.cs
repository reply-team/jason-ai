namespace Jason.Runtime.Tests.Execution;

/// <summary>
/// Where the CI stand-in for an agent host lives once it has been built. Every test that needs a real child
/// process to run an attempt starts it through this entry command, so nothing has to know how the project is
/// laid out on disk.
/// </summary>
public static class FakeAgentHost
{
    /// <summary>
    /// The host is referenced by this test project, so its assembly is copied next to the tests. It is built
    /// framework-dependent and started through the shared host, hence the <c>dotnet</c> in front of it.
    /// </summary>
    public static string Dll => Path.Combine(AppContext.BaseDirectory, "Jason.FakeAgentHost.dll");

    /// <summary>The entry command a role would be configured with: the host, then the behaviour and its options.</summary>
    public static IReadOnlyList<string> EntryCommand(params string[] behaviour)
    {
        ArgumentNullException.ThrowIfNull(behaviour);
        return ["dotnet", Dll, .. behaviour];
    }
}
