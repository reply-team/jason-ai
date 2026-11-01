namespace Jason.App.Tests;

/// <summary>
/// Where the CI stand-in for an agent host lives once it has been built. The host project is referenced by this
/// test project, so its assembly is copied next to the tests; it is built framework-dependent, hence the
/// <c>dotnet</c> in front of it.
/// </summary>
internal static class FakeAgentHostLocator
{
    public static string Dll => Path.Combine(AppContext.BaseDirectory, "Jason.FakeAgentHost.dll");
}
