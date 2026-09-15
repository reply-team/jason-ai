using Jason.Runtime.Plugins.Invocation;

namespace Jason.Runtime.Tests.Plugins;

/// <summary>
/// The shipped executable as a test can start it: the test host is not <c>jason</c>, so the process path would be
/// the test runner. The child under test is still the real thing — the same <c>jason.dll</c>, in plugin-host
/// mode — started through the runtime that copied it next to the tests.
/// </summary>
public sealed class JasonDllLocator : IPluginHostLocator
{
    public IReadOnlyList<string> Command => ["dotnet", Path.Combine(AppContext.BaseDirectory, "jason.dll")];
}
